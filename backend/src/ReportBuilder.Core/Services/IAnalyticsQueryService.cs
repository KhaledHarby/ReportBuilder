using ReportBuilder.Core.Models;
using ReportBuilder.Core.Query;

namespace ReportBuilder.Core.Services;

public interface IAnalyticsQueryService
{
    Task<AnalyticsSnapshotResult> QuerySnapshotAsync(
        Guid dashboardId,
        AnalyticsQueryRequest request,
        CancellationToken cancellationToken = default);

    Task<AnalyticsDataResult> QueryDataAsync(
        Guid dashboardId,
        AnalyticsQueryRequest request,
        CancellationToken cancellationToken = default);

    Task<AnalyticsMapResult> QueryMapAsync(
        Guid dashboardId,
        AnalyticsQueryRequest request,
        CancellationToken cancellationToken = default);

    Task<(string FileName, byte[] Content, string ContentType)> ExportCsvAsync(
        Guid dashboardId,
        AnalyticsQueryRequest request,
        CancellationToken cancellationToken = default);

    Task<AnalyticsSearchResult> SearchValuesAsync(
        Guid dashboardId,
        AnalyticsValueSearchRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class AnalyticsValueSearchRequest
{
    public Dictionary<string, List<string>> Selections { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Query { get; set; } = string.Empty;
    public string? DimensionId { get; set; }
    public int Take { get; set; } = AnalyticsQueryBuilder.MaxSearchHits;
}

public sealed class AnalyticsSearchResult
{
    public IReadOnlyList<AnalyticsSearchHit> Hits { get; init; } = [];
}

public sealed class AnalyticsSearchHit
{
    public string DimensionId { get; init; } = string.Empty;
    public string DimensionLabel { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public long Count { get; init; }
    public string State { get; init; } = AnalyticsValueStates.Associated;
}

public sealed class AnalyticsQueryRequest
{
    public Dictionary<string, List<string>> Selections { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int Skip { get; set; }
    public int Take { get; set; } = 50;
    public bool? Track { get; set; }
}

public sealed class AnalyticsSnapshotResult
{
    public IReadOnlyList<AnalyticsDimensionResult> Dimensions { get; init; } = [];
    public IReadOnlyList<AnalyticsMeasureResult> Measures { get; init; } = [];
    public IReadOnlyList<AnalyticsChartResult> Charts { get; init; } = [];
    /// <summary>Where the numbers came from: see <see cref="AnalyticsDataSources"/>.</summary>
    public string Source { get; init; } = AnalyticsDataSources.Live;
    /// <summary>Time the engine spent producing this snapshot. Repeated from the shared cache unchanged.</summary>
    public long ElapsedMs { get; init; }
}

public static class AnalyticsDataSources
{
    /// <summary>The live SQL view or table.</summary>
    public const string Live = "live";
    /// <summary>The dataset's SQL Server snapshot table.</summary>
    public const string SqlServerCache = "sqlserver";
    /// <summary>The dataset's MongoDB snapshot collection.</summary>
    public const string MongoCache = "mongo";
}

public sealed class AnalyticsDataResult
{
    public IReadOnlyList<AnalyticsGridColumn> Columns { get; init; } = [];
    public IReadOnlyList<Dictionary<string, object?>> Rows { get; init; } = [];
    public long TotalCount { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; }
}

public sealed class AnalyticsMapResult
{
    public IReadOnlyList<AnalyticsMapPoint> MapPoints { get; init; } = [];
}

public sealed class AnalyticsDimensionResult
{
    public string DimensionId { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public IReadOnlyList<string> SelectedValues { get; init; } = [];
    public IReadOnlyList<AnalyticsDimensionValue> Values { get; init; } = [];
}

public sealed class AnalyticsDimensionValue
{
    public string Value { get; init; } = string.Empty;
    public long Count { get; init; }
    public string State { get; init; } = AnalyticsValueStates.Associated;
}

public sealed class AnalyticsMeasureResult
{
    public string MeasureId { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string? FormattedValue { get; init; }
    public double? NumericValue { get; init; }
}

public sealed class AnalyticsGridColumn
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string? UrlTemplate { get; init; }
}

public sealed class AnalyticsChartPoint
{
    public string Value { get; init; } = string.Empty;
    public double Metric { get; init; }
}

public sealed class AnalyticsChartResult
{
    public string ChartId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Type { get; init; } = "bar";
    public string DimensionId { get; init; } = string.Empty;
    public string MeasureId { get; init; } = string.Empty;
    public string State { get; init; } = "default";
    public IReadOnlyList<AnalyticsChartPoint> Points { get; init; } = [];
}

public sealed class AnalyticsMapPoint
{
    public double Lat { get; init; }
    public double Lng { get; init; }
    public string Label { get; init; } = string.Empty;
}

public static class AnalyticsValueStates
{
    public const string Selected = "selected";
    public const string Associated = "associated";
    public const string Excluded = "excluded";
}

public static class AnalyticsNullValue
{
    public const string Sentinel = "__RB_NULL__";
}
