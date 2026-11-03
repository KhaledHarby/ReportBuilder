namespace ReportBuilder.Core.Query;

public static class LookupFilterKeys
{
    public const int MaxKeys = 2000;
    public const string AndIn = "andin";
    public const string AndNotIn = "andnin";
    public const string NoMatch = "nomatch";

    private const char Separator = '\u001e';

    public static string Join(IEnumerable<string> keys)
        => string.Join(Separator, keys);

    public static List<string> Split(string? raw)
        => (raw ?? string.Empty)
            .Split(Separator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();
}
