namespace ReportBuilder.Core.Entities;

public class AnalyticsAccess
{
    public Guid DashboardId { get; set; }
    public Guid UserId { get; set; }

    public AnalyticsDashboard Dashboard { get; set; } = null!;
    public User User { get; set; } = null!;
}
