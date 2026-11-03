using Microsoft.EntityFrameworkCore;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Identity;
using ReportBuilder.Core.Query;
using ReportBuilder.Core.Services;
using ReportBuilder.Infrastructure.Data;

namespace ReportBuilder.Infrastructure.Services;

public sealed class ReportInsightsService : IReportInsightsService
{
    private static readonly string[] UsageActions = ["report.run", "report.export"];
    private static readonly string[] AnalyticsActions = ["analytics.view", "analytics.query", "analytics.export"];

    private readonly ReportBuilderDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ReportInsightsService(ReportBuilderDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<ReportInsightsResult> GetAsync(
        ReportInsightsQuery query,
        CancellationToken cancellationToken = default)
    {
        var days = Math.Clamp(query.Days <= 0 ? 30 : query.Days, 1, 365);
        var from = DateTimeOffset.UtcNow.AddDays(-days);

        var reports = (await _db.ReportTemplates.AsNoTracking()
            .Include(r => r.Dataset)
                .ThenInclude(d => d.CacheState)
            .Include(r => r.Access)
            .Include(r => r.RoleAccess)
                .ThenInclude(row => row.Role)
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken))
            .Where(report => ReportAccessRules.CanView(report, _currentUser))
            .ToList();

        var visibleReportIds = reports.Select(report => report.Id).ToHashSet();
        var usageEvents = (await _db.ActivityLogs.AsNoTracking()
            .Where(log => log.OccurredAt >= from
                          && log.Category == ActivityCategories.Reports
                          && UsageActions.Contains(log.Action)
                          && log.EntityType == "Report"
                          && log.EntityId != null)
            .Select(log => new
            {
                log.Action,
                ReportId = log.EntityId!.Value,
                log.OccurredAt,
                Actor = log.ActorUserName
            })
            .ToListAsync(cancellationToken))
            .Where(log => visibleReportIds.Contains(log.ReportId))
            .ToList();

        var byReport = usageEvents
            .GroupBy(e => e.ReportId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var runs = g.Where(x => x.Action == "report.run").ToList();
                    var exports = g.Where(x => x.Action == "report.export").ToList();
                    var lastRun = runs.OrderByDescending(x => x.OccurredAt).FirstOrDefault();
                    var lastExport = exports.OrderByDescending(x => x.OccurredAt).FirstOrDefault();
                    return new
                    {
                        RunCount = runs.Count,
                        ExportCount = exports.Count,
                        LastRunAt = lastRun?.OccurredAt,
                        LastRunBy = lastRun?.Actor,
                        LastExportAt = lastExport?.OccurredAt,
                        LastExportBy = lastExport?.Actor
                    };
                });

        var reportInsights = reports.Select(report =>
        {
            byReport.TryGetValue(report.Id, out var usage);
            var dataset = report.Dataset;
            var ready = DatasetCacheRouting.IsReady(dataset);

            return new ReportUsageInsight
            {
                ReportId = report.Id,
                ReportName = report.Name,
                ReportNameArabic = report.NameArabic,
                Status = report.Status.ToString(),
                DatasetId = report.DatasetId,
                DatasetName = dataset.DisplayName,
                RunCount = usage?.RunCount ?? 0,
                ExportCount = usage?.ExportCount ?? 0,
                LastRunAt = usage?.LastRunAt,
                LastRunBy = usage?.LastRunBy,
                LastExportAt = usage?.LastExportAt,
                LastExportBy = usage?.LastExportBy,
                ServingFrom = ready ? "cache" : "live",
                CacheLastSuccessAt = dataset.CacheState?.LastSuccessAt,
                CacheStatus = dataset.IsCached ? dataset.CacheState?.Status ?? DatasetCacheStatuses.None : null
            };
        })
        .OrderByDescending(r => r.RunCount + r.ExportCount)
        .ThenBy(r => r.ReportName)
        .ToList();

        var reportCounts = reports
            .GroupBy(r => r.DatasetId)
            .ToDictionary(g => g.Key, g => g.Count());

        var datasets = await _db.Datasets.AsNoTracking()
            .Include(d => d.CacheState)
            .OrderBy(d => d.DisplayName)
            .ToListAsync(cancellationToken);

        var freshness = datasets.Select(dataset =>
        {
            var ready = DatasetCacheRouting.IsReady(dataset);
            reportCounts.TryGetValue(dataset.Id, out var reportCount);
            return new DatasetFreshnessInsight
            {
                DatasetId = dataset.Id,
                DatasetName = dataset.DisplayName,
                DatasetNameArabic = dataset.DisplayNameArabic,
                ObjectKey = dataset.ObjectType == DatasetObjectType.Query
                    ? "SQL"
                    : $"{dataset.SchemaName}.{dataset.ObjectName}",
                IsEnabled = dataset.IsEnabled,
                IsCached = dataset.IsCached,
                ServingFrom = ready ? "cache" : "live",
                CacheStatus = dataset.IsCached ? dataset.CacheState?.Status ?? DatasetCacheStatuses.None : null,
                LastSuccessAt = dataset.CacheState?.LastSuccessAt,
                NextRefreshAt = dataset.CacheState?.NextRefreshAt,
                RowCount = dataset.CacheState?.RowCount,
                RefreshedByServer = dataset.CacheState?.RefreshedByServer,
                LastError = dataset.CacheState?.LastError,
                ReportCount = reportCount
            };
        })
        .OrderByDescending(d => d.IsCached)
        .ThenBy(d => d.DatasetName)
        .ToList();

        var analyticsDashboards = (await _db.AnalyticsDashboards.AsNoTracking()
            .Include(item => item.Access)
            .Include(item => item.RoleAccess)
                .ThenInclude(row => row.Role)
            .OrderBy(item => item.Name)
            .ToListAsync(cancellationToken))
            .Where(item => AnalyticsAccessRules.CanView(item, _currentUser))
            .ToList();

        var visibleDashboardIds = analyticsDashboards.Select(item => item.Id).ToHashSet();
        var analyticsEvents = (await _db.ActivityLogs.AsNoTracking()
            .Where(log => log.OccurredAt >= from
                          && log.Category == ActivityCategories.Analytics
                          && AnalyticsActions.Contains(log.Action)
                          && log.EntityType == "AnalyticsDashboard"
                          && log.EntityId != null)
            .Select(log => new
            {
                log.Action,
                DashboardId = log.EntityId!.Value,
                log.OccurredAt,
                Actor = log.ActorUserName
            })
            .ToListAsync(cancellationToken))
            .Where(log => visibleDashboardIds.Contains(log.DashboardId))
            .ToList();

        var byDashboard = analyticsEvents
            .GroupBy(e => e.DashboardId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var views = g.Where(x => x.Action == "analytics.view").ToList();
                    var queries = g.Where(x => x.Action == "analytics.query").ToList();
                    var exports = g.Where(x => x.Action == "analytics.export").ToList();
                    var lastView = views.Concat(queries).OrderByDescending(x => x.OccurredAt).FirstOrDefault();
                    var lastExport = exports.OrderByDescending(x => x.OccurredAt).FirstOrDefault();
                    return new
                    {
                        ViewCount = views.Count,
                        QueryCount = queries.Count,
                        ExportCount = exports.Count,
                        LastViewAt = lastView?.OccurredAt,
                        LastViewBy = lastView?.Actor,
                        LastExportAt = lastExport?.OccurredAt,
                        LastExportBy = lastExport?.Actor
                    };
                });

        var analyticsInsights = analyticsDashboards.Select(dashboard =>
        {
            byDashboard.TryGetValue(dashboard.Id, out var usage);
            return new AnalyticsUsageInsight
            {
                DashboardId = dashboard.Id,
                DashboardName = dashboard.Name,
                DashboardNameArabic = dashboard.NameArabic,
                Status = dashboard.Status.ToString(),
                ViewCount = usage?.ViewCount ?? 0,
                QueryCount = usage?.QueryCount ?? 0,
                ExportCount = usage?.ExportCount ?? 0,
                LastViewAt = usage?.LastViewAt,
                LastViewBy = usage?.LastViewBy,
                LastExportAt = usage?.LastExportAt,
                LastExportBy = usage?.LastExportBy
            };
        })
        .OrderByDescending(item => item.ViewCount + item.QueryCount + item.ExportCount)
        .ThenBy(item => item.DashboardName)
        .ToList();

        var cached = freshness.Where(d => d.IsCached).ToList();
        var actors = usageEvents
            .Select(e => e.Actor)
            .Concat(analyticsEvents.Select(e => e.Actor))
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var summary = new ReportInsightsSummary
        {
            Days = days,
            ReportRuns = usageEvents.Count(e => e.Action == "report.run"),
            ReportExports = usageEvents.Count(e => e.Action == "report.export"),
            UniqueReportsRun = usageEvents.Where(e => e.Action == "report.run").Select(e => e.ReportId).Distinct().Count(),
            ActiveUsers = actors,
            CachedDatasets = cached.Count,
            CacheReady = cached.Count(d => string.Equals(d.CacheStatus, DatasetCacheStatuses.Ready, StringComparison.OrdinalIgnoreCase)),
            CacheFailed = cached.Count(d => string.Equals(d.CacheStatus, DatasetCacheStatuses.Failed, StringComparison.OrdinalIgnoreCase)),
            ServingFromCache = reportInsights.Count(r => r.ServingFrom == "cache"),
            AnalyticsViews = analyticsEvents.Count(e => e.Action == "analytics.view"),
            AnalyticsQueries = analyticsEvents.Count(e => e.Action == "analytics.query"),
            AnalyticsExports = analyticsEvents.Count(e => e.Action == "analytics.export"),
            UniqueDashboardsViewed = analyticsEvents
                .Where(e => e.Action is "analytics.view" or "analytics.query")
                .Select(e => e.DashboardId)
                .Distinct()
                .Count()
        };

        return new ReportInsightsResult
        {
            Summary = summary,
            Reports = reportInsights,
            Analytics = analyticsInsights,
            Freshness = freshness
        };
    }
}
