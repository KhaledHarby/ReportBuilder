using ReportBuilder.Core.Entities;

namespace ReportBuilder.Core.Identity;

public sealed class AccountUser : ICurrentUser
{
    public Guid? UserId { get; init; }
    public string? UserName { get; init; }
    public string? Email { get; init; }
    public string? DisplayName { get; init; }
    public bool IsAuthenticated { get; init; } = true;
    public bool IsAdmin { get; init; }
    public IReadOnlySet<string> Roles { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> Permissions { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public bool Has(string permission) => Permissions.Contains(permission);

    public static AccountUser From(User user)
    {
        var isAdmin = user.UserRoles.Any(link =>
            link.Role != null
            && link.Role.Name.Equals(IdentityDefaults.AdminRoleName, StringComparison.OrdinalIgnoreCase));
        var roles = user.UserRoles
            .Where(link => link.Role != null)
            .Select(link => link.Role.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new AccountUser
        {
            UserId = user.Id,
            UserName = user.UserName,
            Email = user.Email,
            DisplayName = user.DisplayName,
            IsAuthenticated = user.IsActive,
            IsAdmin = isAdmin,
            Roles = roles
        };
    }
}
