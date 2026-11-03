using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Query;
using ReportBuilder.Core.Services;

namespace ReportBuilder.Infrastructure.Cache;

public sealed class ClickHouseDatasetCacheStore
{
    private const int InsertBatchRows = 8_000;
    private readonly ClickHouseHttpClient _clickHouse;
    private readonly ILogger<ClickHouseDatasetCacheStore> _logger;

    public ClickHouseDatasetCacheStore(
        ClickHouseHttpClient clickHouse,
        ILogger<ClickHouseDatasetCacheStore> logger)
    {
        _clickHouse = clickHouse;
        _logger = logger;
    }

    public async Task<ClickHouseStoreResult> ReplaceAsync(
        SqlConnection sourceConnection,
        Dataset dataset,
        CacheProviderConnection connection,
        CancellationToken cancellationToken)
    {
        ClickHouseHttpClient.Validate(connection);
        var database = connection.Database.Trim();
        var staging = DatasetCacheRouting.ClickHouseStagingTableNameFor(dataset.Id);
        var final = DatasetCacheRouting.ClickHouseTableNameFor(dataset.Id);
        var source = DatasetSource.UncachedFrom(dataset);

        await _clickHouse.ExecuteAsync(
            connection,
            $"CREATE DATABASE IF NOT EXISTS {Quote(database)}",
            cancellationToken);
        await DropTableAsync(connection, database, staging, cancellationToken);

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
        create.Append("CREATE TABLE ").Append(Qualify(database, staging)).Append(" (");
        create.AppendJoin(", ", columns.Select(column => $"{Quote(column.Name)} {column.ClickHouseType}"));
        create.Append(") ENGINE = MergeTree ORDER BY tuple()");
        await _clickHouse.ExecuteAsync(connection, create.ToString(), cancellationToken);

        var rowCount = await InsertAsync(connection, database, staging, columns, reader, cancellationToken);
        await DropTableAsync(connection, database, final, cancellationToken);
        await _clickHouse.ExecuteAsync(
            connection,
            $"RENAME TABLE {Qualify(database, staging)} TO {Qualify(database, final)}",
            cancellationToken);

        long? sizeBytes = null;
        try
        {
            sizeBytes = await _clickHouse.ExecuteScalarLongAsync(
                connection,
                """
                SELECT coalesce(sum(bytes_on_disk), 0)
                FROM system.parts
                WHERE database = {db:String} AND table = {table:String} AND active
                """,
                cancellationToken,
                [
                    new QueryParameter("db", database, "nvarchar"),
                    new QueryParameter("table", final, "nvarchar")
                ]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogInformation(ex, "Could not estimate ClickHouse table size for {Table}.", final);
        }

        return new ClickHouseStoreResult(final, rowCount, sizeBytes);
    }

    public async Task DropDatasetTablesAsync(
        CacheProviderConnection connection,
        Guid datasetId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connection.Host) || string.IsNullOrWhiteSpace(connection.Database))
        {
            return;
        }

        var database = connection.Database.Trim();
        await DropTableAsync(connection, database, DatasetCacheRouting.ClickHouseStagingTableNameFor(datasetId), cancellationToken);
        await DropTableAsync(connection, database, DatasetCacheRouting.ClickHouseTableNameFor(datasetId), cancellationToken);
    }

    private async Task DropTableAsync(
        CacheProviderConnection connection,
        string database,
        string table,
        CancellationToken cancellationToken)
    {
        await _clickHouse.ExecuteAsync(
            connection,
            $"DROP TABLE IF EXISTS {Qualify(database, table)}",
            cancellationToken);
    }

    private async Task<long> InsertAsync(
        CacheProviderConnection connection,
        string database,
        string table,
        IReadOnlyList<ClickHouseColumn> columns,
        SqlDataReader reader,
        CancellationToken cancellationToken)
    {
        var insertSql = $"INSERT INTO {Qualify(database, table)} FORMAT TabSeparated";
        var buffer = new StringBuilder();
        var batchRows = 0;
        var total = 0L;
        var cells = new string[columns.Count];

        while (await reader.ReadAsync(cancellationToken))
        {
            for (var i = 0; i < columns.Count; i++)
            {
                cells[i] = FormatTsv(reader, i);
            }

            buffer.Append(string.Join('\t', cells)).Append('\n');
            batchRows++;
            total++;
            if (batchRows < InsertBatchRows)
            {
                continue;
            }

            await _clickHouse.InsertTsvAsync(connection, insertSql, buffer.ToString(), cancellationToken);
            buffer.Clear();
            batchRows = 0;
        }

        if (batchRows > 0)
        {
            await _clickHouse.InsertTsvAsync(connection, insertSql, buffer.ToString(), cancellationToken);
        }

        return total;
    }

    private static List<ClickHouseColumn> Describe(SqlDataReader reader)
    {
        var columns = new List<ClickHouseColumn>(reader.FieldCount);
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

            columns.Add(new ClickHouseColumn(unique, ClickHouseTypeFor(reader.GetFieldType(i))));
        }

        return columns;
    }

    private static string ClickHouseTypeFor(Type type)
    {
        var inner = Nullable.GetUnderlyingType(type) ?? type;
        var ch = inner switch
        {
            _ when inner == typeof(byte) => "UInt8",
            _ when inner == typeof(short) => "Int16",
            _ when inner == typeof(int) => "Int32",
            _ when inner == typeof(long) => "Int64",
            _ when inner == typeof(bool) => "UInt8",
            _ when inner == typeof(float) => "Float32",
            _ when inner == typeof(double) => "Float64",
            _ when inner == typeof(decimal) => "Float64",
            _ when inner == typeof(Guid) => "UUID",
            _ when inner == typeof(DateTime) => "DateTime64(3)",
            _ when inner == typeof(DateTimeOffset) => "DateTime64(3)",
            _ when inner == typeof(DateOnly) => "Date",
            _ => "String"
        };
        return $"Nullable({ch})";
    }

    private static string FormatTsv(SqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return "\\N";
        }

        var value = reader.GetValue(ordinal);
        var text = value switch
        {
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeOnly t => t.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            TimeSpan ts => ts.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture),
            bool flag => flag ? "1" : "0",
            Guid guid => guid.ToString(),
            byte[] bytes => Convert.ToHexString(bytes),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };

        return EscapeTsv(text);
    }

    private static string EscapeTsv(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal);

    private static string Quote(string name)
        => "`" + name.Replace("`", "``", StringComparison.Ordinal) + "`";

    private static string Qualify(string database, string table)
        => $"{Quote(database)}.{Quote(table)}";

    private readonly record struct ClickHouseColumn(string Name, string ClickHouseType);
}

public readonly record struct ClickHouseStoreResult(string TableName, long RowCount, long? SizeBytes);
