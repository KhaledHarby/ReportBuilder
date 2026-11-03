namespace ReportBuilder.Core.Entities;

public class AnalyticsRoleAccess
{
    public Guid DashboardId { get; set; }
    public Guid RoleId { get; set; }

    public AnalyticsDashboard Dashboard { get; set; } = null!;
    public Role Role { get; set; } = null!;
}
