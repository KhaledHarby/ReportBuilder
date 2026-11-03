using ReportBuilder.Core.Models;

namespace ReportBuilder.Core.Query;

public static class LayoutColumnLocales
{
    public const string Default = "default";
    public const string English = "en";
    public const string Arabic = "ar";

    public static bool IsVisible(LayoutColumn? column, string? lang)
    {
        var locale = Normalize(column?.Locale);
        if (locale is null || locale == Default)
        {
            return true;
        }

        var current = NormalizeLang(lang);
        return current is null || locale == current;
    }

    public static bool ShouldInclude(int index, ReportLayout layout, string? lang)
    {
        var column = index < layout.Columns.Count ? layout.Columns[index] : null;
        return IsVisible(column, lang);
    }

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var locale = value.Trim().ToLowerInvariant();
        return locale switch
        {
            "default" or "all" => Default,
            "en" or "english" => English,
            "ar" or "arabic" => Arabic,
            _ => Default
        };
    }

    public static string? NormalizeLang(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang))
        {
            return null;
        }

        var value = lang.Trim().ToLowerInvariant();
        if (value.StartsWith("ar", StringComparison.Ordinal))
        {
            return Arabic;
        }

        if (value.StartsWith("en", StringComparison.Ordinal))
        {
            return English;
        }

        return null;
    }
}
