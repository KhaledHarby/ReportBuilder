namespace ReportBuilder.Core.Entities;

public static class CacheScheduleModes
{
    public const string Manual = "Manual";
    public const string Interval = "Interval";
    public const string DailyAt = "DailyAt";

    public static string Normalize(string? value)
        => value?.Trim() switch
        {
            Interval => Interval,
            DailyAt => DailyAt,
            _ => Manual
        };
}

public static class DatasetCacheStatuses
{
    public const string None = "None";
    public const string Refreshing = "Refreshing";
    public const string Ready = "Ready";
    public const string Failed = "Failed";
    public const string Cleared = "Cleared";
}
