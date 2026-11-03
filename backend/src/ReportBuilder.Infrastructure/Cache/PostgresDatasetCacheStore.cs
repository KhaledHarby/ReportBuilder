using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Query;
using ReportBuilder.Core.Services;

namespace ReportBuilder.Infrastructure.Cache;

public sealed class PostgresDatasetCacheStore
{
    private readonly PostgresCacheClient _postgres;
    private readonly ILogger<PostgresDatasetCacheStore> _logger;

    public PostgresDatasetCacheStore(
        PostgresCacheClient postgres,
        ILogger<PostgresDatasetCacheStore> logger)
    {
        _postgres = postgres;
        _logger = logger;
    }

    public async Task<PostgresStoreResult> ReplaceAsync(
        SqlConnection sourceConnection,
        Dataset dataset,
        CacheProviderConnection connection,
        CancellationToken cancellationToken)
    {
        var normalized = PostgresCacheClient.Normalize(connection);
        PostgresCacheClient.Validate(normalized);
        var schema = DatasetCacheRouting.CacheSchema;
        var staging = DatasetCacheRouting.PostgresStagingTableNameFor(dataset.Id);
        var final = DatasetCacheRouting.PostgresTableNameFor(dataset.Id);
        var source = DatasetSource.UncachedFrom(dataset);

        await _postgres.ExecuteAsync(normalized, $"CREATE SCHEMA IF NOT EXISTS {Quote(schema)}", cancellationToken);
        await DropTableAsync(normalized, schema, staging, cancellationToken);

        await using var command = sourceConnection.CreateCommand();
        command.CommandText = $"SELECT * FROM {source}";
        command.CommandTimeout = 600;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = Describe(reader);
        if (columns.Count == 0)
        {
            throw new ReportQueryException("The source table has no columns to cache.");
        }

        var create = new StringBuilder();
        create.Append("CREATE TABLE ").Append(Qualify(schema, staging)).Append(" (");
        create.AppendJoin(", ", columns.Select(column => $"{Quote(column.Name)} {column.SqlType}"));
        create.Append(')');
        await _postgres.ExecuteAsync(normalized, create.ToString(), cancellationToken);

        try
        {
            var rowCount = await InsertAsync(normalized, schema, staging, columns, reader, cancellationToken);
            await _postgres.ExecuteAsync(
                normalized,
                $"""
                BEGIN;
                DROP TABLE IF EXISTS {Qualify(schema, final)};
                ALTER TABLE {Qualify(schema, staging)} RENAME TO {Quote(final)};
                COMMIT;
                """,
                cancellationToken);

            await AnalyzeAndIndexAsync(normalized, schema, final, dataset, cancellationToken);

            long? sizeBytes = null;
            try
            {
                sizeBytes = await _postgres.ExecuteScalarLongAsync(
                    normalized,
                    $"SELECT pg_total_relation_size({Literal(Qualify(schema, final))}::regclass)",
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogInformation(ex, "Could not estimate PostgreSQL table size for {Table}.", final);
            }

            return new PostgresStoreResult(final, rowCount, sizeBytes);
        }
        catch
        {
            try
            {
                await DropTableAsync(normalized, schema, staging, CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogInformation(ex, "Could not drop PostgreSQL staging table {Table}.", staging);
            }

            throw;
        }
    }

    public async Task DropDatasetTablesAsync(
        CacheProviderConnection connection,
        Guid datasetId,
        CancellationToken cancellationToken)
    {
        var normalized = PostgresCacheClient.Normalize(connection);
        if (string.IsNullOrWhiteSpace(normalized.Host)
            && string.IsNullOrWhiteSpace(normalized.ConnectionString))
        {
            return;
        }

        var schema = DatasetCacheRouting.CacheSchema;
        await DropTableAsync(normalized, schema, DatasetCacheRouting.PostgresStagingTableNameFor(datasetId), cancellationToken);
        await DropTableAsync(normalized, schema, DatasetCacheRouting.PostgresTableNameFor(datasetId), cancellationToken);
    }

    private async Task DropTableAsync(
        CacheProviderConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken)
    {
        await _postgres.ExecuteAsync(
            connection,
            $"DROP TABLE IF EXISTS {Qualify(schema, table)}",
            cancellationToken);
    }

    private async Task<long> InsertAsync(
        CacheProviderConnection connection,
        string schema,
        string table,
        IReadOnlyList<PostgresColumn> columns,
        SqlDataReader reader,
        CancellationToken cancellationToken)
    {
        var copy = new StringBuilder("COPY ")
            .Append(Qualify(schema, table))
            .Append(" (")
            .AppendJoin(", ", columns.Select(column => Quote(column.Name)))
            .Append(") FROM STDIN (FORMAT BINARY)");

        await using var db = await _postgres.OpenConnectionAsync(connection, cancellationToken);
        try
        {
            await using var importer = await db.BeginBinaryImportAsync(copy.ToString(), cancellationToken);
            var total = 0L;
            while (await reader.ReadAsync(cancellationToken))
            {
                await importer.StartRowAsync(cancellationToken);
                for (var i = 0; i < columns.Count; i++)
                {
                    await WriteCellAsync(importer, reader, i, columns[i], cancellationToken);
                }

                total++;
            }

            await importer.CompleteAsync(cancellationToken);
            return total;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ReportQueryException)
        {
            throw new ReportQueryException(
                "Could not copy rows into PostgreSQL. " + Truncate(ex.GetBaseException().Message, 240));
        }
    }

    private static List<PostgresColumn> Describe(SqlDataReader reader)
    {
        var columns = new List<PostgresColumn>(reader.FieldCount);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"column_{i}";
            }

            var unique = name;
            var suffix = 2;
            while (!used.Add(unique))
            {
                unique = $"{name}_{suffix++}";
            }

            var (sqlType, npgsqlType) = PostgresTypeFor(reader.GetFieldType(i));
            columns.Add(new PostgresColumn(unique, sqlType, npgsqlType));
        }

        return columns;
    }

    private static (string SqlType, NpgsqlDbType NpgsqlType) PostgresTypeFor(Type type)
    {
        var inner = Nullable.GetUnderlyingType(type) ?? type;
        return inner switch
        {
            _ when inner == typeof(byte) || inner == typeof(sbyte) => ("smallint", NpgsqlDbType.Smallint),
            _ when inner == typeof(short) => ("smallint", NpgsqlDbType.Smallint),
            _ when inner == typeof(int) => ("integer", NpgsqlDbType.Integer),
            _ when inner == typeof(long) => ("bigint", NpgsqlDbType.Bigint),
            _ when inner == typeof(bool) => ("boolean", NpgsqlDbType.Boolean),
            _ when inner == typeof(float) => ("real", NpgsqlDbType.Real),
            _ when inner == typeof(double) => ("double precision", NpgsqlDbType.Double),
            _ when inner == typeof(decimal) => ("numeric", NpgsqlDbType.Numeric),
            _ when inner == typeof(Guid) => ("uuid", NpgsqlDbType.Uuid),
            _ when inner == typeof(DateTime) => ("timestamp", NpgsqlDbType.Timestamp),
            _ when inner == typeof(DateTimeOffset) => ("timestamptz", NpgsqlDbType.TimestampTz),
            _ when inner == typeof(DateOnly) => ("date", NpgsqlDbType.Date),
            _ when inner == typeof(TimeOnly) || inner == typeof(TimeSpan) => ("time", NpgsqlDbType.Time),
            _ when inner == typeof(byte[]) => ("bytea", NpgsqlDbType.Bytea),
            _ => ("text", NpgsqlDbType.Text)
        };
    }

    private static async Task WriteCellAsync(
        NpgsqlBinaryImporter importer,
        SqlDataReader reader,
        int ordinal,
        PostgresColumn column,
        CancellationToken cancellationToken)
    {
        if (reader.IsDBNull(ordinal))
        {
            await importer.WriteNullAsync(cancellationToken);
            return;
        }

        var value = ConvertValue(reader, ordinal, column);
        switch (column.NpgsqlType)
        {
            case NpgsqlDbType.Smallint:
                await importer.WriteAsync((short)value, NpgsqlDbType.Smallint, cancellationToken);
                break;
            case NpgsqlDbType.Integer:
                await importer.WriteAsync((int)value, NpgsqlDbType.Integer, cancellationToken);
                break;
            case NpgsqlDbType.Bigint:
                await importer.WriteAsync((long)value, NpgsqlDbType.Bigint, cancellationToken);
                break;
            case NpgsqlDbType.Boolean:
                await importer.WriteAsync((bool)value, NpgsqlDbType.Boolean, cancellationToken);
                break;
            case NpgsqlDbType.Real:
                await importer.WriteAsync((float)value, NpgsqlDbType.Real, cancellationToken);
                break;
            case NpgsqlDbType.Double:
                await importer.WriteAsync((double)value, NpgsqlDbType.Double, cancellationToken);
                break;
            case NpgsqlDbType.Numeric:
                await importer.WriteAsync((decimal)value, NpgsqlDbType.Numeric, cancellationToken);
                break;
            case NpgsqlDbType.Uuid:
                await importer.WriteAsync((Guid)value, NpgsqlDbType.Uuid, cancellationToken);
                break;
            case NpgsqlDbType.Timestamp:
                await importer.WriteAsync((DateTime)value, NpgsqlDbType.Timestamp, cancellationToken);
                break;
            case NpgsqlDbType.TimestampTz:
                await importer.WriteAsync((DateTimeOffset)value, NpgsqlDbType.TimestampTz, cancellationToken);
                break;
            case NpgsqlDbType.Date:
                await importer.WriteAsync((DateOnly)value, NpgsqlDbType.Date, cancellationToken);
                break;
            case NpgsqlDbType.Time:
                await importer.WriteAsync((TimeSpan)value, NpgsqlDbType.Time, cancellationToken);
                break;
            case NpgsqlDbType.Bytea:
                await importer.WriteAsync((byte[])value, NpgsqlDbType.Bytea, cancellationToken);
                break;
            default:
                await importer.WriteAsync((string)value, NpgsqlDbType.Text, cancellationToken);
                break;
        }
    }

    private static object ConvertValue(SqlDataReader reader, int ordinal, PostgresColumn column)
    {
        var value = reader.GetValue(ordinal);
        return column.NpgsqlType switch
        {
            NpgsqlDbType.Smallint => Convert.ToInt16(value, CultureInfo.InvariantCulture),
            NpgsqlDbType.Integer => Convert.ToInt32(value, CultureInfo.InvariantCulture),
            NpgsqlDbType.Bigint => Convert.ToInt64(value, CultureInfo.InvariantCulture),
            NpgsqlDbType.Boolean => Convert.ToBoolean(value, CultureInfo.InvariantCulture),
            NpgsqlDbType.Real => Convert.ToSingle(value, CultureInfo.InvariantCulture),
            NpgsqlDbType.Double => Convert.ToDouble(value, CultureInfo.InvariantCulture),
            NpgsqlDbType.Numeric => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
            NpgsqlDbType.Uuid => value is Guid guid ? guid : Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!),
            NpgsqlDbType.Timestamp => value is DateTime dt ? DateTime.SpecifyKind(dt, DateTimeKind.Unspecified) : Convert.ToDateTime(value, CultureInfo.InvariantCulture),
            NpgsqlDbType.TimestampTz => value is DateTimeOffset dto ? dto : new DateTimeOffset(Convert.ToDateTime(value, CultureInfo.InvariantCulture), TimeSpan.Zero),
            NpgsqlDbType.Date => value is DateOnly date ? date : DateOnly.FromDateTime(Convert.ToDateTime(value, CultureInfo.InvariantCulture)),
            NpgsqlDbType.Time => value switch
            {
                TimeOnly time => time.ToTimeSpan(),
                TimeSpan span => span,
                _ => TimeOnly.FromDateTime(Convert.ToDateTime(value, CultureInfo.InvariantCulture)).ToTimeSpan()
            },
            NpgsqlDbType.Bytea => value is byte[] bytes ? bytes : Encoding.UTF8.GetBytes(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private async Task AnalyzeAndIndexAsync(
        CacheProviderConnection connection,
        string schema,
        string table,
        Dataset dataset,
        CancellationToken cancellationToken)
    {
        try
        {
            await _postgres.ExecuteAsync(connection, $"ANALYZE {Qualify(schema, table)}", cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogInformation(ex, "Could not ANALYZE PostgreSQL cache table {Table}.", table);
        }

        var marked = dataset.Columns
            .Where(column => !column.IsHidden && column.IsIndexed && IsIndexableSqlType(column.SqlType))
            .OrderBy(column => column.Ordinal)
            .ToList();
        var columns = marked.Count > 0
            ? marked
            : dataset.Columns
                .Where(column => !column.IsHidden && IsIndexableSqlType(column.SqlType))
                .OrderBy(column => IndexRank(column.SqlType))
                .ThenBy(column => column.Ordinal)
                .Take(16)
                .ToList();

        for (var i = 0; i < columns.Count; i++)
        {
            var indexName = Truncate($"ix_{table}_{i}", 63);
            try
            {
                await _postgres.ExecuteAsync(
                    connection,
                    $"CREATE INDEX IF NOT EXISTS {Quote(indexName)} ON {Qualify(schema, table)} ({Quote(columns[i].ColumnName)})",
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogInformation(
                    ex,
                    "Could not create PostgreSQL cache index on {Column} for {Table}.",
                    columns[i].ColumnName,
                    table);
            }
        }
    }

    private static bool IsIndexableSqlType(string sqlType)
    {
        var type = (sqlType ?? string.Empty).Trim().ToLowerInvariant();
        if (type.Contains("max", StringComparison.Ordinal)
            || type is "text" or "ntext" or "image" or "xml" or "sql_variant"
            or "geography" or "geometry" or "hierarchyid" or "timestamp" or "rowversion")
        {
            return false;
        }

        var paren = type.IndexOf('(', StringComparison.Ordinal);
        if (paren >= 0)
        {
            type = type[..paren];
        }

        type = type.Trim();
        return type is "int" or "bigint" or "smallint" or "tinyint" or "bit"
            or "uniqueidentifier"
            or "date" or "datetime" or "datetime2" or "smalldatetime" or "time"
            or "nvarchar" or "varchar" or "nchar" or "char" or "sysname"
            or "decimal" or "numeric" or "money" or "smallmoney";
    }

    private static int IndexRank(string sqlType)
    {
        var type = (sqlType ?? string.Empty).Trim().ToLowerInvariant();
        var paren = type.IndexOf('(', StringComparison.Ordinal);
        if (paren >= 0)
        {
            type = type[..paren];
        }

        type = type.Trim();
        return type switch
        {
            "uniqueidentifier" or "int" or "bigint" => 0,
            "smallint" or "tinyint" or "bit" => 1,
            "date" or "datetime" or "datetime2" or "smalldatetime" => 2,
            _ => 3
        };
    }

    private static string Quote(string name)
        => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string Qualify(string schema, string table)
        => $"{Quote(schema)}.{Quote(table)}";

    private static string Literal(string value)
        => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    private readonly record struct PostgresColumn(string Name, string SqlType, NpgsqlDbType NpgsqlType);
}

public readonly record struct PostgresStoreResult(string TableName, long RowCount, long? SizeBytes);
