namespace ReportBuilder.Core.Entities;

public class ReportSchedule
{
    public Guid ReportId { get; set; }
    public Guid? SmtpProfileId { get; set; }
    public Guid? EmailTemplateId { get; set; }
    public bool Enabled { get; set; }
    public string Cadence { get; set; } = "Daily";
    public int? DayOfWeek { get; set; }
    public int? DayOfMonth { get; set; }
    public string LocalTime { get; set; } = "08:00";
    public string Format { get; set; } = "Csv";
    public string Language { get; set; } = "en";
    /// <summary>Dataset column matched to each recipient when the schedule sends their own rows.</summary>
    public string? ScopeField { get; set; }
    /// <summary>id, userName, or email.</summary>
    public string? ScopeProperty { get; set; }
    /// <summary>When set, mail is sent only if the export contains at least one data row.</summary>
    public bool SendOnlyIfRows { get; set; }
    public Guid? OwnerUserId { get; set; }
    public DateTimeOffset? NextRunAt { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<ReportScheduleRecipient> Recipients { get; set; } = [];
    public ICollection<ReportScheduleRun> Runs { get; set; } = [];
}
