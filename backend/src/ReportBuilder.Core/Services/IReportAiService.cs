using ReportBuilder.Core.Models;

namespace ReportBuilder.Core.Services;

public sealed class ReportAiAssistRequest
{
    public string Prompt { get; set; } = string.Empty;
    public Guid? DatasetId { get; set; }
    public string? Name { get; set; }
    public string? NameArabic { get; set; }
    public string? Description { get; set; }
    public string? DescriptionArabic { get; set; }
    public ReportLayout? Layout { get; set; }
}

public sealed class ReportAiAssistResult
{
    public string Name { get; set; } = string.Empty;
    public string? NameArabic { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? DescriptionArabic { get; set; }
    public Guid DatasetId { get; set; }
    public string DatasetName { get; set; } = string.Empty;
    public ReportLayout Layout { get; set; } = new();
    public string Explanation { get; set; } = string.Empty;
}

public sealed class ReportAiStatus
{
    public bool Configured { get; set; }
    public string Model { get; set; } = string.Empty;
}

public interface IReportAiService
{
    Task<ReportAiStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<ReportAiAssistResult> AssistAsync(ReportAiAssistRequest request, CancellationToken cancellationToken = default);
}
