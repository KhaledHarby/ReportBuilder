using ReportBuilder.Core.Entities;

namespace ReportBuilder.Core.Identity;

public static class ReportAccessRules
{
    public static bool IsOwner(ReportTemplate report, ICurrentUser user) =>
        user.UserId is Guid id && report.CreatedBy == id;

    public static bool CanSee(ReportTemplate report, ICurrentUser user)
    {
        if (user.IsAdmin || IsOwner(report, user))
        {
            return true;
        }

        var mode = ReportAccessModes.Normalize(report.AccessMode, report.IsRestricted);
        if (mode == ReportAccessModes.Roles)
        {
            return report.RoleAccess.Any(row =>
                row.Role != null && user.Roles.Contains(row.Role.Name));
        }

        if (mode == ReportAccessModes.Everyone)
        {
            return true;
        }

        return user.UserId is Guid id && report.Access.Any(row => row.UserId == id);
    }

    public static bool CanView(ReportTemplate report, ICurrentUser user)
    {
        if (!CanSee(report, user))
        {
            return false;
        }

        if (report.Status == ReportStatus.Published)
        {
            return true;
        }

        return user.IsAdmin || IsOwner(report, user);
    }

    public static bool CanDesign(ReportTemplate report, ICurrentUser user)
    {
        if (user.IsAdmin)
        {
            return true;
        }

        if (report.IsAdminCreated)
        {
            return false;
        }

        return IsOwner(report, user);
    }
}
