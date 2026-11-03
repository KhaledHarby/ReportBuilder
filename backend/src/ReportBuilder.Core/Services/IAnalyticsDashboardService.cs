using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Models;

namespace ReportBuilder.Core.Services;

public interface IAnalyticsDashboardService
{
    Task<IReadOnlyList<AnalyticsDashboard>> ListAsync(CancellationToken cancellationToken = default);
    Task<AnalyticsDashboard?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AnalyticsDashboard> CreateAsync(AnalyticsDashboardWriteRequest request, CancellationToken cancellationToken = default);
    Task<AnalyticsDashboard?> UpdateAsync(Guid id, AnalyticsDashboardWriteRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReportPerson>> ListPeopleAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReportAccessRole>> ListRolesAsync(CancellationToken cancellationToken = default);
}

public sealed class AnalyticsDashboardWriteRequest
{
    public required string Name { get; init; }
    public string? NameArabic { get; init; }
    public string? Description { get; init; }
    public string? DescriptionArabic { get; init; }
    public required AnalyticsDashboardConfig Config { get; init; }
    public ReportStatus? Status { get; init; }
    public bool? IsRestricted { get; init; }
    public string? AccessMode { get; init; }
    public IReadOnlyList<Guid>? AccessUserIds { get; init; }
    public IReadOnlyList<Guid>? AccessRoleIds { get; init; }
}
