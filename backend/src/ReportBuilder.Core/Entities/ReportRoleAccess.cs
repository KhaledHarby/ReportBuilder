namespace ReportBuilder.Core.Entities;

public class ReportRoleAccess
{
    public Guid ReportId { get; set; }
    public Guid RoleId { get; set; }

    public ReportTemplate Report { get; set; } = null!;
    public Role Role { get; set; } = null!;
}
