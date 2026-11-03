using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReportBuilder.Core.Models;

public sealed class AnalyticsDashboardConfig
{
    public int Version { get; set; } = 1;
    public List<AnalyticsSource> Sources { get; set; } = [];
    public List<AnalyticsLink> Links { get; set; } = [];
    public List<AnalyticsDimension> Dimensions { get; set; } = [];
    public List<AnalyticsMeasure> Measures { get; set; } = [];
    public List<AnalyticsDetailColumn> DetailColumns { get; set; } = [];
    public List<AnalyticsChart> Charts { get; set; } = [];
    public AnalyticsMap? Map { get; set; }
}

public sealed class AnalyticsSource
{
    public string Id { get; set; } = string.Empty;
    public Guid DatasetId { get; set; }
    public string Alias { get; set; } = string.Empty;
}

public sealed class AnalyticsLink
{
    public string Id { get; set; } = string.Empty;
    public string LeftSourceId { get; set; } = string.Empty;
    public string RightSourceId { get; set; } = string.Empty;
    public List<string> LeftKeys { get; set; } = [];
    public List<string> RightKeys { get; set; } = [];
    public string JoinType { get; set; } = "inner";
}

public sealed class AnalyticsDimension
{
    public string Id { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    /// <summary>Catalog column name. Optional when <see cref="Expression"/> is set.</summary>
    public string Field { get; set; } = string.Empty;
    /// <summary>Optional calculated expression, e.g. YEAR([OrderDate]).</summary>
    public string? Expression { get; set; }
    public string Label { get; set; } = string.Empty;
}

public sealed class AnalyticsMeasure
{
    public string Id { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string? Field { get; set; }
    /// <summary>Optional calculated expression. With aggregation "expr", this is the full measure SQL.</summary>
    public string? Expression { get; set; }
    public string Aggregation { get; set; } = "count";
    public string Label { get; set; } = string.Empty;
    public string? Format { get; set; }
}

public sealed class AnalyticsDetailColumn
{
    public string Id { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string Field { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    /// <summary>Optional URL template, e.g. https://erp/orders/{OrderId}. Placeholders match field or column id.</summary>
    public string? UrlTemplate { get; set; }
}

public sealed class AnalyticsChart
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "bar";
    public string DimensionId { get; set; } = string.Empty;
    public string MeasureId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    /// <summary>Selection state this chart listens to: default or compare.</summary>
    public string State { get; set; } = "default";
}

public sealed class AnalyticsMap
{
    public bool Enabled { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public string LatField { get; set; } = string.Empty;
    public string LngField { get; set; } = string.Empty;
    public string? LabelField { get; set; }
}

public static class AnalyticsConfigJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public static string Serialize(AnalyticsDashboardConfig config)
        => JsonSerializer.Serialize(config, Options);

    public static AnalyticsDashboardConfig Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new AnalyticsDashboardConfig();
        }

        try
        {
            return JsonSerializer.Deserialize<AnalyticsDashboardConfig>(json, Options)
                ?? new AnalyticsDashboardConfig();
        }
        catch (JsonException)
        {
            return new AnalyticsDashboardConfig();
        }
    }
}
