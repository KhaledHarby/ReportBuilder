using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ReportBuilder.Infrastructure.Data;

namespace ReportBuilder.Api.Identity;

/// <summary>
/// Replaces token role claims with the roles stored for this user.
/// Report access then follows the current assignment, including after a role change.
/// </summary>
public sealed class CurrentUserRolesMiddleware
{
    private readonly RequestDelegate _next;

    public CurrentUserRolesMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ReportBuilderDbContext db)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && context.User.Identity is ClaimsIdentity identity)
        {
            var idValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.User.FindFirstValue("sub");
            if (Guid.TryParse(idValue, out var userId))
            {
                var names = await db.UserRoles.AsNoTracking()
                    .Where(link => link.UserId == userId)
                    .Select(link => link.Role.Name)
                    .ToListAsync(context.RequestAborted);

                foreach (var claim in identity.Claims
                             .Where(claim => claim.Type == ClaimTypes.Role || claim.Type == "role")
                             .ToList())
                {
                    identity.RemoveClaim(claim);
                }

                foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        identity.AddClaim(new Claim(ClaimTypes.Role, name));
                    }
                }
            }
        }

        await _next(context);
    }
}
