namespace ReportBuilder.Core.Models;

public static class ConditionalFormats
{
    public const int MaxRulesPerColumn = 12;

    private static readonly Dictionary<string, string> Operators = new(StringComparer.OrdinalIgnoreCase)
    {
        ["eq"] = "eq",
        ["neq"] = "neq",
        ["gt"] = "gt",
        ["gte"] = "gte",
        ["lt"] = "lt",
        ["lte"] = "lte",
        ["contains"] = "contains",
        ["in"] = "in",
        ["oneof"] = "in",
        ["isoneof"] = "in",
        ["isnull"] = "isNull",
        ["is_null"] = "isNull",
        ["isempty"] = "isNull",
        ["isnotnull"] = "isNotNull",
        ["is_not_null"] = "isNotNull",
        ["isnotempty"] = "isNotNull"
    };

    public static string NormalizeOperator(string? op)
    {
        if (string.IsNullOrWhiteSpace(op))
        {
            return "eq";
        }

        return Operators.TryGetValue(op.Trim(), out var value) ? value : "eq";
    }

    public static string? NormalizeColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return null;
        }

        var value = color.Trim();
        if (value.StartsWith('#'))
        {
            value = value[1..];
        }

        if (value.Length is not (3 or 6))
        {
            return null;
        }

        foreach (var ch in value)
        {
            if (!char.IsAsciiHexDigit(ch))
            {
                return null;
            }
        }

        if (value.Length == 3)
        {
            value = $"{value[0]}{value[0]}{value[1]}{value[1]}{value[2]}{value[2]}";
        }

        return "#" + value.ToLowerInvariant();
    }

    public static List<ConditionalFormatRule>? Sanitize(IEnumerable<ConditionalFormatRule>? rules)
    {
        if (rules is null)
        {
            return null;
        }

        var cleaned = new List<ConditionalFormatRule>();
        foreach (var rule in rules.Take(MaxRulesPerColumn))
        {
            cleaned.Add(new ConditionalFormatRule
            {
                Operator = NormalizeOperator(rule.Operator),
                Value = string.IsNullOrWhiteSpace(rule.Value) ? null : rule.Value.Trim(),
                BackgroundColor = NormalizeColor(rule.BackgroundColor),
                Color = NormalizeColor(rule.Color),
                Bold = rule.Bold
            });
        }

        return cleaned.Count == 0 ? null : cleaned;
    }
}
