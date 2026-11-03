using Microsoft.EntityFrameworkCore;
using ReportBuilder.Core.Entities;

namespace ReportBuilder.Infrastructure.Identity;

internal static class IdentityQueries
{
    public static IQueryable<User> WithAccess(this IQueryable<User> users) =>
        users
            .Include(user => user.UserRoles)
                .ThenInclude(link => link.Role)
                    .ThenInclude(role => role.RolePermissions)
                        .ThenInclude(link => link.Permission)
            .Include(user => user.UserPermissions)
                .ThenInclude(link => link.Permission)
            .AsSplitQuery();

    public static IQueryable<Role> WithPermissions(this IQueryable<Role> roles) =>
        roles
            .Include(role => role.RolePermissions)
                .ThenInclude(link => link.Permission)
            .Include(role => role.UserRoles)
            .AsSplitQuery();

    public static IReadOnlyList<string> EffectivePermissions(User user) =>
        user.UserRoles
            .SelectMany(link => link.Role.RolePermissions.Select(permission => permission.Permission.Code))
            .Concat(user.UserPermissions.Select(link => link.Permission.Code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IReadOnlyList<string> RoleNames(User user) =>
        user.UserRoles
            .Select(link => link.Role.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
