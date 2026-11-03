using ReportBuilder.Core.Entities;

namespace ReportBuilder.Core.Identity;

public static class AnalyticsAccessRules
{
    public static bool IsOwner(AnalyticsDashboard dashboard, ICurrentUser user) =>
        user.UserId is Guid id && dashboard.CreatedBy == id;

    public static bool CanSee(AnalyticsDashboard dashboard, ICurrentUser user)
    {
        if (user.IsAdmin || IsOwner(dashboard, user))
        {
            return true;
        }

        var mode = ReportAccessModes.Normalize(dashboard.AccessMode, dashboard.IsRestricted);
        if (mode == ReportAccessModes.Roles)
        {
            return dashboard.RoleAccess.Any(row =>
                row.Role != null && user.Roles.Contains(row.Role.Name));
        }

        if (mode == ReportAccessModes.Everyone)
        {
            return true;
        }

        return user.UserId is Guid id && dashboard.Access.Any(row => row.UserId == id);
    }

    public static bool CanView(AnalyticsDashboard dashboard, ICurrentUser user)
    {
        if (!CanSee(dashboard, user))
        {
            return false;
        }

        if (dashboard.Status == ReportStatus.Published)
        {
            return true;
        }

        // Drafts are preview-only for owner/admin (or editors with update permission + ownership).
        return user.IsAdmin || IsOwner(dashboard, user);
    }

    public static bool CanDesign(AnalyticsDashboard dashboard, ICurrentUser user)
    {
        if (user.IsAdmin)
        {
            return true;
        }

        return IsOwner(dashboard, user);
    }
}
