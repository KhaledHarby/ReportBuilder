using ReportBuilder.Core.Identity;

namespace ReportBuilder.Infrastructure.Options;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string Issuer { get; set; } = "MECS.ReportBuilder";
    public string Audience { get; set; } = "MECS.ReportBuilder";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
    public AdminSeedOptions Admin { get; set; } = new();
    public LaunchOptions Launch { get; set; } = new();
}

public sealed class LaunchOptions
{
    public string Secret { get; set; } = string.Empty;
    public int CodeSeconds { get; set; } = 60;
    public string PublicBaseUrl { get; set; } = string.Empty;
    public string DefaultRoleName { get; set; } = IdentityDefaults.PortalViewerRoleName;
    public string ExternalLoginUrl { get; set; } = string.Empty;
}

public sealed class AdminSeedOptions
{
    public string UserName { get; set; } = "admin";
    public string Email { get; set; } = "admin@local";
    public string DisplayName { get; set; } = "Administrator";
    public string Password { get; set; } = "ChangeMe!Admin1";
}
