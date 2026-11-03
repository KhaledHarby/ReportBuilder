namespace ReportBuilder.Core.Services;

public sealed class ActivityEvent
{
    public string Level { get; init; } = ActivityLevels.Information;
    public string Category { get; init; } = ActivityCategories.System;
    public required string Action { get; init; }
    public required string Message { get; init; }
    public Guid? ActorUserId { get; init; }
    public string? ActorUserName { get; init; }
    public Guid? TargetUserId { get; init; }
    public string? TargetUserName { get; init; }
    public string? EntityType { get; init; }
    public Guid? EntityId { get; init; }
    public string? EntityName { get; init; }
    public string? HttpMethod { get; init; }
    public string? Path { get; init; }
    public int? StatusCode { get; init; }
    public int? DurationMs { get; init; }
    public string? IpAddress { get; init; }
    public string? CorrelationId { get; init; }
    public string? Details { get; init; }
}

public static class ActivityLevels
{
    public const string Information = "Information";
    public const string Warning = "Warning";
    public const string Error = "Error";
}

public static class ActivityCategories
{
    public const string Auth = "auth";
    public const string Users = "users";
    public const string Roles = "roles";
    public const string Permissions = "permissions";
    public const string Reports = "reports";
    public const string Mail = "mail";
    public const string Datasets = "datasets";
    public const string Analytics = "analytics";
    public const string Ai = "ai";
    public const string Http = "http";
    public const string System = "system";
}

public sealed class ActivityLogQuery
{
    public int Skip { get; init; }
    public int Take { get; init; } = 50;
    public string? Search { get; init; }
    public string? Level { get; init; }
    public string? Category { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public string? Actor { get; init; }
}

public sealed class ActivityLogPage
{
    public int Total { get; init; }
    public IReadOnlyList<Entities.ActivityLog> Items { get; init; } = [];
}

public sealed class ActivityLogSettings
{
    public int RetentionDays { get; init; }
    public int MinRetentionDays { get; init; }
    public int MaxRetentionDays { get; init; }
}

public interface IActivityLog
{
    Task WriteAsync(ActivityEvent entry, CancellationToken cancellationToken = default);
    Task<ActivityLogPage> ListAsync(ActivityLogQuery query, CancellationToken cancellationToken = default);
    Task<ActivityLogSettings> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<ActivityLogSettings> UpdateSettingsAsync(int retentionDays, CancellationToken cancellationToken = default);
    Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default);
}
