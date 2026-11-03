using System.ComponentModel.DataAnnotations.Schema;
using ReportBuilder.Core.Identity;
using ReportBuilder.Core.Models;

namespace ReportBuilder.Core.Entities;

public class ReportTemplate
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameArabic { get; set; }
    public string? Description { get; set; }
    public string? DescriptionArabic { get; set; }
    public Guid DatasetId { get; set; }
    public Dataset Dataset { get; set; } = null!;
    public Guid? CategoryId { get; set; }
    public ReportCategory? Category { get; set; }
    public string LayoutJson { get; set; } = "{}";
    public string? DraftJson { get; set; }
    public int PublishedVersion { get; set; }
    public ReportStatus Status { get; set; } = ReportStatus.Draft;

    [NotMapped]
    public ReportDraftSnapshot? Draft => ReportDraftJson.Parse(DraftJson);

    [NotMapped]
    public bool HasDraft => Draft is not null;
    public Guid? CreatedBy { get; set; }
    public bool IsAdminCreated { get; set; }
    public bool IsRestricted { get; set; }
    public string AccessMode { get; set; } = ReportAccessModes.Everyone;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<ReportAccess> Access { get; set; } = [];
    public ICollection<ReportRoleAccess> RoleAccess { get; set; } = [];
}
