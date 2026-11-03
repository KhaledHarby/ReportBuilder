using ReportBuilder.Core.Models;

namespace ReportBuilder.Api.Contracts;

public sealed class ReportResultDto
{
    public List<ReportResultColumnDto> Columns { get; set; } = [];
    public List<Dictionary<string, object?>> Rows { get; set; } = [];
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }

    public static ReportResultDto From(ReportResult result) => new()
    {
        Columns = result.Columns.Select(c => new ReportResultColumnDto
        {
            Name = c.Name,
            Label = c.Label,
            DisplayName = c.DisplayName,
            DisplayNameArabic = c.DisplayNameArabic,
            Hidden = c.Hidden
        }).ToList(),
        Rows = result.Rows.Select(r => r.ToDictionary(p => p.Key, p => p.Value)).ToList(),
        TotalCount = result.TotalCount,
        Skip = result.Skip,
        Take = result.Take
    };
}

public sealed class ReportResultColumnDto
{
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? DisplayNameArabic { get; set; }
    public bool Hidden { get; set; }
}
