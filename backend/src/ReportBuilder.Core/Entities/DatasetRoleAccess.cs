namespace ReportBuilder.Core.Entities;

public class DatasetRoleAccess
{
    public Guid DatasetId { get; set; }
    public Guid RoleId { get; set; }

    public Dataset Dataset { get; set; } = null!;
    public Role Role { get; set; } = null!;
}
