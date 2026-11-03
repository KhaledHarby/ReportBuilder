using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Identity;

namespace ReportBuilder.Infrastructure.Identity;

internal static class IdentityMapper
{
    public static UserAccount ToAccount(User user, IReadOnlyDictionary<Guid, string> names)
    {
        var roleIds = user.UserRoles.Select(link => link.RoleId).Distinct().ToList();
        var directPermissionIds = user.UserPermissions.Select(link => link.PermissionId).Distinct().ToList();
        return new UserAccount
        {
            Id = user.Id,
            UserName = user.UserName,
            Email = user.Email,
            DisplayName = user.DisplayName,
            DisplayNameArabic = user.DisplayNameArabic,
            ExternalSource = string.IsNullOrWhiteSpace(user.ExternalSource) ? null : user.ExternalSource,
            IsActive = user.IsActive,
            Roles = IdentityQueries.RoleNames(user),
            Permissions = IdentityQueries.EffectivePermissions(user),
            RoleIds = roleIds,
            DirectPermissionIds = directPermissionIds,
            CreatedBy = user.CreatedBy,
            CreatedByName = Name(names, user.CreatedBy),
            CreatedAt = user.CreatedAt,
            ModifiedBy = user.ModifiedBy,
            ModifiedByName = Name(names, user.ModifiedBy),
            ModifiedAt = user.ModifiedAt
        };
    }

    public static RoleRecord ToRole(Role role, IReadOnlyDictionary<Guid, string> names)
    {
        var permissionIds = role.RolePermissions.Select(link => link.PermissionId).Distinct().ToList();
        return new RoleRecord
        {
            Id = role.Id,
            Name = role.Name,
            Description = role.Description,
            IsSystem = role.IsSystem,
            PermissionIds = permissionIds,
            Permissions = role.RolePermissions
                .Select(link => link.Permission.Code)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            UserCount = role.UserRoles.Count,
            CreatedBy = role.CreatedBy,
            CreatedByName = Name(names, role.CreatedBy),
            CreatedAt = role.CreatedAt,
            ModifiedBy = role.ModifiedBy,
            ModifiedByName = Name(names, role.ModifiedBy),
            ModifiedAt = role.ModifiedAt
        };
    }

    public static PermissionRecord ToPermission(Permission permission, IReadOnlyDictionary<Guid, string> names) => new()
    {
        Id = permission.Id,
        Code = permission.Code,
        Name = permission.Name,
        Description = permission.Description,
        IsSystem = permission.IsSystem,
        CreatedBy = permission.CreatedBy,
        CreatedByName = Name(names, permission.CreatedBy),
        CreatedAt = permission.CreatedAt,
        ModifiedBy = permission.ModifiedBy,
        ModifiedByName = Name(names, permission.ModifiedBy),
        ModifiedAt = permission.ModifiedAt
    };

    private static string? Name(IReadOnlyDictionary<Guid, string> names, Guid? id) =>
        id is { } value && names.TryGetValue(value, out var name) ? name : null;
}
