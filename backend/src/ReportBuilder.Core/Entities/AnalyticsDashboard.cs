using ReportBuilder.Core.Identity;

namespace ReportBuilder.Core.Entities;

public class AnalyticsDashboard
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameArabic { get; set; }
    public string? Description { get; set; }
    public string? DescriptionArabic { get; set; }
    public string ConfigJson { get; set; } = "{}";
    public ReportStatus Status { get; set; } = ReportStatus.Draft;
    public bool IsRestricted { get; set; }
    public string AccessMode { get; set; } = ReportAccessModes.Everyone;
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<AnalyticsAccess> Access { get; set; } = [];
    public ICollection<AnalyticsRoleAccess> RoleAccess { get; set; } = [];
}
