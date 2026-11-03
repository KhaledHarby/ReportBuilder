using ReportBuilder.Core.Services;

namespace ReportBuilder.Api;

public sealed class ActivityLogPurgeHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ActivityLogPurgeHostedService> _logger;

    public ActivityLogPurgeHostedService(
        IServiceScopeFactory scopes,
        ILogger<ActivityLogPurgeHostedService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var logs = scope.ServiceProvider.GetRequiredService<IActivityLog>();
                var deleted = await logs.PurgeExpiredAsync(stoppingToken);
                if (deleted > 0)
                {
                    _logger.LogInformation("Purged {Count} activity log rows.", deleted);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Activity log purge failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
