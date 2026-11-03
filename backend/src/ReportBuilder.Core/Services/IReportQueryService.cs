using ReportBuilder.Core.Identity;
using ReportBuilder.Core.Models;

namespace ReportBuilder.Core.Services;

public readonly record struct ReportExportOutcome(bool Truncated, int RowCount);

public interface IReportQueryService
{
    Task<ReportResult> PreviewAsync(Guid datasetId, ReportLayout layout, int skip, int take, string? lang = null, CancellationToken cancellationToken = default);
    Task<ReportResult> RunAsync(Guid templateId, int skip, int take, ReportLayout? layout = null, string? lang = null, CancellationToken cancellationToken = default);
    Task<ReportResult> QueryDetailAsync(Guid templateId, string parentValue, int skip, int take, ReportLayout? layoutOverride, bool allowLayoutOverride, string? lang = null, CancellationToken cancellationToken = default);
    Task WriteExportAsync(Guid templateId, string reportName, ReportExportFormat format, Stream output, string? lang = null, CancellationToken cancellationToken = default);
    Task<ReportExportOutcome> WriteExportForAsync(Guid templateId, string reportName, ReportExportFormat format, Stream output, ICurrentUser actor, string? lang = null, CancellationToken cancellationToken = default, UserScope? deliveryScope = null);
    Task WritePreviewExportAsync(Guid datasetId, ReportLayout layout, string reportName, ReportExportFormat format, Stream output, string? lang = null, string? reportNameArabic = null, CancellationToken cancellationToken = default);
    Task<ColumnValuesResult> ListColumnValuesAsync(
        Guid datasetId,
        string field,
        int take,
        IReadOnlyList<LayoutFilter>? filters = null,
        string? lang = null,
        CancellationToken cancellationToken = default);
}
