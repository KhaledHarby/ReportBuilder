using ReportBuilder.Core.Models;

namespace ReportBuilder.Core.Entities;

public enum LookupSourceKind
{
    View = 0,
    Query = 1,
    Table = 2
}

public class Lookup
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameArabic { get; set; }
    public LookupSourceKind SourceKind { get; set; }
    public string SchemaName { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public string? SqlText { get; set; }
    public string KeyColumn { get; set; } = string.Empty;
    public string LabelColumn { get; set; } = string.Empty;
    public string? SortColumn { get; set; }
    public string SortDirection { get; set; } = SortDirections.Asc;
    public bool IsValid { get; set; }
    public string? ValidationError { get; set; }
    public string ColumnsJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
