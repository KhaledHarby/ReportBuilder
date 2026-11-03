namespace ReportBuilder.Core.Entities;

public class ReportAccess
{
    public Guid ReportId { get; set; }
    public Guid UserId { get; set; }

    public ReportTemplate Report { get; set; } = null!;
    public User User { get; set; } = null!;
}
