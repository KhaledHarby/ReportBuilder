using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Identity;
using ReportBuilder.Infrastructure.Data;
using ReportBuilder.Infrastructure.Options;

namespace ReportBuilder.Infrastructure.Identity;

public sealed class IdentitySeeder
{
    private readonly ReportBuilderDbContext _db;
    private readonly AuthOptions _options;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public IdentitySeeder(ReportBuilderDbContext db, IOptions<AuthOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!await IdentityTablesExistAsync(cancellationToken))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var actor = IdentityDefaults.SystemActorId;
        var permissionsByCode = await EnsurePermissionsAsync(now, actor, cancellationToken);
        var adminRole = await EnsureAdminRoleAsync(
            permissionsByCode.Values.Where(item => item.IsSystem),
            now,
            actor,
            cancellationToken);
        await EnsurePortalViewerRoleAsync(permissionsByCode, now, actor, cancellationToken);
        await EnsureAdminUserAsync(adminRole, now, actor, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> IdentityTablesExistAsync(CancellationToken cancellationToken)
    {
        await using var command = _db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_SCHEMA = 'rb' AND TABLE_NAME = 'Users'
            ) THEN 1 ELSE 0 END
            """;

        if (command.Connection!.State != System.Data.ConnectionState.Open)
        {
            await command.Connection.OpenAsync(cancellationToken);
        }

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result) == 1;
    }

    private async Task<Dictionary<string, Permission>> EnsurePermissionsAsync(
        DateTimeOffset now,
        Guid actor,
        CancellationToken cancellationToken)
    {
        var existing = await _db.Permissions.ToListAsync(cancellationToken);
        var byCode = existing.ToDictionary(item => item.Code, StringComparer.OrdinalIgnoreCase);

        foreach (var (code, name, description) in PermissionCodes.Catalog)
        {
            if (byCode.TryGetValue(code, out var permission))
            {
                permission.Name = name;
                permission.Description = description;
                permission.IsSystem = true;
                permission.ModifiedBy = actor;
                permission.ModifiedAt = now;
                continue;
            }

            permission = new Permission
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = name,
                Description = description,
                IsSystem = true,
                CreatedBy = actor,
                CreatedAt = now,
                ModifiedBy = actor,
                ModifiedAt = now
            };
            _db.Permissions.Add(permission);
            byCode[code] = permission;
        }

        return byCode;
    }

    private async Task<Role> EnsureAdminRoleAsync(
        IEnumerable<Permission> permissions,
        DateTimeOffset now,
        Guid actor,
        CancellationToken cancellationToken)
    {
        var role = await _db.Roles
            .Include(item => item.RolePermissions)
            .FirstOrDefaultAsync(
                item => item.Name == IdentityDefaults.AdminRoleName,
                cancellationToken);

        if (role is null)
        {
            role = new Role
            {
                Id = Guid.NewGuid(),
                Name = IdentityDefaults.AdminRoleName,
                Description = "Full access to report studio and identity administration.",
                IsSystem = true,
                CreatedBy = actor,
                CreatedAt = now,
                ModifiedBy = actor,
                ModifiedAt = now
            };
            _db.Roles.Add(role);
        }
        else
        {
            role.IsSystem = true;
            role.ModifiedBy = actor;
            role.ModifiedAt = now;
        }

        var assigned = role.RolePermissions.Select(link => link.PermissionId).ToHashSet();
        foreach (var permission in permissions)
        {
            if (assigned.Contains(permission.Id))
            {
                continue;
            }

            role.RolePermissions.Add(new RolePermission
            {
                RoleId = role.Id,
                PermissionId = permission.Id,
                CreatedBy = actor,
                CreatedAt = now
            });
        }

        return role;
    }

    private async Task EnsurePortalViewerRoleAsync(
        IReadOnlyDictionary<string, Permission> permissionsByCode,
        DateTimeOffset now,
        Guid actor,
        CancellationToken cancellationToken)
    {
        var exists = await _db.Roles.AnyAsync(
            item => item.Name == IdentityDefaults.PortalViewerRoleName,
            cancellationToken);
        if (exists)
        {
            return;
        }

        var role = new Role
        {
            Id = Guid.NewGuid(),
            Name = IdentityDefaults.PortalViewerRoleName,
            Description = "Open published reports and analytics launched from MECS or another portal.",
            IsSystem = true,
            CreatedBy = actor,
            CreatedAt = now,
            ModifiedBy = actor,
            ModifiedAt = now
        };
        _db.Roles.Add(role);

        string[] codes =
        [
            PermissionCodes.ReportsView,
            PermissionCodes.ReportsExport,
            PermissionCodes.AnalyticsView,
            PermissionCodes.InsightsView
        ];

        foreach (var code in codes)
        {
            if (!permissionsByCode.TryGetValue(code, out var permission))
            {
                continue;
            }

            role.RolePermissions.Add(new RolePermission
            {
                RoleId = role.Id,
                PermissionId = permission.Id,
                CreatedBy = actor,
                CreatedAt = now
            });
        }
    }

    private async Task EnsureAdminUserAsync(Role adminRole, DateTimeOffset now, Guid actor, CancellationToken cancellationToken)
    {
        var seed = _options.Admin;
        var userName = string.IsNullOrWhiteSpace(seed.UserName) ? "admin" : seed.UserName.Trim();
        var user = await _db.Users
            .Include(item => item.UserRoles)
            .FirstOrDefaultAsync(item => item.UserName == userName, cancellationToken);

        if (user is null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                UserName = userName,
                Email = string.IsNullOrWhiteSpace(seed.Email) ? "admin@local" : seed.Email.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(seed.DisplayName) ? "Administrator" : seed.DisplayName.Trim(),
                IsActive = true,
                CreatedBy = actor,
                CreatedAt = now,
                ModifiedBy = actor,
                ModifiedAt = now
            };
            user.PasswordHash = _passwordHasher.HashPassword(
                user,
                string.IsNullOrWhiteSpace(seed.Password) ? "ChangeMe!Admin1" : seed.Password);
            _db.Users.Add(user);
        }

        if (user.UserRoles.All(link => link.RoleId != adminRole.Id))
        {
            user.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = adminRole.Id,
                CreatedBy = actor,
                CreatedAt = now
            });
        }
    }
}
