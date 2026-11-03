namespace ReportBuilder.Core.Entities;

public class DatasetCacheState
{
    public Guid DatasetId { get; set; }
    public string Status { get; set; } = DatasetCacheStatuses.None;
    public string? CacheTableName { get; set; }
    public DateTimeOffset? LastRefreshStartedAt { get; set; }
    public DateTimeOffset? LastRefreshCompletedAt { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public DateTimeOffset? NextRefreshAt { get; set; }
    public long? RowCount { get; set; }
    public int? DurationMs { get; set; }
    public long? SizeBytes { get; set; }
    public string? RefreshedByServer { get; set; }
    public Guid? RefreshedByUserId { get; set; }
    public string? LastError { get; set; }
    public int CacheVersion { get; set; }
    /// <summary>Where this snapshot lives: SqlServer (default), ClickHouse, etc.</summary>
    public string? CacheProvider { get; set; }

    public Dataset Dataset { get; set; } = null!;
}
