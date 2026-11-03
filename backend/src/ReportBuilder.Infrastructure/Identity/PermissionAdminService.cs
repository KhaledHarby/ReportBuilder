using Microsoft.EntityFrameworkCore;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Identity;
using ReportBuilder.Core.Services;
using ReportBuilder.Infrastructure.Data;

namespace ReportBuilder.Infrastructure.Identity;

public sealed class PermissionAdminService : IPermissionAdminService
{
    private readonly ReportBuilderDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IActivityLog _log;

    public PermissionAdminService(ReportBuilderDbContext db, ICurrentUser currentUser, IActivityLog log)
    {
        _db = db;
        _currentUser = currentUser;
        _log = log;
    }

    public async Task<IReadOnlyList<PermissionRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        var permissions = await _db.Permissions
            .AsNoTracking()
            .OrderBy(permission => permission.Code)
            .ToListAsync(cancellationToken);

        var names = await IdentityLookup.NamesAsync(
            _db,
            permissions.SelectMany(permission => new[] { permission.CreatedBy, permission.ModifiedBy }),
            cancellationToken);

        return permissions.Select(permission => IdentityMapper.ToPermission(permission, names)).ToList();
    }

    public async Task<PermissionRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var permission = await _db.Permissions.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (permission is null)
        {
            return null;
        }

        var names = await IdentityLookup.NamesAsync(_db, [permission.CreatedBy, permission.ModifiedBy], cancellationToken);
        return IdentityMapper.ToPermission(permission, names);
    }

    public async Task<PermissionRecord> CreateAsync(PermissionWriteRequest request, CancellationToken cancellationToken = default)
    {
        var code = IdentityRules.NormalizePermissionCode(request.Code);
        var name = IdentityRules.NormalizePermissionName(request.Name);
        if (await _db.Permissions.AnyAsync(permission => permission.Code == code, cancellationToken))
        {
            throw new IdentityException("A permission with this code already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        var actor = _currentUser.UserId;
        var permission = new Permission
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            Description = Trim(request.Description),
            IsSystem = false,
            CreatedBy = actor,
            CreatedAt = now,
            ModifiedBy = actor,
            ModifiedAt = now
        };

        _db.Permissions.Add(permission);
        await _db.SaveChangesAsync(cancellationToken);
        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Permissions,
                Action = "permission.create",
                Message = $"Created permission {permission.Code}.",
                EntityType = "Permission",
                EntityId = permission.Id,
                EntityName = permission.Code
            },
            cancellationToken);
        return IdentityMapper.ToPermission(permission, await IdentityLookup.NamesAsync(_db, [actor], cancellationToken));
    }

    public async Task<PermissionRecord> UpdateAsync(Guid id, PermissionWriteRequest request, CancellationToken cancellationToken = default)
    {
        var permission = await _db.Permissions.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new IdentityException("Permission was not found.", 404);

        var name = IdentityRules.NormalizePermissionName(request.Name);
        if (!permission.IsSystem)
        {
            var code = IdentityRules.NormalizePermissionCode(request.Code);
            if (await _db.Permissions.AnyAsync(item => item.Id != id && item.Code == code, cancellationToken))
            {
                throw new IdentityException("A permission with this code already exists.");
            }

            permission.Code = code;
        }

        permission.Name = name;
        permission.Description = Trim(request.Description);
        permission.ModifiedBy = _currentUser.UserId;
        permission.ModifiedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Permissions,
                Action = "permission.update",
                Message = $"Updated permission {permission.Code}.",
                EntityType = "Permission",
                EntityId = permission.Id,
                EntityName = permission.Code
            },
            cancellationToken);
        return (await GetAsync(permission.Id, cancellationToken))!;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var permission = await _db.Permissions.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new IdentityException("Permission was not found.", 404);

        if (permission.IsSystem)
        {
            throw new IdentityException("System permissions cannot be deleted.");
        }

        var code = permission.Code;
        var permissionId = permission.Id;
        _db.Permissions.Remove(permission);
        await _db.SaveChangesAsync(cancellationToken);
        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Permissions,
                Action = "permission.delete",
                Message = $"Deleted permission {code}.",
                EntityType = "Permission",
                EntityId = permissionId,
                EntityName = code
            },
            cancellationToken);
    }

    private static string? Trim(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
