using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using ReportBuilder.Core.Identity;

namespace ReportBuilder.Api.Identity;

public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string code)
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.HasClaim("perm", requirement.Code)
            || SatisfiedByBroaderGrant(context, requirement.Code))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    private static bool SatisfiedByBroaderGrant(AuthorizationHandlerContext context, string code)
    {
        if (code == PermissionCodes.LogsView)
        {
            return context.User.HasClaim("perm", PermissionCodes.LogsManage);
        }

        if (code == PermissionCodes.SettingsView)
        {
            return context.User.HasClaim("perm", PermissionCodes.SettingsUpdate);
        }

        if (code == PermissionCodes.SqlView)
        {
            return context.User.HasClaim("perm", PermissionCodes.SqlModify);
        }

        if (code == PermissionCodes.LookupsView)
        {
            return context.User.HasClaim("perm", PermissionCodes.LookupsUpdate);
        }

        return code == PermissionCodes.UsersActivate
            && context.User.HasClaim("perm", PermissionCodes.LogsManage);
    }
}

public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    {
        _fallback = new DefaultAuthorizationPolicyProvider(options);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (string.IsNullOrWhiteSpace(policyName))
        {
            return _fallback.GetPolicyAsync(policyName);
        }

        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(policyName))
            .Build();

        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}
