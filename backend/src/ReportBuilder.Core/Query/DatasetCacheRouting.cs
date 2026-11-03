using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Models;

namespace ReportBuilder.Core.Query;

public static class DatasetCacheRouting
{
    public const string CacheSchema = "cache";

    public static string TableNameFor(Guid datasetId)
        => $"DsCache_{datasetId:N}";

    public static string StagingTableNameFor(Guid datasetId)
        => $"DsCache_{datasetId:N}_stg";

    public static string ClickHouseTableNameFor(Guid datasetId)
        => $"dscache_{datasetId:N}";

    public static string ClickHouseStagingTableNameFor(Guid datasetId)
        => $"dscache_{datasetId:N}_stg";

    public static string RedisKeyFor(Guid datasetId)
        => $"rb:dscache:{datasetId:N}";

    public static string RedisStagingKeyFor(Guid datasetId)
        => $"rb:dscache:{datasetId:N}:stg";

    public static string MongoCollectionNameFor(Guid datasetId)
        => $"dscache_{datasetId:N}";

    public static string MongoStagingCollectionNameFor(Guid datasetId)
        => $"dscache_{datasetId:N}_stg";

    public static string PostgresTableNameFor(Guid datasetId)
        => $"dscache_{datasetId:N}";

    public static string PostgresStagingTableNameFor(Guid datasetId)
        => $"dscache_{datasetId:N}_stg";

    public static string ProviderOf(Dataset dataset)
        => CacheProviders.Normalize(dataset.CacheState?.CacheProvider);

    public static bool IsReady(Dataset dataset)
    {
        var state = dataset.CacheState;
        return dataset.IsCached
            && state is not null
            && string.Equals(state.Status, DatasetCacheStatuses.Ready, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(state.CacheTableName);
    }

    public static bool TryGetCachedSource(Dataset dataset, out string schema, out string objectName)
    {
        schema = dataset.SchemaName;
        objectName = dataset.ObjectName;

        if (!IsReady(dataset))
        {
            return false;
        }

        if (!string.Equals(ProviderOf(dataset), CacheProviders.SqlServer, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        schema = CacheSchema;
        objectName = dataset.CacheState!.CacheTableName!;
        return true;
    }

    public static bool TryGetClickHouseSource(
        Dataset dataset,
        string configuredDatabase,
        out string database,
        out string table)
    {
        database = string.IsNullOrWhiteSpace(configuredDatabase) ? "reportbuilder" : configuredDatabase.Trim();
        table = ClickHouseTableNameFor(dataset.Id);
        if (!IsReady(dataset)
            || !string.Equals(ProviderOf(dataset), CacheProviders.ClickHouse, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        table = dataset.CacheState!.CacheTableName!;
        return true;
    }

    public static bool TryGetRedisSource(Dataset dataset, out string key)
    {
        key = RedisKeyFor(dataset.Id);
        if (!IsReady(dataset)
            || !string.Equals(ProviderOf(dataset), CacheProviders.Redis, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(dataset.CacheState!.CacheTableName))
        {
            key = dataset.CacheState.CacheTableName!;
        }

        return true;
    }

    public static bool TryGetMongoSource(Dataset dataset, out string collection)
    {
        collection = MongoCollectionNameFor(dataset.Id);
        if (!IsReady(dataset)
            || !string.Equals(ProviderOf(dataset), CacheProviders.Mongo, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(dataset.CacheState!.CacheTableName))
        {
            collection = dataset.CacheState.CacheTableName!;
        }

        return true;
    }

    public static bool TryGetPostgresSource(Dataset dataset, out string schema, out string table)
    {
        schema = CacheSchema;
        table = PostgresTableNameFor(dataset.Id);
        if (!IsReady(dataset)
            || !string.Equals(ProviderOf(dataset), CacheProviders.Postgres, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(dataset.CacheState!.CacheTableName))
        {
            table = dataset.CacheState.CacheTableName!;
        }

        return true;
    }

    public static (string Schema, string ObjectName) ResolveSource(Dataset dataset)
        => TryGetCachedSource(dataset, out var schema, out var objectName)
            ? (schema, objectName)
            : (dataset.SchemaName, dataset.ObjectName);
}

