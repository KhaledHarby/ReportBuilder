using Microsoft.EntityFrameworkCore;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Identity;
using ReportBuilder.Core.Services;
using ReportBuilder.Infrastructure.Data;

namespace ReportBuilder.Infrastructure.Identity;

public sealed class RoleAdminService : IRoleAdminService
{
    private readonly ReportBuilderDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IActivityLog _log;

    public RoleAdminService(ReportBuilderDbContext db, ICurrentUser currentUser, IActivityLog log)
    {
        _db = db;
        _currentUser = currentUser;
        _log = log;
    }

    public async Task<IReadOnlyList<RoleRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        var roles = await _db.Roles.WithPermissions()
            .AsNoTracking()
            .OrderBy(role => role.Name)
            .ToListAsync(cancellationToken);

        var names = await IdentityLookup.NamesAsync(
            _db,
            roles.SelectMany(role => new[] { role.CreatedBy, role.ModifiedBy }),
            cancellationToken);

        return roles.Select(role => IdentityMapper.ToRole(role, names)).ToList();
    }

    public async Task<RoleRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var role = await _db.Roles.WithPermissions()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (role is null)
        {
            return null;
        }

        var names = await IdentityLookup.NamesAsync(_db, [role.CreatedBy, role.ModifiedBy], cancellationToken);
        return IdentityMapper.ToRole(role, names);
    }

    public async Task<IReadOnlyList<RoleMember>?> ListUsersAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await _db.Roles.AnyAsync(role => role.Id == id, cancellationToken))
        {
            return null;
        }

        return await _db.Users
            .AsNoTracking()
            .Where(user => user.UserRoles.Any(link => link.RoleId == id))
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.UserName)
            .Select(user => new RoleMember
            {
                Id = user.Id,
                UserName = user.UserName,
                DisplayName = user.DisplayName,
                DisplayNameArabic = user.DisplayNameArabic,
                Email = user.Email,
                IsActive = user.IsActive
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<RoleRecord> CreateAsync(RoleWriteRequest request, CancellationToken cancellationToken = default)
    {
        var name = IdentityRules.NormalizeRoleName(request.Name);
        if (await _db.Roles.AnyAsync(role => role.Name == name, cancellationToken))
        {
            throw new IdentityException("A role with this name already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        var actor = _currentUser.UserId;
        var role = new Role
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = Trim(request.Description),
            IsSystem = false,
            CreatedBy = actor,
            CreatedAt = now,
            ModifiedBy = actor,
            ModifiedAt = now
        };

        _db.Roles.Add(role);
        await ReplacePermissionsAsync(role, request.PermissionIds, actor, now, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Roles,
                Action = "role.create",
                Message = $"Created role {role.Name}.",
                EntityType = "Role",
                EntityId = role.Id,
                EntityName = role.Name
            },
            cancellationToken);
        return (await GetAsync(role.Id, cancellationToken))!;
    }

    public async Task<RoleRecord> UpdateAsync(Guid id, RoleWriteRequest request, CancellationToken cancellationToken = default)
    {
        var role = await _db.Roles.WithPermissions()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new IdentityException("Role was not found.", 404);

        var name = IdentityRules.NormalizeRoleName(request.Name);
        if (role.IsSystem && !name.Equals(role.Name, StringComparison.Ordinal))
        {
            throw new IdentityException("The system Admin role cannot be renamed.");
        }

        if (await _db.Roles.AnyAsync(item => item.Id != id && item.Name == name, cancellationToken))
        {
            throw new IdentityException("A role with this name already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        var actor = _currentUser.UserId;
        role.Name = name;
        role.Description = Trim(request.Description);
        role.ModifiedBy = actor;
        role.ModifiedAt = now;
        await ReplacePermissionsAsync(role, request.PermissionIds, actor, now, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Roles,
                Action = "role.update",
                Message = $"Updated role {role.Name}.",
                EntityType = "Role",
                EntityId = role.Id,
                EntityName = role.Name
            },
            cancellationToken);
        return (await GetAsync(role.Id, cancellationToken))!;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new IdentityException("Role was not found.", 404);

        if (role.IsSystem)
        {
            throw new IdentityException("The system Admin role cannot be deleted.");
        }

        var roleName = role.Name;
        var roleId = role.Id;
        _db.Roles.Remove(role);
        await _db.SaveChangesAsync(cancellationToken);
        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Roles,
                Action = "role.delete",
                Message = $"Deleted role {roleName}.",
                EntityType = "Role",
                EntityId = roleId,
                EntityName = roleName
            },
            cancellationToken);
    }

    private async Task ReplacePermissionsAsync(
        Role role,
        IReadOnlyList<Guid> permissionIds,
        Guid? actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var distinct = permissionIds.Distinct().ToList();
        var permissions = await _db.Permissions
            .Where(permission => distinct.Contains(permission.Id))
            .ToListAsync(cancellationToken);

        if (permissions.Count != distinct.Count)
        {
            throw new IdentityException("One or more permissions were not found.");
        }

        role.RolePermissions.Clear();
        foreach (var permission in permissions)
        {
            role.RolePermissions.Add(new RolePermission
            {
                RoleId = role.Id,
                PermissionId = permission.Id,
                CreatedBy = actor,
                CreatedAt = now
            });
        }
    }

    private static string? Trim(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
