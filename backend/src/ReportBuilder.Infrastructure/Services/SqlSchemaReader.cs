using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Models;
using ReportBuilder.Core.Query;
using ReportBuilder.Core.Services;
using ReportBuilder.Infrastructure.Data;

namespace ReportBuilder.Infrastructure.Services;

internal sealed class SqlSchemaReader
{
    private readonly ReportBuilderDbContext _db;

    public SqlSchemaReader(ReportBuilderDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<DiscoveredObject>> ListObjectsAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT s.name AS SchemaName, o.name AS ObjectName,
                   CASE WHEN o.type = 'V' THEN 'View' ELSE 'Table' END AS ObjectType
            FROM sys.objects o
            INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE o.type IN ('U', 'V')
              AND o.is_ms_shipped = 0
              AND s.name NOT IN (N'sys', N'rb', N'rbq', N'cache', N'INFORMATION_SCHEMA', N'guest')
              AND o.name NOT IN (N'__EFMigrationsHistory')
            ORDER BY s.name, o.name
            """;

        var results = new List<DiscoveredObject>();
        await using var connection = new SqlConnection(_db.Database.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new DiscoveredObject(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2) == "View" ? DatasetObjectType.View : DatasetObjectType.Table));
        }

        return results;
    }

    public async Task<IReadOnlyList<DiscoveredColumn>> ListColumnsAsync(string schema, string objectName, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT c.COLUMN_NAME, c.DATA_TYPE, c.ORDINAL_POSITION
            FROM INFORMATION_SCHEMA.COLUMNS c
            WHERE c.TABLE_SCHEMA = @schema AND c.TABLE_NAME = @name
            ORDER BY c.ORDINAL_POSITION
            """;

        var results = new List<DiscoveredColumn>();
        await using var connection = new SqlConnection(_db.Database.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@name", objectName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new DiscoveredColumn(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2)));
        }

        return results;
    }

    public Task<IReadOnlyList<DiscoveredColumn>> ProbeQueryColumnsAsync(string? sqlText, CancellationToken cancellationToken)
        => ProbeQueryAsync(sqlText, cancellationToken);

    public async Task RefreshColumnsAsync(Dataset dataset, CancellationToken cancellationToken)
    {
        var live = await LiveColumnsAsync(dataset, cancellationToken);
        if (live.Count == 0)
        {
            var target = dataset.ObjectType == DatasetObjectType.Query
                ? "the query"
                : $"{dataset.SchemaName}.{dataset.ObjectName}";
            throw new ReportQueryException($"No columns were found for {target}.");
        }

        var existing = await _db.DatasetColumns.AsNoTracking()
            .Where(c => c.DatasetId == dataset.Id)
            .ToListAsync(cancellationToken);
        var byName = existing.ToDictionary(c => c.ColumnName, StringComparer.OrdinalIgnoreCase);
        var liveNames = live.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var column in live)
        {
            if (byName.TryGetValue(column.Name, out var current))
            {
                if (!string.Equals(current.SqlType, column.SqlType, StringComparison.OrdinalIgnoreCase)
                    || current.Ordinal != column.Ordinal)
                {
                    await _db.Database.ExecuteSqlInterpolatedAsync($"""
                        UPDATE [rb].[DatasetColumns]
                        SET [SqlType] = {column.SqlType}, [Ordinal] = {column.Ordinal}
                        WHERE [Id] = {current.Id}
                        """, cancellationToken);
                }

                continue;
            }

            await InsertColumnAsync(dataset.Id, column, cancellationToken);
        }

        foreach (var stale in existing.Where(column => !liveNames.Contains(column.ColumnName)))
        {
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM [rb].[DatasetColumns] WHERE [Id] = {stale.Id}
                """, cancellationToken);
        }
    }

    public async Task<DatasetColumnGap> GetColumnGapAsync(Dataset dataset, CancellationToken cancellationToken)
    {
        var live = await LiveColumnsAsync(dataset, cancellationToken);
        var existing = await _db.DatasetColumns.AsNoTracking()
            .Where(c => c.DatasetId == dataset.Id)
            .Select(c => c.ColumnName)
            .ToListAsync(cancellationToken);
        var existingNames = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new DatasetColumnGap
        {
            LiveCount = live.Count,
            CatalogCount = existing.Count,
            Missing = live
                .Where(column => !existingNames.Contains(column.Name))
                .Select(column => new DatasetLiveColumn
                {
                    ColumnName = column.Name,
                    SqlType = column.SqlType,
                    Ordinal = column.Ordinal
                })
                .ToList()
        };
    }

    public async Task<string> GetObjectScriptAsync(Dataset dataset, CancellationToken cancellationToken)
    {
        if (dataset.ObjectType == DatasetObjectType.Query)
        {
            return ReadOnlySelect.Normalize(dataset.SqlText);
        }

        await using var connection = new SqlConnection(_db.Database.GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        if (dataset.ObjectType == DatasetObjectType.View)
        {
            const string viewSql = """
                SELECT m.definition
                FROM sys.objects o
                INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
                INNER JOIN sys.sql_modules m ON m.object_id = o.object_id
                WHERE o.type = 'V'
                  AND s.name = @schema
                  AND o.name = @name
                """;
            await using (var command = new SqlCommand(viewSql, connection))
            {
                command.Parameters.AddWithValue("@schema", dataset.SchemaName);
                command.Parameters.AddWithValue("@name", dataset.ObjectName);
                var definition = await command.ExecuteScalarAsync(cancellationToken) as string;
                if (!string.IsNullOrWhiteSpace(definition))
                {
                    return definition.Trim();
                }
            }

            throw new ReportQueryException(
                $"No SQL definition was found for view {dataset.SchemaName}.{dataset.ObjectName}.");
        }

        const string tableSql = """
            SELECT
                c.COLUMN_NAME,
                c.DATA_TYPE,
                c.CHARACTER_MAXIMUM_LENGTH,
                c.NUMERIC_PRECISION,
                c.NUMERIC_SCALE,
                c.DATETIME_PRECISION,
                c.IS_NULLABLE,
                c.COLUMN_DEFAULT
            FROM INFORMATION_SCHEMA.COLUMNS c
            WHERE c.TABLE_SCHEMA = @schema AND c.TABLE_NAME = @name
            ORDER BY c.ORDINAL_POSITION
            """;

        var lines = new List<string>();
        await using (var command = new SqlCommand(tableSql, connection))
        {
            command.Parameters.AddWithValue("@schema", dataset.SchemaName);
            command.Parameters.AddWithValue("@name", dataset.ObjectName);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var columnName = reader.GetString(0);
                var dataType = reader.GetString(1);
                int? charLen = reader.IsDBNull(2) ? null : reader.GetInt32(2);
                byte? numericPrecision = reader.IsDBNull(3) ? null : reader.GetByte(3);
                int? numericScale = reader.IsDBNull(4) ? null : reader.GetInt32(4);
                short? datePrecision = reader.IsDBNull(5) ? null : reader.GetInt16(5);
                var nullable = string.Equals(reader.GetString(6), "YES", StringComparison.OrdinalIgnoreCase);
                var columnDefault = reader.IsDBNull(7) ? null : reader.GetString(7);

                var typeSql = FormatSqlType(dataType, charLen, numericPrecision, numericScale, datePrecision);
                var nullSql = nullable ? "NULL" : "NOT NULL";
                var defaultSql = string.IsNullOrWhiteSpace(columnDefault)
                    ? string.Empty
                    : $" DEFAULT {columnDefault.Trim()}";
                lines.Add($"    {Quote(columnName)} {typeSql} {nullSql}{defaultSql}");
            }
        }

        if (lines.Count == 0)
        {
            throw new ReportQueryException(
                $"No columns were found for table {dataset.SchemaName}.{dataset.ObjectName}.");
        }

        var qualified = $"{Quote(dataset.SchemaName)}.{Quote(dataset.ObjectName)}";
        return $"""
            CREATE TABLE {qualified}
            (
            {string.Join(",\n", lines)}
            );
            """;
    }

    public async Task ApplyObjectScriptAsync(Dataset dataset, string script, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            throw new ReportQueryException("SQL script is required.");
        }

        await using var connection = new SqlConnection(_db.Database.GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        var liveType = await ReadLiveObjectTypeAsync(connection, dataset.SchemaName, dataset.ObjectName, cancellationToken);
        if (liveType is null)
        {
            throw new ReportQueryException(
                $"Object {dataset.SchemaName}.{dataset.ObjectName} was not found in the database.");
        }

        // Keep catalog aligned with the live SQL object type.
        if (liveType != dataset.ObjectType)
        {
            dataset.ObjectType = liveType.Value;
        }

        var prepared = PrepareScript(liveType.Value, dataset.SchemaName, dataset.ObjectName, script);
        var batches = SplitBatches(prepared);
        if (batches.Count == 0)
        {
            throw new ReportQueryException("SQL script is empty.");
        }

        try
        {
            // Required session settings for CREATE/ALTER VIEW; each must be its own batch.
            if (liveType == DatasetObjectType.View)
            {
                await ExecuteBatchAsync(connection, "SET ANSI_NULLS ON;", cancellationToken);
                await ExecuteBatchAsync(connection, "SET QUOTED_IDENTIFIER ON;", cancellationToken);
            }

            foreach (var batch in batches)
            {
                await ExecuteBatchAsync(connection, batch, cancellationToken);
            }
        }
        catch (SqlException ex)
        {
            throw new ReportQueryException($"SQL apply failed: {ex.Message}");
        }

        if (liveType == DatasetObjectType.View)
        {
            var applied = await ReadViewDefinitionAsync(connection, dataset.SchemaName, dataset.ObjectName, cancellationToken);
            if (string.IsNullOrWhiteSpace(applied))
            {
                throw new ReportQueryException(
                    $"SQL ran, but no view definition was found afterward for {dataset.SchemaName}.{dataset.ObjectName}.");
            }

            var expectedBody = NormalizeViewBody(prepared);
            var actualBody = NormalizeViewBody(applied);
            if (!string.IsNullOrWhiteSpace(expectedBody)
                && !string.IsNullOrWhiteSpace(actualBody)
                && !string.Equals(expectedBody, actualBody, StringComparison.OrdinalIgnoreCase))
            {
                throw new ReportQueryException(
                    $"SQL ran, but the database view definition does not match what was applied. Check that the script targets {dataset.SchemaName}.{dataset.ObjectName}.");
            }
        }
    }

    private static async Task ExecuteBatchAsync(SqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = 120
        };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<DatasetObjectType?> ReadLiveObjectTypeAsync(
        SqlConnection connection,
        string schema,
        string objectName,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT CASE WHEN o.type = 'V' THEN 'View' ELSE 'Table' END
            FROM sys.objects o
            INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE o.type IN ('U', 'V')
              AND s.name = @schema
              AND o.name = @name
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@name", objectName);
        var value = await command.ExecuteScalarAsync(cancellationToken) as string;
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Equals("View", StringComparison.OrdinalIgnoreCase)
            ? DatasetObjectType.View
            : DatasetObjectType.Table;
    }

    private static async Task<string?> ReadViewDefinitionAsync(
        SqlConnection connection,
        string schema,
        string objectName,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT m.definition
            FROM sys.objects o
            INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
            INNER JOIN sys.sql_modules m ON m.object_id = o.object_id
            WHERE o.type = 'V'
              AND s.name = @schema
              AND o.name = @name
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@name", objectName);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private static string PrepareScript(
        DatasetObjectType objectType,
        string schema,
        string objectName,
        string script)
    {
        var trimmed = script.Trim().TrimStart('\uFEFF');
        if (objectType != DatasetObjectType.View)
        {
            return trimmed;
        }

        // Force CREATE OR ALTER VIEW and pin the catalog object name so edits apply to the right view.
        var withCreate = System.Text.RegularExpressions.Regex.Replace(
            trimmed,
            @"\bCREATE\s+OR\s+ALTER\s+VIEW\b|\bALTER\s+VIEW\b|\bCREATE\s+VIEW\b",
            "CREATE OR ALTER VIEW",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var qualified = $"{Quote(schema)}.{Quote(objectName)}";
        var replacedName = System.Text.RegularExpressions.Regex.Replace(
            withCreate,
            @"CREATE\s+OR\s+ALTER\s+VIEW\s+([^\s(]+)",
            $"CREATE OR ALTER VIEW {qualified}",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return replacedName;
    }

    private static string NormalizeViewBody(string script)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            script,
            @"\bAS\b(.*)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        if (!match.Success)
        {
            return string.Empty;
        }

        return System.Text.RegularExpressions.Regex.Replace(match.Groups[1].Value, @"\s+", " ").Trim();
    }

    private static List<string> SplitBatches(string script)
    {
        var batches = new List<string>();
        var lines = script.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var current = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^\s*GO\s*(--.*)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                var batch = current.ToString().Trim();
                if (batch.Length > 0)
                {
                    batches.Add(batch);
                }

                current.Clear();
                continue;
            }

            current.AppendLine(line);
        }

        var last = current.ToString().Trim();
        if (last.Length > 0)
        {
            batches.Add(last);
        }

        return batches;
    }

    private static string FormatSqlType(
        string dataType,
        int? charLen,
        byte? numericPrecision,
        int? numericScale,
        short? datePrecision)
    {
        var type = dataType.ToLowerInvariant();
        return type switch
        {
            "nvarchar" or "varchar" or "nchar" or "char" or "varbinary" or "binary"
                => charLen is null
                    ? type
                    : charLen < 0
                        ? $"{type}(max)"
                        : $"{type}({charLen})",
            "decimal" or "numeric"
                => numericPrecision is null
                    ? type
                    : $"{type}({numericPrecision},{numericScale ?? 0})",
            "datetime2" or "datetimeoffset" or "time"
                => datePrecision is null ? type : $"{type}({datePrecision})",
            "float"
                => numericPrecision is null ? type : $"{type}({numericPrecision})",
            _ => type
        };
    }

    private static string Quote(string name)
        => "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";

    public async Task<int> ImportMissingColumnsAsync(
        Dataset dataset,
        IReadOnlyList<string>? columnNames,
        CancellationToken cancellationToken)
    {
        var live = await LiveColumnsAsync(dataset, cancellationToken);
        if (live.Count == 0)
        {
            throw new ReportQueryException(
                $"No columns were found for {dataset.SchemaName}.{dataset.ObjectName}.");
        }

        var liveByName = live.ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);
        var existing = await _db.DatasetColumns.AsNoTracking()
            .Where(c => c.DatasetId == dataset.Id)
            .Select(c => c.ColumnName)
            .ToListAsync(cancellationToken);
        var existingNames = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var requested = (columnNames ?? [])
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        IEnumerable<DiscoveredColumn> selected;
        if (requested.Count > 0)
        {
            var unknown = requested.Where(name => !liveByName.ContainsKey(name)).ToList();
            if (unknown.Count > 0)
            {
                throw new ReportQueryException(
                    $"Column(s) not found on {dataset.SchemaName}.{dataset.ObjectName}: {string.Join(", ", unknown)}.");
            }

            selected = requested
                .Select(name => liveByName[name])
                .Where(column => !existingNames.Contains(column.Name));
        }
        else
        {
            selected = live.Where(column => !existingNames.Contains(column.Name));
        }

        var imported = 0;
        foreach (var column in selected)
        {
            await InsertColumnAsync(dataset.Id, column, cancellationToken);
            imported++;
        }

        return imported;
    }

    private Task<IReadOnlyList<DiscoveredColumn>> LiveColumnsAsync(Dataset dataset, CancellationToken cancellationToken)
        => dataset.ObjectType == DatasetObjectType.Query
            ? ProbeQueryAsync(dataset.SqlText, cancellationToken)
            : ListColumnsAsync(dataset.SchemaName, dataset.ObjectName, cancellationToken);

    private async Task<IReadOnlyList<DiscoveredColumn>> ProbeQueryAsync(string? sqlText, CancellationToken cancellationToken)
    {
        var sql = ReadOnlySelect.Normalize(sqlText);
        var columns = new List<DiscoveredColumn>();
        await using var connection = new SqlConnection(_db.Database.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TOP (0) * FROM (" + sql + ") AS " + SqlIdentifiers.Quote("rb_dataset");
        command.CommandTimeout = 30;
        try
        {
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i).Trim();
                if (name.Length == 0)
                {
                    throw new ReportQueryException("Every column in the query needs a name. Alias calculated columns.");
                }

                if (name.Length > 128)
                {
                    throw new ReportQueryException($"Column '{name}' is longer than 128 characters.");
                }

                if (!seen.Add(name))
                {
                    throw new ReportQueryException($"Query returns column '{name}' more than once. Alias one of them.");
                }

                var sqlType = reader.GetDataTypeName(i);
                if (sqlType.Length > 64)
                {
                    sqlType = sqlType[..64];
                }

                columns.Add(new DiscoveredColumn(name, sqlType, i + 1));
            }
        }
        catch (ReportQueryException)
        {
            throw;
        }
        catch (SqlException ex)
        {
            if (ex.Message.Contains("ORDER BY clause is invalid", StringComparison.OrdinalIgnoreCase))
            {
                throw new ReportQueryException("Remove ORDER BY from the dataset query. Sort the report instead.");
            }

            throw new ReportQueryException($"Dataset query failed: {ex.Message}");
        }

        if (columns.Count == 0)
        {
            throw new ReportQueryException("Dataset query did not return any columns.");
        }

        return columns;
    }

    private async Task InsertColumnAsync(Guid datasetId, DiscoveredColumn column, CancellationToken cancellationToken)
    {
        string? displayNameArabic = null;
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [rb].[DatasetColumns]
                ([Id], [DatasetId], [ColumnName], [SqlType], [DisplayName], [DisplayNameArabic], [IsHidden], [IsFilterable], [IsGroupable], [IsIndexed], [DefaultSortDirection], [Ordinal])
            VALUES
                ({Guid.NewGuid()}, {datasetId}, {column.Name}, {column.SqlType}, {column.Name}, {displayNameArabic}, {false}, {true}, {true}, {false}, {SortDirections.Desc}, {column.Ordinal})
            """, cancellationToken);
    }
}

internal sealed record DiscoveredObject(string SchemaName, string ObjectName, DatasetObjectType ObjectType);

internal sealed record DiscoveredColumn(string Name, string SqlType, int Ordinal);
