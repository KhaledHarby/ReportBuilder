using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Query;
using ReportBuilder.Core.Services;

namespace ReportBuilder.Infrastructure.Cache;

public sealed class MongoDatasetCacheStore
{
    private readonly MongoCacheClient _mongo;

    public MongoDatasetCacheStore(MongoCacheClient mongo)
    {
        _mongo = mongo;
    }

    public async Task<MongoStoreResult> ReplaceAsync(
        SqlConnection sourceConnection,
        Dataset dataset,
        CacheProviderConnection connection,
        CancellationToken cancellationToken)
    {
        MongoCacheClient.Validate(connection);
        var source = DatasetSource.UncachedFrom(dataset);
        await using var command = sourceConnection.CreateCommand();
        command.CommandText = $"SELECT * FROM {source}";
        command.CommandTimeout = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = Describe(reader);
        if (columns.Count == 0)
        {
            throw new ReportQueryException("The source table has no columns to cache.");
        }

        return await _mongo.ReplaceSnapshotAsync(
            connection,
            dataset.Id,
            columns,
            ReadRows(reader, columns, cancellationToken),
            cancellationToken,
            IndexedColumnNames(dataset));
    }

    private static async IAsyncEnumerable<object?[]> ReadRows(
        SqlDataReader reader,
        IReadOnlyList<string> columns,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (await reader.ReadAsync(cancellationToken))
        {
            var values = new object?[columns.Count];
            for (var i = 0; i < columns.Count; i++)
            {
                values[i] = Normalize(reader, i);
            }

            yield return values;
        }
    }

    public Task DropDatasetCollectionsAsync(
        CacheProviderConnection connection,
        Guid datasetId,
        CancellationToken cancellationToken)
        => _mongo.DropDatasetCollectionsAsync(connection, datasetId, cancellationToken);

    private static List<string> IndexedColumnNames(Dataset dataset)
    {
        var marked = dataset.Columns
            .Where(column => !column.IsHidden && column.IsIndexed)
            .OrderBy(column => column.Ordinal)
            .Select(column => column.ColumnName)
            .ToList();
        if (marked.Count > 0)
        {
            return marked;
        }

        return dataset.Columns
            .Where(column => !column.IsHidden && column.IsFilterable)
            .OrderBy(column => column.Ordinal)
            .Select(column => column.ColumnName)
            .Take(16)
            .ToList();
    }

    private static List<string> Describe(SqlDataReader reader)
    {
        var columns = new List<string>(reader.FieldCount);
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

            columns.Add(unique);
        }

        return columns;
    }

    private static object? Normalize(SqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateTime dt => dt,
            DateTimeOffset dto => dto.UtcDateTime,
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeOnly t => t.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            TimeSpan ts => ts.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture),
            Guid guid => guid.ToString(),
            byte[] bytes => Convert.ToHexString(bytes),
            DBNull => null,
            _ => value
        };
    }
}
