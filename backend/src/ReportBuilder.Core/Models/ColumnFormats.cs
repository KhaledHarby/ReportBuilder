namespace ReportBuilder.Core.Models;

public static class ColumnFormats
{
    public const string Date = "date";
    public const string DateTime = "datetime";
    public const string Time = "time";
    public const string Integer = "integer";
    public const string Decimal = "decimal";

    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        Date,
        DateTime,
        Time,
        Integer,
        Decimal
    };

    public static string? Normalize(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return null;
        }

        var value = format.Trim().ToLowerInvariant();
        return Allowed.Contains(value) ? value : null;
    }
}
