using System.Text.RegularExpressions;
using ReportBuilder.Core.Models;

namespace ReportBuilder.Core.Query;

public static partial class ReadOnlySelect
{
    public const int MaxLength = 100_000;

    public static string Normalize(string? sql)
    {
        var text = (sql ?? string.Empty).Trim().TrimEnd(';').Trim();
        if (text.Length == 0)
        {
            throw new ReportQueryException("Dataset query is required.");
        }

        if (text.Length > MaxLength)
        {
            throw new ReportQueryException("Dataset query is too long.");
        }

        if (text.Contains(';'))
        {
            throw new ReportQueryException("Dataset query must be a single SELECT.");
        }

        if (!StartsWithSelect().IsMatch(text) || Forbidden().IsMatch(text))
        {
            throw new ReportQueryException("Dataset query must be a single SELECT.");
        }

        return text;
    }

    [GeneratedRegex(@"^select\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StartsWithSelect();

    [GeneratedRegex(
        @"\b(insert|update|delete|drop|alter|create|exec|execute|merge|truncate|grant|revoke|openrowset|opendatasource|openquery|into|backup|restore|dbcc|waitfor|shutdown|reconfigure|kill)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Forbidden();
}
