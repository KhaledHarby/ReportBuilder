namespace ReportBuilder.Core.Services;

public interface IReportScheduleService
{
    Task<ReportScheduleModel> GetAsync(Guid reportId, CancellationToken cancellationToken = default);
    Task<ReportScheduleModel> SaveAsync(Guid reportId, ReportScheduleWrite request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReportDeliveryRow>> ListHealthAsync(CancellationToken cancellationToken = default);
}

public interface IReportScheduleRunner
{
    Task RunDueAsync(CancellationToken cancellationToken = default);
    Task SendNowAsync(Guid reportId, CancellationToken cancellationToken = default);
}

public sealed class ReportScheduleModel
{
    public bool Enabled { get; set; }
    public string Cadence { get; set; } = "Daily";
    public int? DayOfWeek { get; set; }
    public int? DayOfMonth { get; set; }
    public string LocalTime { get; set; } = "08:00";
    public string Format { get; set; } = "Csv";
    public string Language { get; set; } = "en";
    public Guid? SmtpProfileId { get; set; }
    public Guid? EmailTemplateId { get; set; }
    public bool UserScoped { get; set; }
    public string? ScopeField { get; set; }
    public string? ScopeProperty { get; set; }
    public bool SendOnlyIfRows { get; set; }
    public List<ReportScheduleScopeColumn> ScopeColumns { get; set; } = [];
    public bool Published { get; set; }
    public DateTimeOffset? NextRunAt { get; set; }
    public List<ReportScheduleRecipientModel> Recipients { get; set; } = [];
    public List<ReportSchedulePersonModel> People { get; set; } = [];
    public List<ReportScheduleRoleModel> Roles { get; set; } = [];
    public List<ReportScheduleRunModel> Runs { get; set; } = [];
}

public sealed class ReportScheduleWrite
{
    public bool Enabled { get; set; }
    public string Cadence { get; set; } = "Daily";
    public int? DayOfWeek { get; set; }
    public int? DayOfMonth { get; set; }
    public string LocalTime { get; set; } = "08:00";
    public string Format { get; set; } = "Csv";
    public string Language { get; set; } = "en";
    public Guid? SmtpProfileId { get; set; }
    public Guid? EmailTemplateId { get; set; }
    public List<Guid> UserIds { get; set; } = [];
    public List<Guid> RoleIds { get; set; } = [];
    public List<string> Emails { get; set; } = [];
    public string? ScopeField { get; set; }
    public string? ScopeProperty { get; set; }
    public bool SendOnlyIfRows { get; set; }
}

public sealed class ReportScheduleScopeColumn
{
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? LabelArabic { get; set; }
    public string SqlType { get; set; } = string.Empty;
}

public sealed class ReportScheduleRecipientModel
{
    public Guid? UserId { get; set; }
    public Guid? RoleId { get; set; }
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
}

public sealed class ReportScheduleRoleModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ActiveCount { get; set; }
}

public sealed class ReportSchedulePersonModel
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string? Email { get; set; }
}

public sealed class ReportScheduleRunModel
{
    public DateTimeOffset StartedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public int RecipientCount { get; set; }
    public string? Message { get; set; }
}

public sealed class ReportDeliveryRow
{
    public Guid ReportId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameArabic { get; set; }
    public bool Published { get; set; }
    public bool Enabled { get; set; }
    public string Cadence { get; set; } = "Daily";
    public int? DayOfWeek { get; set; }
    public int? DayOfMonth { get; set; }
    public string LocalTime { get; set; } = "08:00";
    public string Format { get; set; } = "Csv";
    public string Language { get; set; } = "en";
    public int RecipientCount { get; set; }
    public DateTimeOffset? NextRunAt { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
    public string? LastStatus { get; set; }
    public int? LastRecipientCount { get; set; }
    public string? LastMessage { get; set; }
    public bool Busy { get; set; }
    public bool CanRetry { get; set; }
}
