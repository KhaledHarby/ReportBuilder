namespace ReportBuilder.Core.Entities;

public class ReportScheduleRecipient
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? RoleId { get; set; }
    public string? Email { get; set; }
}
