using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Services;

namespace ReportBuilder.Api.Contracts;

public sealed class ActivityLogDto
{
    public Guid Id { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string Level { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
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

    public static ActivityLogDto From(ActivityLog row) => new()
    {
        Id = row.Id,
        OccurredAt = row.OccurredAt,
        Level = row.Level,
        Category = row.Category,
        Action = row.Action,
        Message = row.Message,
        ActorUserId = row.ActorUserId,
        ActorUserName = row.ActorUserName,
        TargetUserId = row.TargetUserId,
        TargetUserName = row.TargetUserName,
        EntityType = row.EntityType,
        EntityId = row.EntityId,
        EntityName = row.EntityName,
        HttpMethod = row.HttpMethod,
        Path = row.Path,
        StatusCode = row.StatusCode,
        DurationMs = row.DurationMs,
        IpAddress = row.IpAddress,
        CorrelationId = row.CorrelationId,
        Details = row.Details
    };
}

public sealed class ActivityLogPageDto
{
    public int Total { get; init; }
    public IReadOnlyList<ActivityLogDto> Items { get; init; } = [];

    public static ActivityLogPageDto From(ActivityLogPage page) => new()
    {
        Total = page.Total,
        Items = page.Items.Select(ActivityLogDto.From).ToList()
    };
}

public sealed class ActivityLogSettingsDto
{
    public int RetentionDays { get; init; }
    public int MinRetentionDays { get; init; }
    public int MaxRetentionDays { get; init; }

    public static ActivityLogSettingsDto From(ActivityLogSettings settings) => new()
    {
        RetentionDays = settings.RetentionDays,
        MinRetentionDays = settings.MinRetentionDays,
        MaxRetentionDays = settings.MaxRetentionDays
    };
}

public sealed class ActivityLogSettingsWriteDto
{
    public int RetentionDays { get; set; }
}
