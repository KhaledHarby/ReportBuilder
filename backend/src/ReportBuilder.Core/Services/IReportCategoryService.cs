using ReportBuilder.Core.Entities;

namespace ReportBuilder.Core.Services;

public sealed class ReportCategoryWriteRequest
{
    public string Name { get; set; } = string.Empty;
    public string? NameArabic { get; set; }
    public int SortOrder { get; set; }
}

public interface IReportCategoryService
{
    Task<IReadOnlyList<ReportCategory>> ListAsync(CancellationToken cancellationToken = default);
    Task<ReportCategory> CreateAsync(ReportCategoryWriteRequest request, CancellationToken cancellationToken = default);
    Task<ReportCategory?> UpdateAsync(Guid id, ReportCategoryWriteRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
