using ReportBuilder.Core.Entities;

namespace ReportBuilder.Core.Query;

public static class DatasetSource
{
    public const string QuerySchema = "rbq";

    public static bool IsQuery(Dataset dataset)
        => dataset.ObjectType == DatasetObjectType.Query;

    public static string UncachedRelation(Dataset dataset)
        => IsQuery(dataset)
            ? "(" + ReadOnlySelect.Normalize(dataset.SqlText) + ")"
            : SqlIdentifiers.Qualify(dataset.SchemaName, dataset.ObjectName);

    public static string UncachedFrom(Dataset dataset)
        => IsQuery(dataset)
            ? UncachedRelation(dataset) + " AS " + SqlIdentifiers.Quote("rb_dataset")
            : UncachedRelation(dataset);

    public static string SqlServerRelation(Dataset dataset)
    {
        if (DatasetCacheRouting.TryGetCachedSource(dataset, out var schema, out var objectName))
        {
            return SqlIdentifiers.Qualify(schema, objectName);
        }

        return UncachedRelation(dataset);
    }

    public static string ReportFrom(Dataset dataset, QueryDialect dialect)
    {
        if (dialect.UsesExternalTable)
        {
            return dialect.Qualify(dialect.Database!, dialect.Table!);
        }

        if (DatasetCacheRouting.TryGetCachedSource(dataset, out var schema, out var objectName))
        {
            return dialect.Qualify(schema, objectName);
        }

        return UncachedFrom(dataset);
    }
}
