namespace ReportBuilder.Core.Entities;

public class LaunchCode
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public string? ReturnPath { get; set; }
    public string? Source { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public User User { get; set; } = null!;
    public bool IsActive => ConsumedAt is null && ExpiresAt > DateTimeOffset.UtcNow;
}
