using ReportBuilder.Core.Models;

namespace ReportBuilder.Core.Query;

public static class RequiredFilters
{
    public static void EnsureSatisfied(ReportLayout layout)
    {
        var missing = layout.Filters
            .Where(IsMissing)
            .Select(Label)
            .Where(label => label.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (missing.Count == 0)
        {
            return;
        }

        throw new ReportQueryException(
            "Choose a value for " + string.Join(", ", missing) + " before this report can run or export.");
    }

    public static bool IsMissing(LayoutFilter filter)
    {
        if (!filter.IsRequired)
        {
            return false;
        }

        var op = (filter.Operator ?? "eq").Trim().ToLowerInvariant();
        if (op is "isnull" or "isnotnull")
        {
            return false;
        }

        if (op is "eq" or "in")
        {
            return (filter.Value ?? string.Empty)
                .Split([',', ';', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Length == 0;
        }

        return string.IsNullOrWhiteSpace(filter.Value);
    }

    private static string Label(LayoutFilter filter)
    {
        var alias = filter.Alias?.Trim();
        if (!string.IsNullOrEmpty(alias))
        {
            return alias;
        }

        return filter.Field?.Trim() ?? string.Empty;
    }
}
