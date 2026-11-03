namespace ReportBuilder.Core.Models;

public sealed class ReportResult
{
    public IReadOnlyList<ReportResultColumn> Columns { get; init; } = [];
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; init; } = [];
    public int TotalCount { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; }
}

public sealed class ReportResultColumn
{
    public string Name { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? DisplayNameArabic { get; init; }
    public bool Hidden { get; init; }
}

public sealed class ColumnValueChoice
{
    public string Value { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

public sealed class ColumnValuesResult
{
    public string Field { get; init; } = string.Empty;
    public IReadOnlyList<string> Values { get; init; } = [];
    public IReadOnlyList<ColumnValueChoice> Choices { get; init; } = [];
    public bool Truncated { get; init; }
}
