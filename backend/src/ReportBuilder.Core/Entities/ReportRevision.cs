namespace ReportBuilder.Core.Entities;

public class ReportRevision
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public int Version { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameArabic { get; set; }
    public string? Description { get; set; }
    public string? DescriptionArabic { get; set; }
    public Guid DatasetId { get; set; }
    public Guid? CategoryId { get; set; }
    public string LayoutJson { get; set; } = "{}";
    public bool IsRestricted { get; set; }
    public string AccessMode { get; set; } = "Everyone";
    public string AccessJson { get; set; } = "[]";
    public string AccessRoleJson { get; set; } = "[]";
    public Guid? PublishedBy { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
}
