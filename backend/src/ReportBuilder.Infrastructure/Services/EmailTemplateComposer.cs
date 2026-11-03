using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using ReportBuilder.Core.Entities;

namespace ReportBuilder.Infrastructure.Services;

internal static class EmailTemplateComposer
{
    public static (string Subject, string Body, bool IsHtml) Compose(
        EmailTemplate? template,
        string reportName,
        string? reportUrl,
        string fileNote,
        string userName,
        string displayName,
        string email,
        string reportDate,
        string applicationName,
        string applicationNameArabic,
        string organizationName,
        string organizationNameArabic,
        string logoHtml,
        string fallbackBody,
        string? reportNameEnglish = null,
        string? reportNameArabic = null,
        string? displayNameArabic = null,
        string? studioName = null,
        string? studioNameArabic = null,
        string? reportDateEnglish = null,
        string? reportDateArabic = null)
    {
        if (template is null)
        {
            return (reportName, fallbackBody, false);
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["reportName"] = reportName,
            ["reportNameEnglish"] = string.IsNullOrWhiteSpace(reportNameEnglish) ? reportName : reportNameEnglish,
            ["reportNameArabic"] = reportNameArabic ?? string.Empty,
            ["reportUrl"] = reportUrl ?? string.Empty,
            ["fileNote"] = fileNote,
            ["userName"] = userName,
            ["displayName"] = displayName,
            ["displayNameArabic"] = displayNameArabic ?? string.Empty,
            ["email"] = email,
            ["reportDate"] = reportDate,
            ["reportDateEnglish"] = string.IsNullOrWhiteSpace(reportDateEnglish) ? reportDate : reportDateEnglish,
            ["reportDateArabic"] = reportDateArabic ?? string.Empty,
            ["applicationName"] = applicationName,
            ["applicationNameArabic"] = applicationNameArabic,
            ["studioName"] = studioName ?? string.Empty,
            ["studioNameArabic"] = studioNameArabic ?? string.Empty,
            ["organizationName"] = organizationName,
            ["organizationNameArabic"] = organizationNameArabic,
            ["logo"] = logoHtml
        };
        var subject = Apply(template.Subject, html: false, values);
        var body = Apply(template.Body, template.IsHtml, values);
        if (!HasToken(template.Body, "fileNote") && !string.IsNullOrWhiteSpace(fileNote))
        {
            body += template.IsHtml
                ? "<p>" + WebUtility.HtmlEncode(fileNote) + "</p>"
                : "\n\n" + fileNote;
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            subject = reportName;
        }

        return (subject, body, template.IsHtml);
    }

    public static (string Subject, string Body, bool IsHtml) Compose(
        EmailTemplate? template,
        IReadOnlyDictionary<string, string> values,
        string fallbackSubject,
        string fallbackBody)
    {
        if (template is null)
        {
            return (fallbackSubject, fallbackBody, false);
        }

        var subject = Apply(template.Subject, html: false, values);
        if (string.IsNullOrWhiteSpace(subject))
        {
            subject = fallbackSubject;
        }

        return (subject, Apply(template.Body, template.IsHtml, values), template.IsHtml);
    }

    public static string ReportDate(string? language)
    {
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById("Asia/Dubai", out var dubai)
            ? dubai
            : TimeZoneInfo.FindSystemTimeZoneById("Arabian Standard Time");
        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
        var culture = string.Equals(language, "ar", StringComparison.OrdinalIgnoreCase)
            ? CultureInfo.GetCultureInfo("ar-AE")
            : CultureInfo.GetCultureInfo("en-GB");
        return local.ToString("d MMM yyyy HH:mm", culture);
    }

    private static readonly HashSet<string> RawHtmlTokens = new(StringComparer.OrdinalIgnoreCase) { "logo" };

    private static string Apply(string source, bool html, IReadOnlyDictionary<string, string> values)
    {
        var text = source;
        foreach (var pair in values)
        {
            var value = html && !RawHtmlTokens.Contains(pair.Key)
                ? WebUtility.HtmlEncode(pair.Value).Replace("\r\n", "<br>").Replace("\n", "<br>")
                : html
                    ? pair.Value
                    : RawHtmlTokens.Contains(pair.Key) ? string.Empty : pair.Value;
            text = Replace(text, pair.Key, value);
        }

        return text;
    }

    private static string Replace(string source, string token, string value)
        => Regex.Replace(source, @"\{" + token + @"\}", value.Replace("$", "$$"), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool HasToken(string source, string token)
        => Regex.IsMatch(source, @"\{" + token + @"\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
