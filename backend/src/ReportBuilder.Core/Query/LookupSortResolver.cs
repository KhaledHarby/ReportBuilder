using System.Globalization;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Models;

namespace ReportBuilder.Core.Query;

public static class LookupSortResolver
{
    public const int MaxKeys = 5_000;

    public static bool TryOrderColumn(
        Dataset dataset,
        ReportLayout layout,
        string field,
        out DatasetColumn column,
        out IReadOnlyDictionary<string, string>? lookupText)
    {
        column = null!;
        lookupText = null;
        if (!LookupFields.TryParse(field, out _, out _))
        {
            return false;
        }

        if (!LookupColumnPlan.TryResolve(dataset, field, out column, out _))
        {
            throw new ReportQueryException($"Unknown or hidden sort field '{field}'.");
        }

        var sort = layout.LookupSorts.FirstOrDefault(item =>
            item.Field.Equals(field, StringComparison.OrdinalIgnoreCase));
        lookupText = sort?.TextByKey ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return true;
    }

    public static string KeyOf(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is Guid guid)
        {
            return guid.ToString("D");
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
        return Guid.TryParse(text, out var parsed) ? parsed.ToString("D") : text;
    }

    public static string? Text(IReadOnlyDictionary<string, string>? map, object? cell)
    {
        if (map is null)
        {
            return null;
        }

        var key = KeyOf(cell);
        return key.Length > 0 && map.TryGetValue(key, out var text) ? text : null;
    }

    /// <summary>
    /// When every sort value is numeric, rewrite them so a text ORDER BY matches numeric order.
    /// </summary>
    public static void UseSortableValues(Dictionary<string, string> map)
    {
        if (map.Count == 0)
        {
            return;
        }

        var numbers = new Dictionary<string, decimal>(map.Comparer);
        foreach (var pair in map)
        {
            var text = pair.Value.Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return;
            }

            numbers[pair.Key] = number;
        }

        foreach (var pair in numbers)
        {
            map[pair.Key] = NumericToken(pair.Value);
        }
    }

    private static string NumericToken(decimal number)
    {
        var negative = number < 0;
        var formatted = decimal.Abs(number).ToString("F10", CultureInfo.InvariantCulture);
        var parts = formatted.Split('.');
        var whole = parts[0].PadLeft(29, '0');
        var fraction = parts.Length > 1 ? parts[1].PadRight(10, '0') : "0000000000";
        var body = whole + "." + fraction;
        if (!negative)
        {
            return "1" + body;
        }

        var chars = body.ToCharArray();
        for (var index = 0; index < chars.Length; index++)
        {
            if (chars[index] is >= '0' and <= '9')
            {
                chars[index] = (char)('9' - (chars[index] - '0'));
            }
        }

        return "0" + new string(chars);
    }
}
