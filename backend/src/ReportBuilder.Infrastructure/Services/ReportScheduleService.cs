using System.Globalization;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Identity;
using ReportBuilder.Core.Models;
using ReportBuilder.Core.Query;
using ReportBuilder.Core.Services;
using ReportBuilder.Infrastructure.Data;

namespace ReportBuilder.Infrastructure.Services;

public sealed class ReportScheduleService : IReportScheduleService
{
    private readonly ReportBuilderDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IActivityLog _log;

    public ReportScheduleService(ReportBuilderDbContext db, ICurrentUser currentUser, IActivityLog log)
    {
        _db = db;
        _currentUser = currentUser;
        _log = log;
    }

    public async Task<ReportScheduleModel> GetAsync(Guid reportId, CancellationToken cancellationToken = default)
    {
        var report = await LoadReportAsync(reportId, cancellationToken);
        var schedule = await _db.ReportSchedules
            .AsNoTracking()
            .Include(item => item.Recipients)
            .FirstOrDefaultAsync(item => item.ReportId == reportId, cancellationToken);
        var runs = await _db.ReportScheduleRuns
            .AsNoTracking()
            .Where(item => item.ReportId == reportId)
            .OrderByDescending(item => item.StartedAt)
            .Take(8)
            .ToListAsync(cancellationToken);
        var people = await EligiblePeopleAsync(report, cancellationToken);
        var roles = await ListRolesAsync(people, cancellationToken);
        var scopeColumns = await ScopeColumnsAsync(report.DatasetId, cancellationToken);
        return ToModel(report, schedule, runs, people, roles, scopeColumns);
    }

    public async Task<ReportScheduleModel> SaveAsync(
        Guid reportId,
        ReportScheduleWrite request,
        CancellationToken cancellationToken = default)
    {
        var report = await LoadReportAsync(reportId, cancellationToken);
        if (string.Equals(LayoutJson.KindOf(report.LayoutJson), "chart", StringComparison.OrdinalIgnoreCase))
        {
            throw new ReportQueryException("Charts are not scheduled.");
        }

        var cadence = NormalizeCadence(request.Cadence);
        var format = NormalizeFormat(request.Format);
        var language = NormalizeLanguage(request.Language);
        var localTime = NormalizeTime(request.LocalTime);
        var dayOfWeek = cadence == "Weekly" ? Math.Clamp(request.DayOfWeek ?? 1, 0, 6) : (int?)null;
        var dayOfMonth = cadence == "Monthly" ? Math.Clamp(request.DayOfMonth ?? 1, 1, 31) : (int?)null;
        var scope = await NormalizeScopeAsync(report, request.ScopeField, request.ScopeProperty, cancellationToken);
        var userScoped = IsUserScoped(report) || scope.Field is not null;
        var emails = NormalizeEmails(request.Emails, userScoped);
        var people = await EligiblePeopleAsync(report, cancellationToken);
        var userIds = request.UserIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        if (userIds.Any(id => people.All(person => person.Id != id)))
        {
            throw new ReportQueryException("A selected person cannot open this report.");
        }

        var roleIds = request.RoleIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        if (roleIds.Count > 0)
        {
            var found = await _db.Roles.CountAsync(role => roleIds.Contains(role.Id), cancellationToken);
            if (found != roleIds.Count)
            {
                throw new ReportQueryException("A selected role was not found.");
            }
        }

        if (request.Enabled && userIds.Count == 0 && emails.Count == 0 && roleIds.Count == 0)
        {
            throw new ReportQueryException("Choose at least one recipient.");
        }

        if (request.SmtpProfileId is Guid profileId
            && !await _db.SmtpProfiles.AnyAsync(item => item.Id == profileId, cancellationToken))
        {
            throw new ReportQueryException("SMTP setting was not found.");
        }

        if (request.EmailTemplateId is Guid templateId
            && !await _db.EmailTemplates.AnyAsync(item => item.Id == templateId, cancellationToken))
        {
            throw new ReportQueryException("Email template was not found.");
        }

        var schedule = await _db.ReportSchedules
            .Include(item => item.Recipients)
            .FirstOrDefaultAsync(item => item.ReportId == reportId, cancellationToken);
        if (schedule is null)
        {
            schedule = new ReportSchedule { ReportId = reportId };
            _db.ReportSchedules.Add(schedule);
        }

        schedule.Enabled = request.Enabled;
        schedule.Cadence = cadence;
        schedule.DayOfWeek = dayOfWeek;
        schedule.DayOfMonth = dayOfMonth;
        schedule.LocalTime = localTime;
        schedule.Format = format;
        schedule.Language = language;
        schedule.ScopeField = scope.Field;
        schedule.ScopeProperty = scope.Property;
        schedule.SmtpProfileId = request.SmtpProfileId;
        schedule.EmailTemplateId = request.EmailTemplateId;
        schedule.OwnerUserId = _currentUser.UserId;
        schedule.UpdatedAt = DateTimeOffset.UtcNow;
        schedule.NextRunAt = request.Enabled
            ? ScheduleClock.Next(cadence, dayOfWeek, dayOfMonth, localTime, DateTimeOffset.UtcNow)
            : null;

        _db.ReportScheduleRecipients.RemoveRange(schedule.Recipients);
        foreach (var userId in userIds)
        {
            schedule.Recipients.Add(new ReportScheduleRecipient
            {
                Id = Guid.NewGuid(),
                ReportId = reportId,
                UserId = userId
            });
        }

        foreach (var roleId in roleIds)
        {
            schedule.Recipients.Add(new ReportScheduleRecipient
            {
                Id = Guid.NewGuid(),
                ReportId = reportId,
                RoleId = roleId
            });
        }

        foreach (var email in emails)
        {
            schedule.Recipients.Add(new ReportScheduleRecipient
            {
                Id = Guid.NewGuid(),
                ReportId = reportId,
                Email = email
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Reports,
                Action = "report.schedule",
                Message = request.Enabled
                    ? $"Scheduled report {report.Name}."
                    : $"Saved the schedule for {report.Name} without sending.",
                EntityType = "Report",
                EntityId = reportId,
                EntityName = report.Name
            },
            cancellationToken);

        return await GetAsync(reportId, cancellationToken);
    }

    public async Task<IReadOnlyList<ReportDeliveryRow>> ListHealthAsync(CancellationToken cancellationToken = default)
    {
        var schedules = await _db.ReportSchedules
            .AsNoTracking()
            .Select(item => new
            {
                item.ReportId,
                item.Enabled,
                item.Cadence,
                item.DayOfWeek,
                item.DayOfMonth,
                item.LocalTime,
                item.Format,
                item.Language,
                item.NextRunAt,
                item.LockedUntil
            })
            .ToListAsync(cancellationToken);
        if (schedules.Count == 0)
        {
            return [];
        }

        var ids = schedules.Select(item => item.ReportId).ToList();
        var reports = await _db.ReportTemplates
            .AsNoTracking()
            .Where(item => ids.Contains(item.Id))
            .Select(item => new
            {
                item.Id,
                item.Name,
                item.NameArabic,
                item.Status,
                item.CreatedBy,
                item.IsAdminCreated
            })
            .ToListAsync(cancellationToken);
        var reportById = reports.ToDictionary(item => item.Id);
        var recipientCounts = await ActiveRecipientCountsAsync(ids, cancellationToken);
        var runs = await _db.ReportScheduleRuns
            .AsNoTracking()
            .Where(item => ids.Contains(item.ReportId))
            .OrderByDescending(item => item.StartedAt)
            .Select(item => new
            {
                item.ReportId,
                item.StartedAt,
                item.Status,
                item.RecipientCount,
                item.Message
            })
            .ToListAsync(cancellationToken);
        var lastByReport = new Dictionary<Guid, (DateTimeOffset StartedAt, string? Status, int RecipientCount, string? Message)>();
        foreach (var run in runs)
        {
            lastByReport.TryAdd(run.ReportId, (run.StartedAt, run.Status, run.RecipientCount, run.Message));
        }

        var now = DateTimeOffset.UtcNow;
        var rows = new List<ReportDeliveryRow>();
        foreach (var schedule in schedules)
        {
            if (!reportById.TryGetValue(schedule.ReportId, out var report))
            {
                continue;
            }

            if (!ReportAccessRules.CanDesign(
                    new ReportTemplate
                    {
                        CreatedBy = report.CreatedBy,
                        IsAdminCreated = report.IsAdminCreated
                    },
                    _currentUser))
            {
                continue;
            }

            lastByReport.TryGetValue(schedule.ReportId, out var last);
            var published = report.Status == ReportStatus.Published;
            var busy = schedule.LockedUntil is DateTimeOffset locked && locked > now;
            var recipientCount = recipientCounts.TryGetValue(schedule.ReportId, out var count) ? count : 0;
            var hasRun = last.StartedAt != default;
            var failed = hasRun && string.Equals(last.Status, "Failed", StringComparison.OrdinalIgnoreCase);
            rows.Add(new ReportDeliveryRow
            {
                ReportId = schedule.ReportId,
                Name = report.Name,
                NameArabic = report.NameArabic,
                Published = published,
                Enabled = schedule.Enabled,
                Cadence = schedule.Cadence,
                DayOfWeek = schedule.DayOfWeek,
                DayOfMonth = schedule.DayOfMonth,
                LocalTime = schedule.LocalTime,
                Format = schedule.Format,
                Language = schedule.Language,
                RecipientCount = recipientCount,
                NextRunAt = schedule.NextRunAt,
                LastRunAt = hasRun ? last.StartedAt : null,
                LastStatus = hasRun ? last.Status : null,
                LastRecipientCount = hasRun ? last.RecipientCount : null,
                LastMessage = hasRun ? last.Message : null,
                Busy = busy,
                CanRetry = failed
                    && published
                    && recipientCount > 0
                    && !busy
                    && _currentUser.Has(PermissionCodes.ReportsSchedule)
            });
        }

        return rows
            .OrderByDescending(row => string.Equals(row.LastStatus, "Failed", StringComparison.OrdinalIgnoreCase))
            .ThenBy(row => row.NextRunAt ?? DateTimeOffset.MaxValue)
            .ThenBy(row => row.Name)
            .ToList();
    }

    private async Task<ReportTemplate> LoadReportAsync(Guid reportId, CancellationToken cancellationToken)
    {
        var report = await _db.ReportTemplates
            .Include(item => item.Access)
            .Include(item => item.RoleAccess)
            .ThenInclude(item => item.Role)
            .FirstOrDefaultAsync(item => item.Id == reportId, cancellationToken);
        if (report is null)
        {
            throw new ReportQueryException("Report was not found.");
        }

        if (!ReportAccessRules.CanDesign(report, _currentUser))
        {
            throw new ReportQueryException("You cannot schedule this report.");
        }

        return report;
    }

    private async Task<List<ReportSchedulePersonModel>> EligiblePeopleAsync(
        ReportTemplate report,
        CancellationToken cancellationToken)
    {
        var users = await _db.Users
            .AsNoTracking()
            .Include(user => user.UserRoles)
            .ThenInclude(link => link.Role)
            .Where(user => user.IsActive)
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.UserName)
            .ToListAsync(cancellationToken);

        return users
            .Where(user => ReportAccessRules.CanSee(report, AccountUser.From(user)))
            .Select(user => new ReportSchedulePersonModel
            {
                Id = user.Id,
                DisplayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName : user.DisplayName,
                UserName = user.UserName,
                Email = string.IsNullOrWhiteSpace(user.Email) ? null : user.Email.Trim()
            })
            .ToList();
    }

    private async Task<List<ReportScheduleRoleModel>> ListRolesAsync(
        IReadOnlyList<ReportSchedulePersonModel> people,
        CancellationToken cancellationToken)
    {
        var eligible = people
            .Where(person => !string.IsNullOrWhiteSpace(person.Email))
            .Select(person => person.Id)
            .ToHashSet();
        var roles = await _db.Roles.AsNoTracking()
            .OrderBy(role => role.Name)
            .Select(role => new { role.Id, role.Name })
            .ToListAsync(cancellationToken);
        var links = await _db.UserRoles.AsNoTracking()
            .Where(link => link.User.IsActive)
            .Select(link => new { link.RoleId, link.UserId })
            .ToListAsync(cancellationToken);
        var counts = links
            .Where(link => eligible.Contains(link.UserId))
            .GroupBy(link => link.RoleId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.UserId).Distinct().Count());
        return roles
            .Select(role => new ReportScheduleRoleModel
            {
                Id = role.Id,
                Name = role.Name,
                ActiveCount = counts.TryGetValue(role.Id, out var count) ? count : 0
            })
            .ToList();
    }

    private async Task<Dictionary<Guid, int>> ActiveRecipientCountsAsync(
        IReadOnlyList<Guid> reportIds,
        CancellationToken cancellationToken)
    {
        var rows = await _db.ReportScheduleRecipients.AsNoTracking()
            .Where(item => reportIds.Contains(item.ReportId))
            .Select(item => new { item.ReportId, item.UserId, item.RoleId, item.Email })
            .ToListAsync(cancellationToken);
        var roleIds = rows.Where(item => item.RoleId != null).Select(item => item.RoleId!.Value).Distinct().ToList();
        var members = roleIds.Count == 0
            ? []
            : await _db.UserRoles.AsNoTracking()
                .Where(link => roleIds.Contains(link.RoleId) && link.User.IsActive)
                .Select(link => new { link.RoleId, link.UserId })
                .ToListAsync(cancellationToken);
        var directIds = rows.Where(item => item.UserId != null).Select(item => item.UserId!.Value).Distinct().ToList();
        var activeDirect = directIds.Count == 0
            ? new HashSet<Guid>()
            : (await _db.Users.AsNoTracking()
                .Where(user => directIds.Contains(user.Id) && user.IsActive)
                .Select(user => user.Id)
                .ToListAsync(cancellationToken)).ToHashSet();
        var membersByRole = members
            .GroupBy(item => item.RoleId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.UserId).Distinct().ToList());
        var counts = new Dictionary<Guid, int>();
        foreach (var group in rows.GroupBy(item => item.ReportId))
        {
            var users = new HashSet<Guid>();
            var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in group)
            {
                if (row.UserId is Guid userId && activeDirect.Contains(userId))
                {
                    users.Add(userId);
                }

                if (row.RoleId is Guid roleId && membersByRole.TryGetValue(roleId, out var roleUsers))
                {
                    foreach (var memberId in roleUsers)
                    {
                        users.Add(memberId);
                    }
                }

                if (!string.IsNullOrWhiteSpace(row.Email))
                {
                    emails.Add(row.Email.Trim());
                }
            }

            counts[group.Key] = users.Count + emails.Count;
        }

        return counts;
    }

    private static ReportScheduleModel ToModel(
        ReportTemplate report,
        ReportSchedule? schedule,
        IReadOnlyList<ReportScheduleRun> runs,
        IReadOnlyList<ReportSchedulePersonModel> people,
        IReadOnlyList<ReportScheduleRoleModel> roles,
        IReadOnlyList<ReportScheduleScopeColumn> scopeColumns)
    {
        var names = people.ToDictionary(person => person.Id);
        var roleNames = roles.ToDictionary(role => role.Id, role => role.Name);
        return new ReportScheduleModel
        {
            Enabled = schedule?.Enabled ?? false,
            Cadence = schedule?.Cadence ?? "Daily",
            DayOfWeek = schedule?.DayOfWeek,
            DayOfMonth = schedule?.DayOfMonth,
            LocalTime = schedule?.LocalTime ?? "08:00",
            Format = schedule?.Format ?? "Csv",
            Language = schedule?.Language ?? "en",
            SmtpProfileId = schedule?.SmtpProfileId,
            EmailTemplateId = schedule?.EmailTemplateId,
            UserScoped = IsUserScoped(report),
            ScopeField = schedule?.ScopeField,
            ScopeProperty = schedule?.ScopeProperty,
            ScopeColumns = scopeColumns.ToList(),
            Published = report.Status == ReportStatus.Published,
            NextRunAt = schedule?.NextRunAt,
            Recipients = schedule?.Recipients
                .Select(item => new ReportScheduleRecipientModel
                {
                    UserId = item.UserId,
                    RoleId = item.RoleId,
                    Email = item.Email,
                    DisplayName = item.UserId is Guid userId && names.TryGetValue(userId, out var person)
                        ? person.DisplayName
                        : item.RoleId is Guid roleId && roleNames.TryGetValue(roleId, out var roleName)
                            ? roleName
                            : null
                })
                .ToList() ?? [],
            People = people.ToList(),
            Roles = roles.ToList(),
            Runs = runs.Select(item => new ReportScheduleRunModel
            {
                StartedAt = item.StartedAt,
                Status = item.Status,
                RecipientCount = item.RecipientCount,
                Message = item.Message
            }).ToList()
        };
    }

    private async Task<(string? Field, string? Property)> NormalizeScopeAsync(
        ReportTemplate report,
        string? field,
        string? property,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            return (null, null);
        }

        var columns = await _db.DatasetColumns
            .AsNoTracking()
            .Where(item => item.DatasetId == report.DatasetId && !item.IsHidden && item.IsFilterable)
            .ToListAsync(cancellationToken);
        var column = columns.FirstOrDefault(item =>
            item.ColumnName.Equals(field.Trim(), StringComparison.OrdinalIgnoreCase));
        if (column is null)
        {
            throw new ReportQueryException("Choose a filterable column for each person's rows.");
        }

        var chosen = (property ?? string.Empty).Trim();
        if (chosen.Length == 0
            || !ScheduleScopeProperties.Contains(chosen)
            || !UserScopeProperties.IsCompatible(chosen, column.SqlType))
        {
            throw new ReportQueryException("Match the column to user id, username, or email.");
        }

        return (column.ColumnName, UserScopeProperties.Normalize(chosen));
    }

    private async Task<List<ReportScheduleScopeColumn>> ScopeColumnsAsync(
        Guid datasetId,
        CancellationToken cancellationToken)
    {
        var columns = await _db.DatasetColumns
            .AsNoTracking()
            .Where(item => item.DatasetId == datasetId && !item.IsHidden && item.IsFilterable)
            .OrderBy(item => item.Ordinal)
            .ToListAsync(cancellationToken);
        return columns
            .Where(item => ScheduleScopeProperties.Any(property => UserScopeProperties.IsCompatible(property, item.SqlType)))
            .Select(item => new ReportScheduleScopeColumn
            {
                Name = item.ColumnName,
                Label = item.DisplayName,
                LabelArabic = item.DisplayNameArabic,
                SqlType = item.SqlType
            })
            .ToList();
    }

    private static readonly HashSet<string> ScheduleScopeProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        UserScopeProperties.Id,
        UserScopeProperties.UserName,
        UserScopeProperties.Email
    };

    private static bool IsUserScoped(ReportTemplate report)
    {
        try
        {
            return LayoutJson.Parse(report.LayoutJson).UserScope?.Enabled == true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string NormalizeCadence(string? value)
    {
        if (string.Equals(value, "Weekly", StringComparison.OrdinalIgnoreCase))
        {
            return "Weekly";
        }

        if (string.Equals(value, "Monthly", StringComparison.OrdinalIgnoreCase))
        {
            return "Monthly";
        }

        if (string.Equals(value, "Daily", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(value))
        {
            return "Daily";
        }

        throw new ReportQueryException("Choose a daily, weekly, or monthly schedule.");
    }

    private static string NormalizeFormat(string? value)
    {
        if (string.Equals(value, "Pdf", StringComparison.OrdinalIgnoreCase))
        {
            return "Pdf";
        }

        if (string.Equals(value, "Xlsx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Excel", StringComparison.OrdinalIgnoreCase))
        {
            return "Xlsx";
        }

        if (string.Equals(value, "Csv", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(value))
        {
            return "Csv";
        }

        throw new ReportQueryException("Choose CSV, Excel, or PDF.");
    }

    private static string NormalizeLanguage(string? value)
    {
        if (string.Equals(value, "ar", StringComparison.OrdinalIgnoreCase))
        {
            return "ar";
        }

        if (string.Equals(value, "en", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(value))
        {
            return "en";
        }

        throw new ReportQueryException("Choose English or Arabic.");
    }

    private static string NormalizeTime(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (!TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new ReportQueryException("Choose a time as HH:mm.");
        }

        return text;
    }

    private static List<string> NormalizeEmails(IEnumerable<string>? emails, bool userScoped)
    {
        var list = new List<string>();
        foreach (var raw in emails ?? [])
        {
            var text = raw.Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (userScoped)
            {
                throw new ReportQueryException("A user-scoped report cannot add email addresses.");
            }

            try
            {
                var parsed = new MailAddress(text).Address.Trim();
                if (list.Any(item => item.Equals(parsed, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                list.Add(parsed);
            }
            catch (FormatException)
            {
                throw new ReportQueryException($"'{text}' is not a valid email address.");
            }
        }

        return list;
    }
}
