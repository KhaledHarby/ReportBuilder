using Microsoft.EntityFrameworkCore;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Identity;
using ReportBuilder.Core.Services;
using ReportBuilder.Infrastructure.Data;

namespace ReportBuilder.Infrastructure.Identity;

public sealed class UserAdminService : IUserAdminService
{
    private readonly ReportBuilderDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IActivityLog _log;

    public UserAdminService(ReportBuilderDbContext db, ICurrentUser currentUser, IActivityLog log)
    {
        _db = db;
        _currentUser = currentUser;
        _log = log;
    }

    public async Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken = default)
    {
        var users = await _db.Users.WithAccess()
            .AsNoTracking()
            .OrderBy(user => user.UserName)
            .ToListAsync(cancellationToken);

        var names = await IdentityLookup.NamesAsync(
            _db,
            users.SelectMany(user => new[] { user.CreatedBy, user.ModifiedBy }),
            cancellationToken);

        return users.Select(user => IdentityMapper.ToAccount(user, names)).ToList();
    }

    public async Task<UserAccount?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.WithAccess()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (user is null)
        {
            return null;
        }

        var names = await IdentityLookup.NamesAsync(_db, [user.CreatedBy, user.ModifiedBy], cancellationToken);
        return IdentityMapper.ToAccount(user, names);
    }

    public async Task<UserAccount> CreateAsync(UserWriteRequest request, CancellationToken cancellationToken = default)
    {
        IdentityRules.EnsurePassword(request.Password, required: true);
        var userName = IdentityRules.NormalizeUserName(request.UserName);
        var email = IdentityRules.NormalizeEmail(request.Email);
        var displayName = IdentityRules.NormalizeDisplayName(request.DisplayName);
        await EnsureUniqueAsync(userName, email, excludeId: null, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var actor = _currentUser.UserId;
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            Email = email,
            DisplayName = displayName,
            DisplayNameArabic = IdentityRules.OptionalArabicDisplayName(request.DisplayNameArabic, null),
            IsActive = request.IsActive,
            CreatedBy = actor,
            CreatedAt = now,
            ModifiedBy = actor,
            ModifiedAt = now
        };
        user.PasswordHash = IdentityRules.HashPassword(user, request.Password!);

        _db.Users.Add(user);
        await ReplaceAssignmentsAsync(user, request.RoleIds, request.PermissionIds, actor, now, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Users,
                Action = "user.create",
                Message = $"Created user {user.UserName}.",
                TargetUserId = user.Id,
                TargetUserName = user.UserName,
                EntityType = "User",
                EntityId = user.Id,
                EntityName = user.UserName
            },
            cancellationToken);

        return (await GetAsync(user.Id, cancellationToken))!;
    }

    public async Task<UserAccount> UpdateAsync(Guid id, UserWriteRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.WithAccess()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new IdentityException("User was not found.", 404);

        var email = IdentityRules.NormalizeEmail(request.Email);
        var displayName = IdentityRules.NormalizeDisplayName(request.DisplayName);
        await EnsureUniqueAsync(user.UserName, email, user.Id, cancellationToken);
        IdentityRules.EnsurePassword(request.Password, required: false);

        var wasAdmin = IsAdmin(user);
        user.Email = email;
        user.DisplayName = displayName;
        user.DisplayNameArabic = IdentityRules.OptionalArabicDisplayName(request.DisplayNameArabic, user.DisplayNameArabic);
        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            user.PasswordHash = IdentityRules.HashPassword(user, request.Password);
        }

        var now = DateTimeOffset.UtcNow;
        var actor = _currentUser.UserId;
        await ReplaceAssignmentsAsync(user, request.RoleIds, request.PermissionIds, actor, now, cancellationToken);

        if (wasAdmin && !IsAdmin(user))
        {
            await EnsureAnotherAdminAsync(user.Id, cancellationToken);
        }

        user.ModifiedBy = actor;
        user.ModifiedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Users,
                Action = "user.update",
                Message = $"Updated user {user.UserName}.",
                TargetUserId = user.Id,
                TargetUserName = user.UserName,
                EntityType = "User",
                EntityId = user.Id,
                EntityName = user.UserName
            },
            cancellationToken);
        return (await GetAsync(user.Id, cancellationToken))!;
    }

    public async Task<UserAccount> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.WithAccess()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new IdentityException("User was not found.", 404);

        if (_currentUser.UserId == user.Id && !isActive)
        {
            throw new IdentityException("You cannot deactivate your own account.");
        }

        if (user.IsActive && !isActive && IsAdmin(user))
        {
            await EnsureAnotherAdminAsync(user.Id, cancellationToken);
        }

        user.IsActive = isActive;
        user.ModifiedBy = _currentUser.UserId;
        user.ModifiedAt = DateTimeOffset.UtcNow;

        if (!isActive)
        {
            user.SecurityStamp = Guid.NewGuid();
            var tokens = await _db.RefreshTokens
                .Where(token => token.UserId == user.Id && token.RevokedAt == null)
                .ToListAsync(cancellationToken);
            foreach (var token in tokens)
            {
                token.RevokedAt = DateTimeOffset.UtcNow;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Users,
                Action = isActive ? "user.activate" : "user.deactivate",
                Message = isActive
                    ? $"Activated user {user.UserName}."
                    : $"Deactivated user {user.UserName}.",
                TargetUserId = user.Id,
                TargetUserName = user.UserName,
                EntityType = "User",
                EntityId = user.Id,
                EntityName = user.UserName
            },
            cancellationToken);
        return (await GetAsync(user.Id, cancellationToken))!;
    }

    public async Task SignOutEverywhereAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.WithAccess()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new IdentityException("User was not found.", 404);

        if (_currentUser.UserId == user.Id)
        {
            throw new IdentityException("Sign out of your own session from the sidebar.");
        }

        var tokens = await _db.RefreshTokens
            .Where(token => token.UserId == user.Id && token.RevokedAt == null)
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        user.SecurityStamp = Guid.NewGuid();
        user.ModifiedBy = _currentUser.UserId;
        user.ModifiedAt = now;
        foreach (var token in tokens)
        {
            token.RevokedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Users,
                Action = "user.signout",
                Message = $"Signed {user.UserName} out of all sessions.",
                TargetUserId = user.Id,
                TargetUserName = user.UserName,
                EntityType = "User",
                EntityId = user.Id,
                EntityName = user.UserName
            },
            cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.WithAccess()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new IdentityException("User was not found.", 404);

        if (_currentUser.UserId == user.Id)
        {
            throw new IdentityException("You cannot delete your own account.");
        }

        if (IsAdmin(user))
        {
            await EnsureAnotherAdminAsync(user.Id, cancellationToken);
        }

        var userName = user.UserName;
        var userId = user.Id;
        _db.Users.Remove(user);
        await _db.SaveChangesAsync(cancellationToken);
        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Users,
                Action = "user.delete",
                Message = $"Deleted user {userName}.",
                TargetUserId = userId,
                TargetUserName = userName,
                EntityType = "User",
                EntityId = userId,
                EntityName = userName
            },
            cancellationToken);
    }

    private async Task EnsureUniqueAsync(string userName, string email, Guid? excludeId, CancellationToken cancellationToken)
    {
        var taken = await _db.Users.AnyAsync(
            user => user.Id != excludeId && (user.UserName == userName || user.Email == email),
            cancellationToken);
        if (taken)
        {
            throw new IdentityException("A user with this user name or email already exists.");
        }
    }

    private async Task ReplaceAssignmentsAsync(
        User user,
        IReadOnlyList<Guid> roleIds,
        IReadOnlyList<Guid> permissionIds,
        Guid? actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var distinctRoles = roleIds.Distinct().ToList();
        var distinctPermissions = permissionIds.Distinct().ToList();

        var roles = await _db.Roles.Where(role => distinctRoles.Contains(role.Id)).ToListAsync(cancellationToken);
        if (roles.Count != distinctRoles.Count)
        {
            throw new IdentityException("One or more roles were not found.");
        }

        var permissions = await _db.Permissions
            .Where(permission => distinctPermissions.Contains(permission.Id))
            .ToListAsync(cancellationToken);
        if (permissions.Count != distinctPermissions.Count)
        {
            throw new IdentityException("One or more permissions were not found.");
        }

        user.UserRoles.Clear();
        foreach (var role in roles)
        {
            user.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = role.Id,
                Role = role,
                CreatedBy = actor,
                CreatedAt = now
            });
        }

        user.UserPermissions.Clear();
        foreach (var permission in permissions)
        {
            user.UserPermissions.Add(new UserPermission
            {
                UserId = user.Id,
                PermissionId = permission.Id,
                CreatedBy = actor,
                CreatedAt = now
            });
        }
    }

    private static bool IsAdmin(User user) =>
        user.UserRoles.Any(link =>
            link.Role is { } role
            && role.IsSystem
            && role.Name.Equals(IdentityDefaults.AdminRoleName, StringComparison.OrdinalIgnoreCase));

    private async Task EnsureAnotherAdminAsync(Guid excludeUserId, CancellationToken cancellationToken)
    {
        var remaining = await _db.UserRoles
            .AnyAsync(
                link => link.UserId != excludeUserId
                    && link.User.IsActive
                    && link.Role.IsSystem
                    && link.Role.Name == IdentityDefaults.AdminRoleName,
                cancellationToken);

        if (!remaining)
        {
            throw new IdentityException("At least one active Admin user must remain.");
        }
    }
}
