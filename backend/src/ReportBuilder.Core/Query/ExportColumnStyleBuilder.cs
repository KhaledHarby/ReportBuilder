using ReportBuilder.Core.Models;

namespace ReportBuilder.Core.Query;

public static class ExportColumnStyleBuilder
{
    public static ExportColumnStyle[] For(IReadOnlyList<int> indexes, ReportLayout layout, string? lang)
    {
        var styles = new ExportColumnStyle[indexes.Count];
        for (var i = 0; i < indexes.Count; i++)
        {
            var index = indexes[i];
            var column = index >= 0 && index < layout.Columns.Count ? layout.Columns[index] : null;
            styles[i] = From(column, lang);
        }

        return styles;
    }

    public static ExportColumnStyle From(LayoutColumn? column, string? lang)
    {
        if (column is null)
        {
            return new ExportColumnStyle();
        }

        var align = column.Align?.Trim().ToLowerInvariant();
        if (align is not ("left" or "center" or "right"))
        {
            align = null;
        }

        return new ExportColumnStyle
        {
            Align = align,
            HeaderBand = Band(column, lang),
            Wrap = column.Wrap
        };
    }

    private static string? Band(LayoutColumn column, string? lang)
    {
        var english = column.HeaderBand?.Trim() ?? string.Empty;
        var arabic = column.HeaderBandArabic?.Trim() ?? string.Empty;
        var preferArabic = LayoutColumnLocales.NormalizeLang(lang) == LayoutColumnLocales.Arabic;
        var text = preferArabic && arabic.Length > 0 ? arabic : english.Length > 0 ? english : arabic;
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
