using ReportBuilder.Core.Identity;

namespace ReportBuilder.Core.Entities;

public class Dataset
{
    public Guid Id { get; set; }
    public string SchemaName { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public DatasetObjectType ObjectType { get; set; }
    /// <summary>Single read-only SELECT when <see cref="ObjectType"/> is <see cref="DatasetObjectType.Query"/>.</summary>
    public string? SqlText { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? DisplayNameArabic { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsRestricted { get; set; }
    public string AccessMode { get; set; } = ReportAccessModes.Everyone;
    public bool IsCached { get; set; }
    public string CacheScheduleMode { get; set; } = CacheScheduleModes.Manual;
    public int? CacheIntervalMinutes { get; set; }
    public string? CacheDailyAtLocalTime { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<DatasetColumn> Columns { get; set; } = new List<DatasetColumn>();
    public ICollection<DatasetAccess> Access { get; set; } = [];
    public ICollection<DatasetRoleAccess> RoleAccess { get; set; } = [];
    public ICollection<ReportTemplate> Templates { get; set; } = new List<ReportTemplate>();
    public DatasetCacheState? CacheState { get; set; }
}
