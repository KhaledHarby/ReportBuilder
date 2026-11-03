using ReportBuilder.Core.Entities;

namespace ReportBuilder.Core.Services;

public sealed class ReportInsightsQuery
{
    public int Days { get; init; } = 30;
}

public sealed class ReportInsightsSummary
{
    public int Days { get; init; }
    public int ReportRuns { get; init; }
    public int ReportExports { get; init; }
    public int UniqueReportsRun { get; init; }
    public int ActiveUsers { get; init; }
    public int CachedDatasets { get; init; }
    public int CacheReady { get; init; }
    public int CacheFailed { get; init; }
    public int ServingFromCache { get; init; }
    public int AnalyticsViews { get; init; }
    public int AnalyticsQueries { get; init; }
    public int AnalyticsExports { get; init; }
    public int UniqueDashboardsViewed { get; init; }
}

public sealed class AnalyticsUsageInsight
{
    public Guid DashboardId { get; init; }
    public string DashboardName { get; init; } = string.Empty;
    public string? DashboardNameArabic { get; init; }
    public string Status { get; init; } = string.Empty;
    public int ViewCount { get; init; }
    public int QueryCount { get; init; }
    public int ExportCount { get; init; }
    public DateTimeOffset? LastViewAt { get; init; }
    public string? LastViewBy { get; init; }
    public DateTimeOffset? LastExportAt { get; init; }
    public string? LastExportBy { get; init; }
}

public sealed class ReportUsageInsight
{
    public Guid ReportId { get; init; }
    public string ReportName { get; init; } = string.Empty;
    public string? ReportNameArabic { get; init; }
    public string Status { get; init; } = string.Empty;
    public Guid DatasetId { get; init; }
    public string DatasetName { get; init; } = string.Empty;
    public int RunCount { get; init; }
    public int ExportCount { get; init; }
    public DateTimeOffset? LastRunAt { get; init; }
    public string? LastRunBy { get; init; }
    public DateTimeOffset? LastExportAt { get; init; }
    public string? LastExportBy { get; init; }
    public string ServingFrom { get; init; } = "live";
    public DateTimeOffset? CacheLastSuccessAt { get; init; }
    public string? CacheStatus { get; init; }
}

public sealed class DatasetFreshnessInsight
{
    public Guid DatasetId { get; init; }
    public string DatasetName { get; init; } = string.Empty;
    public string? DatasetNameArabic { get; init; }
    public string ObjectKey { get; init; } = string.Empty;
    public bool IsEnabled { get; init; }
    public bool IsCached { get; init; }
    public string ServingFrom { get; init; } = "live";
    public string? CacheStatus { get; init; }
    public DateTimeOffset? LastSuccessAt { get; init; }
    public DateTimeOffset? NextRefreshAt { get; init; }
    public long? RowCount { get; init; }
    public string? RefreshedByServer { get; init; }
    public string? LastError { get; init; }
    public int ReportCount { get; init; }
}

public sealed class ReportInsightsResult
{
    public ReportInsightsSummary Summary { get; init; } = new();
    public IReadOnlyList<ReportUsageInsight> Reports { get; init; } = [];
    public IReadOnlyList<AnalyticsUsageInsight> Analytics { get; init; } = [];
    public IReadOnlyList<DatasetFreshnessInsight> Freshness { get; init; } = [];
}

public interface IReportInsightsService
{
    Task<ReportInsightsResult> GetAsync(ReportInsightsQuery query, CancellationToken cancellationToken = default);
}
