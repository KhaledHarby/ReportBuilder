using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Models;

namespace ReportBuilder.Core.Services;

public sealed class ReportTemplateWriteRequest
{
    public string Name { get; set; } = string.Empty;
    public string? NameArabic { get; set; }
    public string? Description { get; set; }
    public string? DescriptionArabic { get; set; }
    public Guid DatasetId { get; set; }
    public ReportLayout Layout { get; set; } = new();
    public ReportStatus? Status { get; set; }
    public Guid? CategoryId { get; set; }
    public bool? IsRestricted { get; set; }
    public string? AccessMode { get; set; }
    public IReadOnlyList<Guid>? AccessUserIds { get; set; }
    public IReadOnlyList<Guid>? AccessRoleIds { get; set; }
}

public sealed class ReportPerson
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? DisplayNameArabic { get; set; }
}

public sealed class ReportAccessRole
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public interface IReportTemplateService
{
    Task<IReadOnlyList<ReportTemplate>> ListAsync(CancellationToken cancellationToken = default);
    Task<ReportTemplate?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ReportTemplate> CreateAsync(ReportTemplateWriteRequest request, CancellationToken cancellationToken = default);
    Task<ReportTemplate?> DuplicateAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ReportTemplate?> UpdateAsync(Guid id, ReportTemplateWriteRequest request, CancellationToken cancellationToken = default);
    Task<ReportTemplate?> SetStatusAsync(Guid id, ReportStatus status, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ReportHistory> ListRevisionsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ReportTemplate?> RestoreRevisionAsync(Guid id, int version, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReportPerson>> ListPeopleAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReportAccessRole>> ListRolesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, string>> UserNamesAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken = default);
}
