namespace ReportBuilder.Core.Entities;

using ReportBuilder.Core.Models;

public class DatasetColumn
{
    public Guid Id { get; set; }
    public Guid DatasetId { get; set; }
    public Dataset Dataset { get; set; } = null!;
    public string ColumnName { get; set; } = string.Empty;
    public string SqlType { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? DisplayNameArabic { get; set; }
    public bool IsHidden { get; set; }
    public bool IsFilterable { get; set; } = true;
    public bool IsGroupable { get; set; } = true;
    /// <summary>When true, the cache table gets a nonclustered index on this column.</summary>
    public bool IsIndexed { get; set; }
    public string DefaultSortDirection { get; set; } = SortDirections.Desc;
    public int Ordinal { get; set; }
    public Guid? LookupId { get; set; }
    public Lookup? Lookup { get; set; }
}
