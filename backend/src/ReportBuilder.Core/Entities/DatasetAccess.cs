namespace ReportBuilder.Core.Entities;

public class DatasetAccess
{
    public Guid DatasetId { get; set; }
    public Guid UserId { get; set; }

    public Dataset Dataset { get; set; } = null!;
    public User User { get; set; } = null!;
}
