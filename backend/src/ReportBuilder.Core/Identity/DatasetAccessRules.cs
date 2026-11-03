using ReportBuilder.Core.Entities;

namespace ReportBuilder.Core.Identity;

public static class DatasetAccessRules
{
    public static bool CanUseInDesigner(Dataset dataset, ICurrentUser user)
    {
        if (user.IsAdmin)
        {
            return true;
        }

        var mode = ReportAccessModes.Normalize(dataset.AccessMode, dataset.IsRestricted);
        if (mode == ReportAccessModes.Roles)
        {
            return dataset.RoleAccess.Any(row =>
                row.Role != null && user.Roles.Contains(row.Role.Name));
        }

        if (mode == ReportAccessModes.Everyone)
        {
            return true;
        }

        return user.UserId is Guid id && dataset.Access.Any(row => row.UserId == id);
    }
}
