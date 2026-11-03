using System.Globalization;
using System.Text.RegularExpressions;
using ReportBuilder.Core.Models;

namespace ReportBuilder.Core.Query;

public static class ColumnValueFormatter
{
    public static string Format(
        object? value,
        string? format,
        IFormatProvider? provider = null,
        string? columnName = null)
    {
        if (value is null)
        {
            return string.Empty;
        }

        var normalized = ColumnFormats.Normalize(format);
        if (normalized is null)
        {
            return FormatDefault(value, provider, columnName);
        }

        return normalized switch
        {
            ColumnFormats.Date => FormatDate(value, "d", provider),
            ColumnFormats.DateTime => FormatDate(value, "g", provider),
            ColumnFormats.Time => FormatDate(value, "t", provider),
            ColumnFormats.Integer when TryNumber(value, out var number) =>
                number.ToString(IsYearColumn(columnName) ? "0" : "N0", provider ?? CultureInfo.InvariantCulture),
            ColumnFormats.Decimal when TryNumber(value, out var number) =>
                number.ToString("N2", provider ?? CultureInfo.InvariantCulture),
            _ => FormatDefault(value, provider, columnName)
        };
    }

    public static string FormatLayout(object? value, string? format, LayoutColumn? column, string? resultName = null)
        => Format(value, format, columnName: ColumnLabel(column, resultName));

    private static string? ColumnLabel(LayoutColumn? column, string? resultName)
    {
        var parts = new[] { column?.Alias, column?.Field, resultName };
        var label = string.Join(' ', parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        return string.IsNullOrWhiteSpace(label) ? null : label;
    }

    internal static bool IsYearColumn(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var text = name.Trim();
        return text.Contains("سنة", StringComparison.Ordinal)
            || YearSuffix.IsMatch(text)
            || YearToken.IsMatch(text);
    }

    private static readonly Regex YearSuffix = new(
        @"year$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex YearToken = new(
        @"(^|[^A-Za-z0-9])(year|yr)([^A-Za-z0-9]|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static string FormatDefault(object value, IFormatProvider? provider, string? columnName = null)
    {
        if (IsYearColumn(columnName) && TryNumber(value, out var number) && number == decimal.Truncate(number))
        {
            return number.ToString("0", CultureInfo.InvariantCulture);
        }

        return value switch
        {
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", provider ?? CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm:ss", provider ?? CultureInfo.InvariantCulture),
            DateOnly d => d.ToString("yyyy-MM-dd", provider ?? CultureInfo.InvariantCulture),
            TimeOnly t => t.ToString("HH:mm:ss", provider ?? CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, provider ?? CultureInfo.InvariantCulture) ?? string.Empty,
            _ => Convert.ToString(value, provider ?? CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private static string FormatDate(object value, string pattern, IFormatProvider? provider)
    {
        if (!TryDate(value, out var date))
        {
            return FormatDefault(value, provider);
        }

        return date.ToString(pattern, provider ?? CultureInfo.InvariantCulture);
    }

    private static bool TryDate(object value, out DateTime date)
    {
        switch (value)
        {
            case DateTime dt:
                date = dt;
                return true;
            case DateTimeOffset dto:
                date = dto.DateTime;
                return true;
            case DateOnly d:
                date = d.ToDateTime(TimeOnly.MinValue);
                return true;
            case TimeOnly t:
                date = DateTime.Today.Add(t.ToTimeSpan());
                return true;
            case string text when DateTime.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                out var parsed):
                date = parsed;
                return true;
            default:
                date = default;
                return false;
        }
    }

    private static bool TryNumber(object value, out decimal number)
    {
        switch (value)
        {
            case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                return true;
            case string text when decimal.TryParse(
                text,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var parsed):
                number = parsed;
                return true;
            default:
                number = default;
                return false;
        }
    }
}
