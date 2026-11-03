using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Models;

namespace ReportBuilder.Core.Services;

public sealed class DatasetUpdateRequest
{
    public string? DisplayName { get; set; }
    public string? DisplayNameArabic { get; set; }
    public bool? IsEnabled { get; set; }
    public bool? IsRestricted { get; set; }
    public string? AccessMode { get; set; }
    public int? SortOrder { get; set; }
    public List<Guid>? AccessUserIds { get; set; }
    public List<Guid>? AccessRoleIds { get; set; }
    public List<DatasetColumnUpdate>? Columns { get; set; }
}

public sealed class DatasetCacheConfigureRequest
{
    public bool? IsCached { get; set; }
    public string? CacheScheduleMode { get; set; }
    public int? CacheIntervalMinutes { get; set; }
    public string? CacheDailyAtLocalTime { get; set; }
}

public sealed class DatasetColumnUpdate
{
    public Guid Id { get; set; }
    public string? DisplayName { get; set; }
    public string? DisplayNameArabic { get; set; }
    public bool? IsHidden { get; set; }
    public bool? IsFilterable { get; set; }
    public bool? IsGroupable { get; set; }
    public bool? IsIndexed { get; set; }
    public string? DefaultSortDirection { get; set; }
    public bool UpdateLookup { get; set; }
    public Guid? LookupId { get; set; }
}

public sealed class ImportColumnsRequest
{
    public List<string>? ColumnNames { get; set; }
}

public sealed class DatasetLiveColumn
{
    public string ColumnName { get; set; } = string.Empty;
    public string SqlType { get; set; } = string.Empty;
    public int Ordinal { get; set; }
}

public sealed class DatasetColumnGap
{
    public int LiveCount { get; set; }
    public int CatalogCount { get; set; }
    public List<DatasetLiveColumn> Missing { get; set; } = [];
}

public sealed class DatasetSqlScript
{
    public Guid DatasetId { get; set; }
    public string SchemaName { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public string ObjectType { get; set; } = string.Empty;
    public string Script { get; set; } = string.Empty;
}

public sealed class ApplyDatasetSqlRequest
{
    public string Script { get; set; } = string.Empty;
}

public sealed class DatasetQueryWriteRequest
{
    public string? DisplayName { get; set; }
    public string? DisplayNameArabic { get; set; }
    public string? SqlText { get; set; }
}

public interface IDatasetCatalogService
{
    Task<IReadOnlyList<DatasetLiveColumn>> PreviewQueryColumnsAsync(string? sqlText, CancellationToken cancellationToken = default);
    Task<Dataset> CreateQueryAsync(DatasetQueryWriteRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Dataset>> DiscoverAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Dataset>> ListAsync(bool enabledOnly, CancellationToken cancellationToken = default);
    Task<Dataset?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DatasetColumnGap?> GetColumnGapAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DatasetSqlScript?> GetSqlScriptAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DatasetSqlScript> ApplySqlScriptAsync(Guid id, ApplyDatasetSqlRequest request, CancellationToken cancellationToken = default);
    Task<Dataset> ImportColumnsAsync(Guid id, ImportColumnsRequest request, CancellationToken cancellationToken = default);
    Task<Dataset> UpdateAsync(Guid id, DatasetUpdateRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReportPerson>> ListPeopleAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReportAccessRole>> ListRolesAsync(CancellationToken cancellationToken = default);
}
