using System.Globalization;
using System.Text;
using System.Threading.Channels;
using ReportBuilder.Infrastructure.Services;

namespace ReportBuilder.Api;

/// <summary>
/// Overwrites `{dashboard name} query.sql` and `{dashboard name} sql.sql` when generated-query
/// logging is on. Writing happens off the request path so a dashboard click never waits on disk.
/// </summary>
public sealed class AnalyticsSqlLogWriter : BackgroundService
{
    private readonly Channel<AnalyticsSqlLogFile> _queue;
    private readonly ILogger<AnalyticsSqlLogWriter> _logger;
    private readonly string _directory;
    private readonly int _retentionDays;
    private DateOnly _lastPurge = DateOnly.MinValue;

    public AnalyticsSqlLogWriter(
        Channel<AnalyticsSqlLogFile> queue,
        IConfiguration configuration,
        ILogger<AnalyticsSqlLogWriter> logger)
    {
        _queue = queue;
        _logger = logger;
        _directory = AnalyticsSqlLogOptions.ResolveDirectory(configuration);
        _retentionDays = Math.Clamp(configuration.GetValue("Analytics:SqlLogRetentionDays", 7), 1, 365);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var file in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                Purge();
                Directory.CreateDirectory(_directory);
                var suffix = file.Kind == "data" ? "sql.sql" : "query.sql";
                var name = $"{SafeFileName(file.DashboardName)} {suffix}";
                await File.WriteAllTextAsync(Path.Combine(_directory, name), file.Content, Encoding.UTF8, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not write analytics SQL log for dashboard {DashboardId}.", file.DashboardId);
            }
        }
    }

    private void Purge()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_lastPurge == today || !Directory.Exists(_directory))
        {
            return;
        }

        _lastPurge = today;
        var cutoff = today.AddDays(-_retentionDays);
        foreach (var folder in Directory.EnumerateDirectories(_directory))
        {
            var name = Path.GetFileName(folder);
            if (!DateOnly.TryParseExact(name, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                || day >= cutoff)
            {
                continue;
            }

            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not purge analytics SQL log folder {Folder}.", folder);
            }
        }
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = (value ?? string.Empty).Select(ch => invalid.Contains(ch) ? ' ' : ch).ToArray();
        var cleaned = string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Length > 80)
        {
            cleaned = cleaned[..80].Trim();
        }

        return string.IsNullOrEmpty(cleaned) ? "analytics" : cleaned;
    }
}
