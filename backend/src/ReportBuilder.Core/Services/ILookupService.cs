using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Models;
using ReportBuilder.Core.Query;

namespace ReportBuilder.Core.Services;

public sealed class LookupColumnInfo
{
    public string Name { get; set; } = string.Empty;
    public string SqlType { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? DisplayNameArabic { get; set; }
    public string? Locale { get; set; }
    /// <summary>Null means a column saved before selection existed and stays available.</summary>
    public bool? Included { get; set; }
    public bool? ShowInDesigner { get; set; }
}

public sealed class LookupColumnLocale
{
    public string Name { get; set; } = string.Empty;
    public string? Locale { get; set; }
    public string? DisplayName { get; set; }
    public string? DisplayNameArabic { get; set; }
    public bool? Included { get; set; }
    public bool? ShowInDesigner { get; set; }
}

public sealed class LookupWriteRequest
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameArabic { get; set; }
    public LookupSourceKind SourceKind { get; set; }
    public string? SchemaName { get; set; }
    public string? ObjectName { get; set; }
    public string? SqlText { get; set; }
    public string KeyColumn { get; set; } = string.Empty;
    public string LabelColumn { get; set; } = string.Empty;
    public string? SortColumn { get; set; }
    public string? SortDirection { get; set; }
    public List<LookupColumnLocale>? ColumnLocales { get; set; }
}

public sealed class LookupObjectInfo
{
    public string SchemaName { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public string ObjectType { get; set; } = string.Empty;
}

public interface ILookupService
{
    Task<IReadOnlyList<Lookup>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LookupObjectInfo>> ListObjectsAsync(string schema, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LookupColumnInfo>> PreviewColumnsAsync(string schema, string objectName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LookupColumnInfo>> PreviewQueryColumnsAsync(string sql, CancellationToken cancellationToken = default);
    Task<Lookup> SaveAsync(LookupWriteRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ColumnValuesResult?> TryListValuesAsync(
        Guid lookupId,
        int take,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? restrictToKeys = null,
        bool sourceTruncated = false);
    Task<ReportResult> ApplyReportColumnsAsync(ReportResult result, LookupColumnPlan plan, string? lang = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LayoutFilter>> RewriteFiltersAsync(Dataset dataset, IReadOnlyList<LayoutFilter> filters, CancellationToken cancellationToken = default);
    Task AttachSortsAsync(Dataset dataset, ReportLayout layout, CancellationToken cancellationToken = default);
    Task<ColumnValuesResult?> TryListLookupColumnValuesAsync(
        Dataset dataset,
        string field,
        int take,
        string? lang = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? restrictToKeys = null,
        bool sourceTruncated = false);
}
