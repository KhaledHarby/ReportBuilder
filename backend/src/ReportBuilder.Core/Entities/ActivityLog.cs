namespace ReportBuilder.Core.Entities;

public class ActivityLog
{
    public Guid Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string Level { get; set; } = "Information";
    public string Category { get; set; } = "system";
    public string Action { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Guid? ActorUserId { get; set; }
    public string? ActorUserName { get; set; }
    public Guid? TargetUserId { get; set; }
    public string? TargetUserName { get; set; }
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public string? EntityName { get; set; }
    public string? HttpMethod { get; set; }
    public string? Path { get; set; }
    public int? StatusCode { get; set; }
    public int? DurationMs { get; set; }
    public string? IpAddress { get; set; }
    public string? CorrelationId { get; set; }
    public string? Details { get; set; }
}
