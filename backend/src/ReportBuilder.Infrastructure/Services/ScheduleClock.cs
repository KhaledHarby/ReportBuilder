using System.Globalization;

namespace ReportBuilder.Infrastructure.Services;

internal static class ScheduleClock
{
    private static readonly TimeZoneInfo Zone = ResolveZone();

    public static DateTimeOffset Next(
        string cadence,
        int? dayOfWeek,
        int? dayOfMonth,
        string localTime,
        DateTimeOffset afterUtc)
    {
        var time = TimeOnly.ParseExact(localTime, "HH:mm", CultureInfo.InvariantCulture);
        var afterLocal = TimeZoneInfo.ConvertTime(afterUtc, Zone).DateTime;

        if (string.Equals(cadence, "Weekly", StringComparison.OrdinalIgnoreCase))
        {
            var target = (DayOfWeek)Math.Clamp(dayOfWeek ?? 1, 0, 6);
            var delta = ((int)target - (int)afterLocal.DayOfWeek + 7) % 7;
            var candidate = afterLocal.Date.AddDays(delta).Add(time.ToTimeSpan());
            if (candidate <= afterLocal)
            {
                candidate = candidate.AddDays(7);
            }

            return ToOffset(candidate);
        }

        if (string.Equals(cadence, "Monthly", StringComparison.OrdinalIgnoreCase))
        {
            var day = Math.Clamp(dayOfMonth ?? 1, 1, 31);
            var candidate = OnDay(afterLocal.Year, afterLocal.Month, day, time);
            if (candidate <= afterLocal)
            {
                var next = afterLocal.AddMonths(1);
                candidate = OnDay(next.Year, next.Month, day, time);
            }

            return ToOffset(candidate);
        }

        var daily = afterLocal.Date.Add(time.ToTimeSpan());
        if (daily <= afterLocal)
        {
            daily = daily.AddDays(1);
        }

        return ToOffset(daily);
    }

    private static DateTime OnDay(int year, int month, int day, TimeOnly time)
    {
        var last = DateTime.DaysInMonth(year, month);
        return new DateTime(year, month, Math.Min(day, last)).Add(time.ToTimeSpan());
    }

    private static DateTimeOffset ToOffset(DateTime local)
        => new(local, Zone.GetUtcOffset(local));

    private static TimeZoneInfo ResolveZone()
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById("Asia/Dubai", out var zone))
        {
            return zone;
        }

        return TimeZoneInfo.FindSystemTimeZoneById("Arabian Standard Time");
    }
}
