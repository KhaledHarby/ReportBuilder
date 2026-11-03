using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using ReportBuilder.Core.Identity;

namespace ReportBuilder.Api.Identity;

public sealed class CurrentUser : ICurrentUser
{
    public CurrentUser(IHttpContextAccessor accessor)
    {
        var user = accessor.HttpContext?.User;
        IsAuthenticated = user?.Identity?.IsAuthenticated == true;
        var idValue = user?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user?.FindFirstValue("sub");
        UserId = Guid.TryParse(idValue, out var id) ? id : null;
        UserName = user?.FindFirstValue(ClaimTypes.Name)
            ?? user?.FindFirstValue("unique_name")
            ?? user?.Identity?.Name;
        Email = user?.FindFirstValue(ClaimTypes.Email)
            ?? user?.FindFirstValue(JwtRegisteredClaimNames.Email);
        DisplayName = user?.FindFirstValue("display_name");
        Permissions = user?.FindAll("perm")
                .Select(claim => claim.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Roles = user?.FindAll(claim =>
                    claim.Type == ClaimTypes.Role || claim.Type == "role")
                .Select(claim => claim.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IsAdmin = Roles.Contains(IdentityDefaults.AdminRoleName);
    }

    public Guid? UserId { get; }
    public string? UserName { get; }
    public string? Email { get; }
    public string? DisplayName { get; }
    public bool IsAuthenticated { get; }
    public bool IsAdmin { get; }
    public IReadOnlySet<string> Roles { get; }
    public IReadOnlySet<string> Permissions { get; }
    public bool Has(string permission) => Permissions.Contains(permission);
}
