namespace ReportBuilder.Core.Models;

public static class SortDirections
{
    public const string Asc = "asc";
    public const string Desc = "desc";

    public static string Normalize(string? direction, string defaultDirection = Desc) =>
        string.IsNullOrWhiteSpace(direction)
            ? Normalize(defaultDirection)
            : string.Equals(direction, Desc, StringComparison.OrdinalIgnoreCase)
                ? Desc
                : Asc;

    public static string ToSql(string? direction, string defaultDirection = Desc) =>
        string.Equals(Normalize(direction, defaultDirection), Desc, StringComparison.OrdinalIgnoreCase)
            ? "DESC"
            : "ASC";
}
