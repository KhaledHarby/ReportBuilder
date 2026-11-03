using System.Text.Json.Serialization;

namespace ReportBuilder.Core.Models;

public sealed class ReportLayout
{
    public string Kind { get; set; } = "table";
    public List<LayoutColumn> Columns { get; set; } = [];
    public List<string> GroupBy { get; set; } = [];
    public List<LayoutFilter> Filters { get; set; } = [];
    /// <summary>How many filters share one row in the report viewer. Missing or invalid values use 2.</summary>
    public int FiltersPerRow { get; set; } = 2;
    public List<LayoutOrderBy> OrderBy { get; set; } = [];
    /// <summary>Runtime only. Maps a lookup sort field to the text of each fact key.</summary>
    [JsonIgnore]
    public List<LookupSort> LookupSorts { get; set; } = [];
    public ChartConfig? Chart { get; set; }
    public UserScope? UserScope { get; set; }
    /// <summary>Child rows shown under a master row. The child query is separate from the master query.</summary>
    public ReportDetail? Detail { get; set; }

    public bool IsChart =>
        string.Equals(Kind, "chart", StringComparison.OrdinalIgnoreCase) || Chart is not null;
}

public sealed class ReportDetail
{
    public bool Enabled { get; set; }
    public Guid DatasetId { get; set; }
    public string ParentField { get; set; } = string.Empty;
    public string ChildField { get; set; } = string.Empty;
    public List<LayoutColumn> Columns { get; set; } = [];
    public List<LayoutFilter> Filters { get; set; } = [];
    public List<LayoutOrderBy> OrderBy { get; set; } = [];
}

public sealed class UserScope
{
    public bool Enabled { get; set; }
    public string Field { get; set; } = string.Empty;
    public string Property { get; set; } = UserScopeProperties.Id;
}

public sealed class ChartConfig
{
    public string Type { get; set; } = "bar";
    public bool Stacked { get; set; }
    public bool ShowLegend { get; set; } = true;
    public bool ShowDataLabels { get; set; }
}

public sealed class LayoutColumn
{
    public string Field { get; set; } = string.Empty;
    /// <summary>When set, the column is calculated from this expression instead of a dataset field. Use [ColumnName] references.</summary>
    public string? Expression { get; set; }
    public string? Alias { get; set; }
    public string? Aggregate { get; set; }
    public string? Format { get; set; }
    /// <summary>Optional URL template, e.g. https://example/{Field}. Placeholders match field or alias.</summary>
    public string? UrlTemplate { get; set; }
    /// <summary>default (or empty) = all languages; en = English view only; ar = Arabic view only.</summary>
    public string? Locale { get; set; }
    /// <summary>left, center, or right. Empty follows the grid default.</summary>
    public string? Align { get; set; }
    /// <summary>Shared title for neighboring columns with the same text.</summary>
    public string? HeaderBand { get; set; }
    public string? HeaderBandArabic { get; set; }
    /// <summary>Stay visible while the report grid scrolls horizontally.</summary>
    public bool Sticky { get; set; }
    /// <summary>Show the full value on more than one line.</summary>
    public bool Wrap { get; set; }
    /// <summary>First matching rule wins. Applied in the report grid, not in SQL.</summary>
    public List<ConditionalFormatRule>? ConditionalFormat { get; set; }
}

public sealed class ConditionalFormatRule
{
    public string Operator { get; set; } = "eq";
    public string? Value { get; set; }
    public string? BackgroundColor { get; set; }
    public string? Color { get; set; }
    public bool Bold { get; set; }
}

public sealed class LayoutFilter
{
    public string Field { get; set; } = string.Empty;
    public string Operator { get; set; } = "eq";
    public string? Value { get; set; }
    public bool IsFixed { get; set; }
    /// <summary>When true the filter still applies to the query but is not shown on the report view.</summary>
    public bool IsHidden { get; set; }
    /// <summary>The report does not run or export until this filter has a value.</summary>
    public bool IsRequired { get; set; }
    public string? Alias { get; set; }
    public string? AliasArabic { get; set; }
    /// <summary>Optional parent filter field. The value list is narrowed by that filter (and its ancestors).</summary>
    public string? DependsOn { get; set; }
}

public sealed class LayoutOrderBy
{
    public string Field { get; set; } = string.Empty;
    public string Direction { get; set; } = "asc";
}

public sealed class LookupSort
{
    public string Field { get; set; } = string.Empty;
    public string FactColumn { get; set; } = string.Empty;
    public Dictionary<string, string> TextByKey { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
