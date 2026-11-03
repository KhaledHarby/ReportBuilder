using System.Text.Json;
using ReportBuilder.Core.Query;

namespace ReportBuilder.Core.Models;

public sealed class ReportDraftSnapshot
{
    public int Version { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameArabic { get; set; }
    public string? Description { get; set; }
    public string? DescriptionArabic { get; set; }
    public Guid DatasetId { get; set; }
    public Guid? CategoryId { get; set; }
    public bool IsRestricted { get; set; }
    public string AccessMode { get; set; } = "";
    public ReportLayout Layout { get; set; } = new();
    public List<Guid> AccessUserIds { get; set; } = [];
    public List<Guid> AccessRoleIds { get; set; } = [];
}

public static class ReportDraftJson
{
    public static ReportDraftSnapshot? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<ReportDraftSnapshot>(json, LayoutJson.Options);
    }

    public static string Serialize(ReportDraftSnapshot draft)
        => JsonSerializer.Serialize(draft, LayoutJson.Options);
}
