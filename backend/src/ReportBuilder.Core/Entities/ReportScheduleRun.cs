namespace ReportBuilder.Core.Entities;

public class ReportScheduleRun
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string Status { get; set; } = "Failed";
    public int RecipientCount { get; set; }
    public string? Message { get; set; }
}
