using Microsoft.EntityFrameworkCore;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Query;
using ReportBuilder.Core.Services;
using ReportBuilder.Infrastructure.Data;

namespace ReportBuilder.Infrastructure.Services;

public sealed class AppBrandingService : IAppBrandingService
{
    private const int MaxLogoBytes = 512 * 1024;
    private readonly ReportBuilderDbContext _db;

    public AppBrandingService(ReportBuilderDbContext db)
    {
        _db = db;
    }

    public async Task<AppBrandingModel> GetAsync(CancellationToken cancellationToken = default)
    {
        var row = await EnsureAsync(cancellationToken);
        return ToModel(row);
    }

    public async Task<AppBrandingModel> UpdateAsync(
        AppBrandingWrite request,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        var applicationName = Required(request.ApplicationName, "Application name");
        var studioName = Required(request.StudioName, "Studio label");
        var row = await EnsureAsync(cancellationToken);
        row.ApplicationName = applicationName;
        row.ApplicationNameArabic = Trimmed(request.ApplicationNameArabic, 80);
        row.StudioName = studioName;
        row.StudioNameArabic = Trimmed(request.StudioNameArabic, 80);
        row.OrganizationName = Trimmed(request.OrganizationName, 160);
        row.OrganizationNameArabic = Trimmed(request.OrganizationNameArabic, 160);
        row.DefaultLanguage = Language(request.DefaultLanguage);
        if (request.RemoveLogo)
        {
            row.Logo = null;
            row.LogoContentType = null;
        }
        else if (!string.IsNullOrWhiteSpace(request.LogoBase64))
        {
            var (bytes, contentType) = DecodeLogo(request.LogoBase64);
            row.Logo = bytes;
            row.LogoContentType = contentType;
        }

        row.ModifiedBy = userId;
        row.ModifiedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return ToModel(row);
    }

    public async Task<AppLogoFile?> LogoAsync(CancellationToken cancellationToken = default)
    {
        var row = await _db.AppSettings.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == AppSettings.DefaultId, cancellationToken);
        if (row?.Logo is not { Length: > 0 } || string.IsNullOrWhiteSpace(row.LogoContentType))
        {
            return null;
        }

        return new AppLogoFile(row.Logo, row.LogoContentType);
    }

    private async Task<AppSettings> EnsureAsync(CancellationToken cancellationToken)
    {
        var row = await _db.AppSettings.FirstOrDefaultAsync(item => item.Id == AppSettings.DefaultId, cancellationToken);
        if (row is not null)
        {
            return row;
        }

        row = new AppSettings
        {
            Id = AppSettings.DefaultId,
            ModifiedAt = DateTimeOffset.UtcNow
        };
        _db.AppSettings.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
        return row;
    }

    private static AppBrandingModel ToModel(AppSettings row) => new()
    {
        ApplicationName = string.IsNullOrWhiteSpace(row.ApplicationName) ? "MECS" : row.ApplicationName,
        ApplicationNameArabic = string.IsNullOrWhiteSpace(row.ApplicationNameArabic)
            ? (string.IsNullOrWhiteSpace(row.ApplicationName) ? "MECS" : row.ApplicationName)
            : row.ApplicationNameArabic,
        StudioName = string.IsNullOrWhiteSpace(row.StudioName) ? "Report studio" : row.StudioName,
        StudioNameArabic = string.IsNullOrWhiteSpace(row.StudioNameArabic) ? "استوديو التقارير" : row.StudioNameArabic,
        OrganizationName = row.OrganizationName ?? "",
        OrganizationNameArabic = row.OrganizationNameArabic ?? "",
        DefaultLanguage = Language(row.DefaultLanguage),
        HasLogo = row.Logo is { Length: > 0 },
        LogoVersion = row.ModifiedAt.UtcTicks
    };

    private static string Language(string? value)
    {
        var text = (value ?? "").Trim().ToLowerInvariant();
        if (text is "en" or "ar")
        {
            return text;
        }

        if (string.IsNullOrEmpty(text))
        {
            return "en";
        }

        throw new ReportQueryException("Default language must be English or Arabic.");
    }

    private static string Required(string? value, string label)
    {
        var text = Trimmed(value, 80);
        if (text.Length == 0)
        {
            throw new ReportQueryException($"{label} is required.");
        }

        return text;
    }

    private static string Trimmed(string? value, int max)
    {
        var text = (value ?? "").Trim();
        if (text.Length > max)
        {
            throw new ReportQueryException($"Keep this name to {max} characters.");
        }

        return text;
    }

    private static (byte[] Bytes, string ContentType) DecodeLogo(string raw)
    {
        var payload = raw.Trim();
        var marker = payload.IndexOf(',');
        if (payload.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && marker >= 0)
        {
            payload = payload[(marker + 1)..];
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(payload);
        }
        catch (FormatException)
        {
            throw new ReportQueryException("The logo file could not be read.");
        }

        if (bytes.Length == 0 || bytes.Length > MaxLogoBytes)
        {
            throw new ReportQueryException("The logo must be an image of 512 KB or smaller.");
        }

        var contentType = Sniff(bytes) ?? throw new ReportQueryException("Use a PNG, JPEG, WEBP, or GIF logo.");
        return (bytes, contentType);
    }

    private static string? Sniff(byte[] bytes)
    {
        if (bytes.Length >= 8
            && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return "image/png";
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (bytes.Length >= 6
            && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38)
        {
            return "image/gif";
        }

        if (bytes.Length >= 12
            && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
            && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
        {
            return "image/webp";
        }

        return null;
    }
}
