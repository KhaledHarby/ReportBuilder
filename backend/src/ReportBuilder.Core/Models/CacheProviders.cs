namespace ReportBuilder.Core.Models;

public static class CacheProviders
{
    public const string SqlServer = "SqlServer";
    public const string ClickHouse = "ClickHouse";
    public const string Timescale = "Timescale";
    public const string Redis = "Redis";
    public const string Postgres = "Postgres";
    public const string Mongo = "Mongo";

    public static readonly string[] All =
    [
        SqlServer,
        ClickHouse,
        Timescale,
        Redis,
        Postgres,
        Mongo
    ];

    public static readonly string[] Implemented =
    [
        SqlServer,
        ClickHouse,
        Redis,
        Postgres,
        Mongo
    ];

    public static readonly string[] ComingSoon =
    [
        Timescale
    ];

    public static string Normalize(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return SqlServer;
        }

        var value = provider.Trim();
        foreach (var known in All)
        {
            if (known.Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        return SqlServer;
    }

    public static bool IsImplemented(string? provider)
        => Implemented.Contains(Normalize(provider), StringComparer.OrdinalIgnoreCase);
}
