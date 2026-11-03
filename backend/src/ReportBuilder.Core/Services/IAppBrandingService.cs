namespace ReportBuilder.Core.Services;

public interface IAppBrandingService
{
    Task<AppBrandingModel> GetAsync(CancellationToken cancellationToken = default);
    Task<AppBrandingModel> UpdateAsync(AppBrandingWrite request, Guid? userId, CancellationToken cancellationToken = default);
    Task<AppLogoFile?> LogoAsync(CancellationToken cancellationToken = default);
}

public sealed class AppBrandingModel
{
    public string ApplicationName { get; set; } = "MECS";
    public string ApplicationNameArabic { get; set; } = "";
    public string StudioName { get; set; } = "Report studio";
    public string StudioNameArabic { get; set; } = "";
    public string OrganizationName { get; set; } = "";
    public string OrganizationNameArabic { get; set; } = "";
    public string DefaultLanguage { get; set; } = "en";
    public bool HasLogo { get; set; }
    public long LogoVersion { get; set; }
}

public sealed class AppBrandingWrite
{
    public string ApplicationName { get; set; } = "";
    public string? ApplicationNameArabic { get; set; }
    public string StudioName { get; set; } = "";
    public string? StudioNameArabic { get; set; }
    public string? OrganizationName { get; set; }
    public string? OrganizationNameArabic { get; set; }
    public string? DefaultLanguage { get; set; }
    public string? LogoBase64 { get; set; }
    public bool RemoveLogo { get; set; }
}

public sealed record AppLogoFile(byte[] Bytes, string ContentType);
