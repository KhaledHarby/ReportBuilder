using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Identity;
using ReportBuilder.Core.Services;
using ReportBuilder.Infrastructure.Data;

namespace ReportBuilder.Infrastructure.Services;

public sealed class ActivityLogService : IActivityLog
{
    public const int DefaultTake = 50;
    public const int MaxTake = 200;
    private const int MaxMessage = 1000;
    private const int MaxDetails = 4000;
    private const int MaxName = 200;
    private const int MaxUserName = 64;
    private const int MaxPath = 256;

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ActivityLogService> _logger;
    private readonly ICurrentUser _currentUser;

    public ActivityLogService(
        IServiceScopeFactory scopes,
        ILogger<ActivityLogService> logger,
        ICurrentUser currentUser)
    {
        _scopes = scopes;
        _logger = logger;
        _currentUser = currentUser;
    }

    public async Task WriteAsync(ActivityEvent entry, CancellationToken cancellationToken = default)
    {
        try
        {
            var row = new ActivityLog
            {
                Id = Guid.NewGuid(),
                OccurredAt = DateTimeOffset.UtcNow,
                Level = Clip(entry.Level, 16) ?? ActivityLevels.Information,
                Category = Clip(entry.Category, 32) ?? ActivityCategories.System,
                Action = Clip(entry.Action, 64) ?? "unknown",
                Message = Clip(entry.Message, MaxMessage) ?? string.Empty,
                ActorUserId = entry.ActorUserId ?? _currentUser.UserId,
                ActorUserName = Clip(entry.ActorUserName ?? _currentUser.UserName, MaxUserName),
                TargetUserId = entry.TargetUserId,
                TargetUserName = Clip(entry.TargetUserName, MaxUserName),
                EntityType = Clip(entry.EntityType, 32),
                EntityId = entry.EntityId,
                EntityName = Clip(entry.EntityName, MaxName),
                HttpMethod = Clip(entry.HttpMethod, 16),
                Path = Clip(entry.Path, MaxPath),
                StatusCode = entry.StatusCode,
                DurationMs = entry.DurationMs,
                IpAddress = Clip(entry.IpAddress, 64),
                CorrelationId = Clip(entry.CorrelationId, 64),
                Details = Clip(entry.Details, MaxDetails)
            };

            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportBuilderDbContext>();
            db.ActivityLogs.Add(row);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write activity log {Action}", entry.Action);
        }
    }

    public async Task<ActivityLogPage> ListAsync(ActivityLogQuery query, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportBuilderDbContext>();
        var skip = Math.Max(query.Skip, 0);
        var take = query.Take <= 0 ? DefaultTake : Math.Min(query.Take, MaxTake);

        IQueryable<ActivityLog> rows = db.ActivityLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Level))
        {
            var level = query.Level.Trim();
            rows = rows.Where(row => row.Level == level);
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var category = query.Category.Trim();
            rows = rows.Where(row => row.Category == category);
        }

        if (query.From is { } from)
        {
            rows = rows.Where(row => row.OccurredAt >= from);
        }

        if (query.To is { } to)
        {
            rows = rows.Where(row => row.OccurredAt <= to);
        }

        if (!string.IsNullOrWhiteSpace(query.Actor))
        {
            var actor = query.Actor.Trim();
            rows = rows.Where(row =>
                (row.ActorUserName != null && row.ActorUserName.Contains(actor))
                || (row.TargetUserName != null && row.TargetUserName.Contains(actor)));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            rows = rows.Where(row =>
                row.Message.Contains(search)
                || row.Action.Contains(search)
                || (row.ActorUserName != null && row.ActorUserName.Contains(search))
                || (row.TargetUserName != null && row.TargetUserName.Contains(search))
                || (row.EntityName != null && row.EntityName.Contains(search))
                || (row.Path != null && row.Path.Contains(search))
                || (row.CorrelationId != null && row.CorrelationId.Contains(search)));
        }

        var total = await rows.CountAsync(cancellationToken);
        var items = await rows
            .OrderByDescending(row => row.OccurredAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return new ActivityLogPage { Total = total, Items = items };
    }

    public async Task<ActivityLogSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportBuilderDbContext>();
        var settings = await EnsureSettingsAsync(db, cancellationToken);
        return ToSettings(settings);
    }

    public async Task<ActivityLogSettings> UpdateSettingsAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        var days = AppSettings.ClampRetention(retentionDays);
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportBuilderDbContext>();
        var settings = await EnsureSettingsAsync(db, cancellationToken);
        settings.LogRetentionDays = days;
        settings.ModifiedBy = _currentUser.UserId;
        settings.ModifiedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.System,
                Action = "settings.update",
                Message = $"Log retention set to {days} days.",
                Details = $"{{\"retentionDays\":{days}}}"
            },
            cancellationToken);

        return ToSettings(settings);
    }

    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportBuilderDbContext>();
        var settings = await EnsureSettingsAsync(db, cancellationToken);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-settings.LogRetentionDays);
        var deleted = await db.ActivityLogs
            .Where(row => row.OccurredAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        await WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.System,
                Action = "system.purge",
                Message = $"Removed {deleted} activity log rows older than {settings.LogRetentionDays} days.",
                Details = $"{{\"deleted\":{deleted},\"retentionDays\":{settings.LogRetentionDays}}}"
            },
            cancellationToken);

        return deleted;
    }

    private static async Task<AppSettings> EnsureSettingsAsync(
        ReportBuilderDbContext db,
        CancellationToken cancellationToken)
    {
        var settings = await db.AppSettings.FirstOrDefaultAsync(
            row => row.Id == AppSettings.DefaultId,
            cancellationToken);
        if (settings is not null)
        {
            return settings;
        }

        settings = new AppSettings
        {
            Id = AppSettings.DefaultId,
            LogRetentionDays = AppSettings.DefaultRetentionDays,
            ModifiedAt = DateTimeOffset.UtcNow
        };
        db.AppSettings.Add(settings);
        await db.SaveChangesAsync(cancellationToken);
        return settings;
    }

    private static ActivityLogSettings ToSettings(AppSettings settings) => new()
    {
        RetentionDays = settings.LogRetentionDays,
        MinRetentionDays = AppSettings.MinRetentionDays,
        MaxRetentionDays = AppSettings.MaxRetentionDays
    };

    private static string? Clip(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
