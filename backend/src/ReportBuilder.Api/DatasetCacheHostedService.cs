using ReportBuilder.Core.Services;
using ReportBuilder.Infrastructure.Cache;

namespace ReportBuilder.Api;

public sealed class DatasetCacheHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly DatasetCacheRefreshQueue _queue;
    private readonly ILogger<DatasetCacheHostedService> _logger;

    public DatasetCacheHostedService(
        IServiceScopeFactory scopes,
        DatasetCacheRefreshQueue queue,
        ILogger<DatasetCacheHostedService> logger)
    {
        _scopes = scopes;
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (_queue.TryRead(out var item))
                {
                    await RefreshOneAsync(item.DatasetId, item.UserId, stoppingToken);
                }

                using var scope = _scopes.CreateScope();
                var cache = scope.ServiceProvider.GetRequiredService<IDatasetCacheService>();
                var refreshed = await cache.RefreshDueAsync(stoppingToken);
                if (refreshed > 0)
                {
                    _logger.LogInformation("Refreshed {Count} dataset cache(s).", refreshed);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Dataset cache scheduler failed.");
            }

            using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            wait.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                await _queue.WaitToReadAsync(wait.Token);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // Scheduled poll.
            }
        }
    }

    private async Task RefreshOneAsync(Guid datasetId, Guid? userId, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var cache = scope.ServiceProvider.GetRequiredService<IDatasetCacheService>();
            await cache.RefreshAsync(datasetId, userId, stoppingToken);
            _logger.LogInformation("Finished queued cache refresh for dataset {DatasetId}.", datasetId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Queued cache refresh failed for dataset {DatasetId}.", datasetId);
        }
    }
}
