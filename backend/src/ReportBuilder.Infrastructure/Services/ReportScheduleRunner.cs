using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Identity;
using ReportBuilder.Core.Models;
using ReportBuilder.Core.Query;
using ReportBuilder.Core.Services;
using ReportBuilder.Infrastructure.Data;
using ReportBuilder.Infrastructure.Options;

namespace ReportBuilder.Infrastructure.Services;

public sealed class ReportScheduleRunner : IReportScheduleRunner
{
    public const int MaxAttachmentBytes = 10 * 1024 * 1024;

    private readonly ReportBuilderDbContext _db;
    private readonly IReportQueryService _queries;
    private readonly IAppBrandingService _branding;
    private readonly IOptions<AuthOptions> _auth;
    private readonly IActivityLog _log;
    private readonly ICurrentUser _currentUser;

    public ReportScheduleRunner(
        ReportBuilderDbContext db,
        IReportQueryService queries,
        IAppBrandingService branding,
        IOptions<AuthOptions> auth,
        IActivityLog log,
        ICurrentUser currentUser)
    {
        _db = db;
        _queries = queries;
        _branding = branding;
        _auth = auth;
        _log = log;
        _currentUser = currentUser;
    }

    public async Task RunDueAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE s
            SET s.[Enabled] = 0, s.[NextRunAt] = NULL, s.[UpdatedAt] = {now}
            FROM [rb].[ReportSchedules] s
            INNER JOIN [rb].[ReportTemplates] t ON t.[Id] = s.[ReportId]
            WHERE s.[Enabled] = 1 AND t.[Status] <> N'Published'
            """, cancellationToken);

        var due = await _db.ReportSchedules
            .AsNoTracking()
            .Where(item => item.Enabled
                && item.NextRunAt != null
                && item.NextRunAt <= now
                && (item.LockedUntil == null || item.LockedUntil < now))
            .OrderBy(item => item.NextRunAt)
            .Select(item => item.ReportId)
            .Take(20)
            .ToListAsync(cancellationToken);

        foreach (var reportId in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await ClaimAsync(reportId, cancellationToken))
            {
                await RunOneAsync(reportId, advanceNext: true, cancellationToken);
            }
        }
    }

    public async Task SendNowAsync(Guid reportId, CancellationToken cancellationToken = default)
    {
        var schedule = await _db.ReportSchedules
            .AsNoTracking()
            .Include(item => item.Recipients)
            .FirstOrDefaultAsync(item => item.ReportId == reportId, cancellationToken);
        if (schedule is null || schedule.Recipients.Count == 0)
        {
            throw new ReportQueryException("Save the schedule and choose at least one recipient before sending.");
        }

        var published = await _db.ReportTemplates
            .AsNoTracking()
            .AnyAsync(item => item.Id == reportId && item.Status == ReportStatus.Published, cancellationToken);
        if (!published)
        {
            throw new ReportQueryException("Publish the report before sending it.");
        }

        if (!await ClaimManualAsync(reportId, cancellationToken))
        {
            throw new ReportQueryException("This report is already being sent.");
        }

        await RunOneAsync(reportId, advanceNext: false, cancellationToken);
    }

    private async Task<bool> ClaimAsync(Guid reportId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var until = now.AddMinutes(30);
        var claimed = await _db.ReportSchedules
            .Where(item => item.ReportId == reportId
                && item.Enabled
                && item.NextRunAt != null
                && item.NextRunAt <= now
                && (item.LockedUntil == null || item.LockedUntil < now))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.LockedUntil, until),
                cancellationToken);
        return claimed == 1;
    }

    private async Task<bool> ClaimManualAsync(Guid reportId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var until = now.AddMinutes(30);
        var claimed = await _db.ReportSchedules
            .Where(item => item.ReportId == reportId
                && (item.LockedUntil == null || item.LockedUntil < now))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.LockedUntil, until),
                cancellationToken);
        return claimed == 1;
    }

    private async Task RunOneAsync(Guid reportId, bool advanceNext, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var sent = 0;
        string status = "Failed";
        string? message = null;
        try
        {
            (sent, status, message) = await DeliverAsync(reportId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            status = "Failed";
            message = Trim(ex.Message);
            await TryNotifyOwnerAsync(reportId, message, cancellationToken);
        }
        finally
        {
            await FinishAsync(reportId, started, status, sent, message, advanceNext, cancellationToken);
        }
    }

    private async Task<(int Sent, string Status, string? Message)> DeliverAsync(
        Guid reportId,
        CancellationToken cancellationToken)
    {
        var schedule = await _db.ReportSchedules
            .Include(item => item.Recipients)
            .FirstAsync(item => item.ReportId == reportId, cancellationToken);
        var report = await _db.ReportTemplates
            .Include(item => item.Access)
            .Include(item => item.RoleAccess)
            .ThenInclude(item => item.Role)
            .FirstAsync(item => item.Id == reportId, cancellationToken);
        if (report.Status != ReportStatus.Published)
        {
            schedule.Enabled = false;
            return (0, "Skipped", "The report is not published.");
        }

        if (LayoutJson.KindOf(report.LayoutJson) == "chart")
        {
            schedule.Enabled = false;
            return (0, "Skipped", "Charts are not scheduled.");
        }

        var owner = await LoadUserAsync(schedule.OwnerUserId, cancellationToken);
        if (owner is null || !owner.IsActive)
        {
            return (0, "Failed", "The schedule owner is not active.");
        }

        var profile = await ResolveProfileAsync(schedule.SmtpProfileId, cancellationToken);
        if (profile is null)
        {
            return (0, "Failed", "No mail server is configured.");
        }

        var mailTemplate = await ResolveTemplateAsync(schedule.EmailTemplateId, cancellationToken);
        var branding = await _branding.GetAsync(cancellationToken);
        var logo = await _branding.LogoAsync(cancellationToken);

        var arabic = schedule.Language == "ar";
        var reportName = arabic && !string.IsNullOrWhiteSpace(report.NameArabic)
            ? report.NameArabic.Trim()
            : report.Name;
        var format = ParseFormat(schedule.Format);
        var userScoped = LayoutJson.Parse(report.LayoutJson).UserScope?.Enabled == true;
        UserScope? deliveryScope = null;
        if (!string.IsNullOrWhiteSpace(schedule.ScopeField))
        {
            deliveryScope = new UserScope
            {
                Enabled = true,
                Field = schedule.ScopeField.Trim(),
                Property = schedule.ScopeProperty ?? UserScopeProperties.UserName
            };
        }

        var ownRows = userScoped || deliveryScope is not null;
        var notes = new List<string>();
        var sent = 0;
        var users = await ResolveRecipientsAsync(schedule, report, notes, cancellationToken);

        if (ownRows)
        {
            if (schedule.Recipients.Any(item => item.UserId is null && item.RoleId is null && !string.IsNullOrWhiteSpace(item.Email)))
            {
                notes.Add("Added addresses were skipped because each person receives only their own rows.");
            }

            foreach (var user in users)
            {
                ExportFile? file = null;
                try
                {
                    file = await ExportAsync(report, reportName, format, AccountUser.From(user), schedule.Language, cancellationToken, deliveryScope);
                    await SendFileAsync(profile, mailTemplate, branding, logo, owner, user, report, reportName, format, schedule.Language, file, includeLink: true, cancellationToken);
                    sent++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    notes.Add(Trim(ex.Message) ?? "Mail was not sent.");
                }
                finally
                {
                    file?.Dispose();
                }
            }
        }
        else
        {
            var ownerActor = AccountUser.From(owner);
            var file = await ExportAsync(report, reportName, format, ownerActor, schedule.Language, cancellationToken);
            try
            {
                var skippedAdded = new List<string>();
                foreach (var user in users)
                {
                    try
                    {
                        await SendFileAsync(profile, mailTemplate, branding, logo, owner, user, report, reportName, format, schedule.Language, file, includeLink: true, cancellationToken);
                        sent++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        notes.Add(Trim(ex.Message) ?? "Mail was not sent.");
                    }
                }

                foreach (var recipient in schedule.Recipients)
                {
                    if (recipient.UserId is not null || recipient.RoleId is not null || string.IsNullOrWhiteSpace(recipient.Email))
                    {
                        continue;
                    }

                    if (file.Length > MaxAttachmentBytes)
                    {
                        skippedAdded.Add(recipient.Email);
                        continue;
                    }

                    try
                    {
                        await SendFileAsync(profile, mailTemplate, branding, logo, owner, null, report, reportName, format, schedule.Language, file, includeLink: false, cancellationToken, recipient.Email);
                        sent++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        notes.Add(Trim(ex.Message) ?? "Mail was not sent.");
                    }
                }

                if (skippedAdded.Count > 0)
                {
                    notes.Add("The file was larger than 10 MB, so added addresses were not mailed.");
                    if (!string.IsNullOrWhiteSpace(owner.Email))
                    {
                        var notice = arabic
                            ? $"حجم الملف أكبر من 10 ميغابايت، لذلك لم يُرسل إلى العناوين المضافة:\n{string.Join("\n", skippedAdded)}\nمن يمكنهم فتح التقرير استلموا رابطاً."
                            : $"The file was larger than 10 MB, so it was not mailed to added addresses:\n{string.Join("\n", skippedAdded)}\nPeople who can open the report received a link.";
                        await SmtpMailSender.SendAsync(
                            profile,
                            owner.Email,
                            reportName,
                            notice,
                            null,
                            null,
                            null,
                            cancellationToken,
                            _log,
                            MailOf(report, owner, owner, owner.Email));
                    }
                }
            }
            finally
            {
                file.Dispose();
            }
        }

        if (sent == 0)
        {
            return (0, "Skipped", Trim(string.Join(" ", notes.Distinct())) ?? "No recipient could be mailed.");
        }

        return (sent, "Sent", Trim(string.Join(" ", notes.Distinct())));
    }

    private async Task<ExportFile> ExportAsync(
        ReportTemplate report,
        string reportName,
        ReportExportFormat format,
        AccountUser actor,
        string language,
        CancellationToken cancellationToken,
        UserScope? deliveryScope = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rb-schedule-{Guid.NewGuid():N}");
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.DeleteOnClose);
        try
        {
            var truncated = await _queries.WriteExportForAsync(
                report.Id,
                reportName,
                format,
                stream,
                actor,
                language,
                cancellationToken,
                deliveryScope);
            var length = stream.Length;
            byte[]? bytes = null;
            if (length <= MaxAttachmentBytes)
            {
                stream.Position = 0;
                bytes = new byte[length];
                await stream.ReadExactlyAsync(bytes, cancellationToken);
            }

            return new ExportFile(stream, bytes, length, truncated, SafeFileName(AttachmentName(report)) + Extension(format));
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    private async Task SendFileAsync(
        SmtpProfile profile,
        EmailTemplate? mailTemplate,
        AppBrandingModel branding,
        AppLogoFile? logo,
        User owner,
        User? recipient,
        ReportTemplate report,
        string reportName,
        ReportExportFormat format,
        string language,
        ExportFile file,
        bool includeLink,
        CancellationToken cancellationToken,
        string? addedEmail = null)
    {
        var arabic = language == "ar";
        var tooLarge = file.Length > MaxAttachmentBytes;
        if (tooLarge && !includeLink)
        {
            return;
        }

        var fileNote = arabic ? "الملف مرفق." : "The report file is attached.";
        if (!tooLarge && file.Truncated && format == ReportExportFormat.Pdf)
        {
            fileNote += arabic
                ? "\nتم قص ملف PDF عند 20,000 صف."
                : "\nThe PDF was cut at 20,000 rows.";
        }
        else if (tooLarge)
        {
            fileNote = arabic
                ? "حجم الملف أكبر من 10 ميغابايت، لذلك لم يُرفق."
                : "The file is larger than 10 MB, so it is not attached.";
        }

        var reportUrl = includeLink ? ReportUrl(report.Id) : null;
        var email = recipient?.Email ?? addedEmail ?? string.Empty;
        var userName = recipient?.UserName ?? string.Empty;
        var displayName = recipient is null || string.IsNullOrWhiteSpace(recipient.DisplayName)
            ? userName
            : recipient.DisplayName;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = email;
        }
        var fallback = arabic ? $"{reportName}\n" : $"{reportName}\n";
        fallback += "\n" + fileNote;
        if (tooLarge && includeLink)
        {
            fallback += arabic
                ? $"\nافتح التقرير: {reportUrl}"
                : $"\nOpen the report: {reportUrl}";
        }

        var composed = EmailTemplateComposer.Compose(
            mailTemplate,
            reportName,
            reportUrl,
            fileNote,
            userName,
            displayName,
            email,
            EmailTemplateComposer.ReportDate(language),
            branding.ApplicationName,
            branding.ApplicationNameArabic,
            branding.OrganizationName,
            branding.OrganizationNameArabic,
            branding.HasLogo ? EmailLogoHtml.Render(branding.ApplicationName, logo?.Bytes) : string.Empty,
            fallback.Trim(),
            report.Name,
            string.IsNullOrWhiteSpace(report.NameArabic) ? string.Empty : report.NameArabic.Trim(),
            string.IsNullOrWhiteSpace(recipient?.DisplayNameArabic) ? string.Empty : recipient.DisplayNameArabic.Trim(),
            branding.StudioName,
            branding.StudioNameArabic,
            EmailTemplateComposer.ReportDate("en"),
            EmailTemplateComposer.ReportDate("ar"));

        await SmtpMailSender.SendAsync(
            profile,
            email,
            composed.Subject,
            composed.Body,
            tooLarge ? null : file.FileName,
            tooLarge ? null : file.Bytes,
            tooLarge ? null : ContentType(format),
            cancellationToken,
            _log,
            MailOf(report, owner, recipient, email),
            composed.IsHtml,
            composed.IsHtml ? logo?.Bytes : null,
            composed.IsHtml ? logo?.ContentType : null);
    }

    private async Task TryNotifyOwnerAsync(Guid reportId, string? error, CancellationToken cancellationToken)
    {
        try
        {
            var schedule = await _db.ReportSchedules.AsNoTracking()
                .FirstOrDefaultAsync(item => item.ReportId == reportId, cancellationToken);
            var report = await _db.ReportTemplates.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == reportId, cancellationToken);
            var owner = await LoadUserAsync(schedule?.OwnerUserId, cancellationToken);
            if (report is null || owner is null || string.IsNullOrWhiteSpace(owner.Email))
            {
                return;
            }

            var profile = await ResolveProfileAsync(schedule?.SmtpProfileId, cancellationToken);
            await NotifyAsync(profile, owner, report, schedule?.Language ?? "en", error ?? "The schedule failed.", cancellationToken, failure: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The run row still records the original failure.
        }
    }

    private async Task NotifyAsync(
        SmtpProfile? profile,
        User owner,
        ReportTemplate report,
        string language,
        string error,
        CancellationToken cancellationToken,
        bool failure)
    {
        if (profile is null || string.IsNullOrWhiteSpace(owner.Email))
        {
            return;
        }

        var arabic = language == "ar";
        var name = arabic && !string.IsNullOrWhiteSpace(report.NameArabic) ? report.NameArabic! : report.Name;
        var body = failure
            ? arabic
                ? $"لم يُرسل التقرير المجدول \"{name}\".\n\n{error}"
                : $"Scheduled report \"{name}\" was not sent.\n\n{error}"
            : error;
        await SmtpMailSender.SendAsync(
            profile,
            owner.Email,
            name,
            body,
            null,
            null,
            null,
            cancellationToken,
            _log,
            MailOf(report, owner, owner, owner.Email));
    }

    private MailActivity MailOf(ReportTemplate report, User owner, User? recipient, string? email)
    {
        var scheduled = _currentUser.UserId is null;
        return new MailActivity(
            scheduled ? owner.Id : null,
            scheduled ? owner.UserName : null,
            recipient?.Id,
            recipient?.UserName ?? email,
            report.Id,
            report.Name);
    }

    private async Task FinishAsync(
        Guid reportId,
        DateTimeOffset started,
        string status,
        int sent,
        string? message,
        bool advanceNext,
        CancellationToken cancellationToken)
    {
        var schedule = await _db.ReportSchedules
            .FirstOrDefaultAsync(item => item.ReportId == reportId, cancellationToken);
        if (schedule is null)
        {
            return;
        }

        var finished = DateTimeOffset.UtcNow;
        schedule.LockedUntil = null;
        schedule.LastRunAt = finished;
        if (advanceNext && schedule.Enabled)
        {
            var after = schedule.NextRunAt is DateTimeOffset due ? due.AddMinutes(1) : finished;
            if (after < finished)
            {
                after = finished;
            }

            schedule.NextRunAt = ScheduleClock.Next(
                schedule.Cadence,
                schedule.DayOfWeek,
                schedule.DayOfMonth,
                schedule.LocalTime,
                after);
        }

        _db.ReportScheduleRuns.Add(new ReportScheduleRun
        {
            Id = Guid.NewGuid(),
            ReportId = reportId,
            StartedAt = started,
            FinishedAt = finished,
            Status = status,
            RecipientCount = sent,
            Message = message
        });

        var stale = await _db.ReportScheduleRuns
            .Where(item => item.ReportId == reportId)
            .OrderByDescending(item => item.StartedAt)
            .Skip(29)
            .ToListAsync(cancellationToken);
        if (stale.Count > 0)
        {
            _db.ReportScheduleRuns.RemoveRange(stale);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<SmtpProfile?> ResolveProfileAsync(Guid? profileId, CancellationToken cancellationToken)
    {
        if (profileId is Guid id)
        {
            var selected = await _db.SmtpProfiles.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (selected is not null)
            {
                return selected;
            }
        }

        return await _db.SmtpProfiles.AsNoTracking()
            .FirstOrDefaultAsync(item => item.IsDefault, cancellationToken);
    }

    private async Task<EmailTemplate?> ResolveTemplateAsync(Guid? templateId, CancellationToken cancellationToken)
    {
        if (templateId is Guid id)
        {
            var selected = await _db.EmailTemplates.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (selected is not null)
            {
                return selected;
            }
        }

        return await _db.EmailTemplates.AsNoTracking()
            .FirstOrDefaultAsync(item => item.IsDefault, cancellationToken);
    }

    private async Task<List<User>> ResolveRecipientsAsync(
        ReportSchedule schedule,
        ReportTemplate report,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        var seen = new HashSet<Guid>();
        var result = new List<User>();

        void Take(User user)
        {
            if (!seen.Add(user.Id))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(user.Email))
            {
                notes.Add("Skipped a person with no active email.");
                return;
            }

            if (!ReportAccessRules.CanView(report, AccountUser.From(user)))
            {
                notes.Add($"Skipped {user.Email} because they can no longer open the report.");
                return;
            }

            result.Add(user);
        }

        foreach (var recipient in schedule.Recipients)
        {
            if (recipient.UserId is not Guid userId)
            {
                continue;
            }

            var user = await LoadUserAsync(userId, cancellationToken);
            if (user is null || !user.IsActive)
            {
                notes.Add("Skipped a person with no active email.");
                continue;
            }

            Take(user);
        }

        var roleIds = schedule.Recipients
            .Where(item => item.RoleId is not null)
            .Select(item => item.RoleId!.Value)
            .Distinct()
            .ToList();
        if (roleIds.Count == 0)
        {
            return result;
        }

        var members = await _db.Users
            .AsNoTracking()
            .Include(user => user.UserRoles)
            .ThenInclude(link => link.Role)
            .Where(user => user.IsActive && user.UserRoles.Any(link => roleIds.Contains(link.RoleId)))
            .ToListAsync(cancellationToken);
        foreach (var member in members)
        {
            Take(member);
        }

        return result;
    }

    private Task<User?> LoadUserAsync(Guid? userId, CancellationToken cancellationToken)
    {
        if (userId is not Guid id)
        {
            return Task.FromResult<User?>(null);
        }

        return _db.Users
            .AsNoTracking()
            .Include(user => user.UserRoles)
            .ThenInclude(link => link.Role)
            .FirstOrDefaultAsync(user => user.Id == id, cancellationToken);
    }

    private string ReportUrl(Guid reportId)
    {
        var root = (_auth.Value.Launch.PublicBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        return string.IsNullOrEmpty(root)
            ? $"/reports/{reportId}/view"
            : $"{root}/reports/{reportId}/view";
    }

    private static ReportExportFormat ParseFormat(string? value)
    {
        if (string.Equals(value, "Pdf", StringComparison.OrdinalIgnoreCase))
        {
            return ReportExportFormat.Pdf;
        }

        if (string.Equals(value, "Xlsx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Excel", StringComparison.OrdinalIgnoreCase))
        {
            return ReportExportFormat.Xlsx;
        }

        return ReportExportFormat.Csv;
    }

    private static string Extension(ReportExportFormat format)
        => format switch
        {
            ReportExportFormat.Pdf => ".pdf",
            ReportExportFormat.Xlsx => ".xlsx",
            _ => ".csv"
        };

    private static string ContentType(ReportExportFormat format)
        => format switch
        {
            ReportExportFormat.Pdf => "application/pdf",
            ReportExportFormat.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "text/csv"
        };

    private static string? Trim(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        return text.Length <= 1000 ? text : text[..1000];
    }

    private static string AttachmentName(ReportTemplate report)
    {
        var english = report.Name?.Trim() ?? "";
        var arabic = report.NameArabic?.Trim() ?? "";
        var combined = string.IsNullOrWhiteSpace(arabic) || arabic.Equals(english, StringComparison.Ordinal)
            ? english
            : $"{english} - {arabic}";
        if (combined.Length > 180)
        {
            combined = combined[..180].Trim();
        }

        return string.IsNullOrWhiteSpace(combined) ? "report" : combined;
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = string.Concat(name.Select(ch => invalid.Contains(ch) ? '-' : ch)).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "report" : cleaned;
    }

    private sealed class ExportFile : IDisposable
    {
        private readonly FileStream _stream;

    public ExportFile(FileStream stream, byte[]? bytes, long length, bool truncated, string fileName)
        {
            _stream = stream;
            Bytes = bytes;
            Length = length;
            Truncated = truncated;
            FileName = fileName;
        }

        public byte[]? Bytes { get; }
        public long Length { get; }
        public bool Truncated { get; }
        public string FileName { get; }

        public void Dispose() => _stream.Dispose();
    }
}
