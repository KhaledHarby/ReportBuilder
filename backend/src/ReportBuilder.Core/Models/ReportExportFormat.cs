namespace ReportBuilder.Core.Models;

public enum ReportExportFormat
{
    Csv,
    Pdf,
    Docx,
    Xlsx
}

public static class ReportExportLimits
{
    public const int PrintableRowCap = 20_000;

    public static int? RowCapFor(ReportExportFormat format)
        => format is ReportExportFormat.Csv or ReportExportFormat.Xlsx ? null : PrintableRowCap;
}

public sealed class ReportExportTable
{
    public required string Title { get; init; }
    public string? TitleArabic { get; init; }
    public required IReadOnlyList<string> Columns { get; init; }
    public required IReadOnlyList<IReadOnlyList<string>> Rows { get; init; }
    public bool Truncated { get; init; }
    public string? ExportedBy { get; init; }
    public string? Organization { get; init; }
    public string? OrganizationArabic { get; init; }
    public string? Application { get; init; }
    public byte[]? Logo { get; init; }
    public bool Arabic { get; init; }
    public IReadOnlyList<ExportColumnStyle> ColumnStyles { get; init; } = [];
}

public sealed class ExportColumnStyle
{
    public string? Align { get; init; }
    public string? HeaderBand { get; init; }
    public bool Wrap { get; init; }
}

public readonly record struct ExportHeaderSpan(int Start, int Count, string? Title);

public static class ExportHeaderBands
{
    public static IReadOnlyList<ExportColumnStyle> Aligned(ReportExportTable table)
    {
        if (table.ColumnStyles.Count == table.Columns.Count)
        {
            return table.ColumnStyles;
        }

        var styles = new ExportColumnStyle[table.Columns.Count];
        for (var i = 0; i < styles.Length; i++)
        {
            styles[i] = i < table.ColumnStyles.Count ? table.ColumnStyles[i] : new ExportColumnStyle();
        }

        return styles;
    }

    public static bool Any(IReadOnlyList<ExportColumnStyle> styles)
        => styles.Any(style => !string.IsNullOrWhiteSpace(style.HeaderBand));

    public static List<ExportHeaderSpan> Spans(IReadOnlyList<ExportColumnStyle> styles)
    {
        var spans = new List<ExportHeaderSpan>();
        var index = 0;
        while (index < styles.Count)
        {
            var title = styles[index].HeaderBand?.Trim();
            if (string.IsNullOrEmpty(title))
            {
                spans.Add(new ExportHeaderSpan(index, 1, null));
                index++;
                continue;
            }

            var end = index + 1;
            while (end < styles.Count
                && string.Equals(styles[end].HeaderBand?.Trim(), title, StringComparison.Ordinal))
            {
                end++;
            }

            spans.Add(new ExportHeaderSpan(index, end - index, title));
            index = end;
        }

        return spans;
    }
}
