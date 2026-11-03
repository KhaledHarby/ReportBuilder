namespace ReportBuilder.Core.Entities;

public class AppSettings
{
    public const int DefaultId = 1;
    public const int DefaultRetentionDays = 30;
    public const int MinRetentionDays = 7;
    public const int MaxRetentionDays = 365;

    public int Id { get; set; } = DefaultId;
    public int LogRetentionDays { get; set; } = DefaultRetentionDays;
    public string ApplicationName { get; set; } = "MECS";
    public string ApplicationNameArabic { get; set; } = "MECS";
    public string StudioName { get; set; } = "Report studio";
    public string StudioNameArabic { get; set; } = "استوديو التقارير";
    public string OrganizationName { get; set; } = "";
    public string OrganizationNameArabic { get; set; } = "";
    public string DefaultLanguage { get; set; } = "en";
    public string? LogoContentType { get; set; }
    public byte[]? Logo { get; set; }
    public Guid? ModifiedBy { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }

    public static int ClampRetention(int days) =>
        Math.Clamp(days, MinRetentionDays, MaxRetentionDays);
}
