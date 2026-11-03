using ReportBuilder.Core.Entities;

namespace ReportBuilder.Core.Services;

public interface IDatasetCacheService
{
    Task<Dataset> ConfigureAsync(Guid datasetId, DatasetCacheConfigureRequest request, CancellationToken cancellationToken = default);
    Task<Dataset> RequestRefreshAsync(Guid datasetId, Guid? userId = null, CancellationToken cancellationToken = default);
    Task<Dataset> RefreshAsync(Guid datasetId, Guid? userId = null, CancellationToken cancellationToken = default);
    Task<Dataset> ClearAsync(Guid datasetId, Guid? userId = null, CancellationToken cancellationToken = default);
    Task<int> RefreshDueAsync(CancellationToken cancellationToken = default);
    Task SyncColumnCacheIndexAsync(Guid datasetId, Guid columnId, bool indexed, CancellationToken cancellationToken = default);
}
