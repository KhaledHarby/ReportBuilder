using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Models;
using ReportBuilder.Core.Query;
using ReportBuilder.Core.Services;
using ReportBuilder.Infrastructure.Cache;
using ReportBuilder.Infrastructure.Data;
using ActivityEvent = ReportBuilder.Core.Services.ActivityEvent;

namespace ReportBuilder.Infrastructure.Services;

public sealed class DatasetCacheService : IDatasetCacheService
{
    private const int StuckRefreshingMinutes = 24 * 60;
    private const int MinIntervalMinutes = 5;
    private const int MaxIntervalMinutes = 10080;
    private const int RefreshCommandTimeoutSeconds = 600;

    private readonly ReportBuilderDbContext _db;
    private readonly IActivityLog _log;
    private readonly ILogger<DatasetCacheService> _logger;
    private readonly ICacheSettingsService _cacheSettings;
    private readonly ClickHouseDatasetCacheStore _clickHouse;
    private readonly RedisDatasetCacheStore _redis;
    private readonly MongoDatasetCacheStore _mongo;
    private readonly PostgresDatasetCacheStore _postgres;
    private readonly DatasetCacheRefreshQueue _refreshQueue;

    public DatasetCacheService(
        ReportBuilderDbContext db,
        IActivityLog log,
        ILogger<DatasetCacheService> logger,
        ICacheSettingsService cacheSettings,
        ClickHouseDatasetCacheStore clickHouse,
        RedisDatasetCacheStore redis,
        MongoDatasetCacheStore mongo,
        PostgresDatasetCacheStore postgres,
        DatasetCacheRefreshQueue refreshQueue)
    {
        _db = db;
        _log = log;
        _logger = logger;
        _cacheSettings = cacheSettings;
        _clickHouse = clickHouse;
        _redis = redis;
        _mongo = mongo;
        _refreshQueue = refreshQueue;
        _postgres = postgres;
    }

    public async Task<int> RefreshDueAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var dueIds = await _db.Datasets.AsNoTracking()
            .Where(d => d.IsCached
                        && d.IsEnabled
                        && d.CacheState != null
                        && d.CacheState.Status != DatasetCacheStatuses.Refreshing
                        && d.CacheState.NextRefreshAt != null
                        && d.CacheState.NextRefreshAt <= now)
            .OrderBy(d => d.CacheState!.NextRefreshAt)
            .Select(d => d.Id)
            .Take(5)
            .ToListAsync(cancellationToken);

        // Also reclaim stuck refreshes
        var stuckCutoff = now.AddMinutes(-StuckRefreshingMinutes);
        var stuckIds = await _db.Datasets.AsNoTracking()
            .Where(d => d.IsCached
                        && d.CacheState != null
                        && d.CacheState.Status == DatasetCacheStatuses.Refreshing
                        && d.CacheState.LastRefreshStartedAt != null
                        && d.CacheState.LastRefreshStartedAt < stuckCutoff)
            .Select(d => d.Id)
            .Take(3)
            .ToListAsync(cancellationToken);

        var ids = dueIds.Concat(stuckIds).Distinct().ToList();
        var refreshed = 0;
        foreach (var id in ids)
        {
            try
            {
                await RefreshAsync(id, userId: null, cancellationToken);
                refreshed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Scheduled cache refresh failed for dataset {DatasetId}.", id);
            }
        }

        return refreshed;
    }

    public async Task<Dataset> ConfigureAsync(
        Guid datasetId,
        DatasetCacheConfigureRequest request,
        CancellationToken cancellationToken = default)
    {
        _db.ChangeTracker.Clear();
        var dataset = await LoadDatasetAsync(datasetId, cancellationToken);
        var wasCached = dataset.IsCached;
        var isCached = request.IsCached ?? dataset.IsCached;
        var cacheScheduleMode = request.CacheScheduleMode is null
            ? CacheScheduleModes.Normalize(dataset.CacheScheduleMode)
            : CacheScheduleModes.Normalize(request.CacheScheduleMode);
        var cacheIntervalMinutes = request.CacheIntervalMinutes.HasValue || request.CacheScheduleMode is not null
            ? ClampInterval(request.CacheIntervalMinutes ?? dataset.CacheIntervalMinutes)
            : dataset.CacheIntervalMinutes;
        if (cacheScheduleMode == CacheScheduleModes.Interval)
        {
            cacheIntervalMinutes = ClampInterval(cacheIntervalMinutes);
        }

        string? cacheDailyAtLocalTime;
        if (request.CacheDailyAtLocalTime is not null || request.CacheScheduleMode is not null)
        {
            cacheDailyAtLocalTime = NormalizeDailyTime(
                request.CacheDailyAtLocalTime ?? dataset.CacheDailyAtLocalTime)
                ?? (cacheScheduleMode == CacheScheduleModes.DailyAt ? "02:00" : null);
        }
        else
        {
            cacheDailyAtLocalTime = dataset.CacheDailyAtLocalTime;
        }

        if (cacheScheduleMode == CacheScheduleModes.DailyAt && string.IsNullOrWhiteSpace(cacheDailyAtLocalTime))
        {
            cacheDailyAtLocalTime = "02:00";
        }

        var updatedAt = DateTimeOffset.UtcNow;
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [rb].[Datasets]
            SET [IsCached] = {isCached},
                [CacheScheduleMode] = {cacheScheduleMode},
                [CacheIntervalMinutes] = {cacheIntervalMinutes},
                [CacheDailyAtLocalTime] = {cacheDailyAtLocalTime},
                [UpdatedAt] = {updatedAt}
            WHERE [Id] = {datasetId}
            """, cancellationToken);

        if (wasCached && !isCached)
        {
            return await ClearAsync(datasetId, userId: null, cancellationToken);
        }

        if (!isCached)
        {
            _db.ChangeTracker.Clear();
            return await LoadDatasetAsync(datasetId, cancellationToken);
        }

        _db.ChangeTracker.Clear();
        var cached = await _db.Datasets.Include(d => d.CacheState)
            .FirstAsync(d => d.Id == datasetId, cancellationToken);
        cached.IsCached = true;
        cached.CacheScheduleMode = cacheScheduleMode;
        cached.CacheIntervalMinutes = cacheIntervalMinutes;
        cached.CacheDailyAtLocalTime = cacheDailyAtLocalTime;

        if (cached.CacheState is null)
        {
            cached.CacheState = new DatasetCacheState
            {
                DatasetId = datasetId,
                Status = DatasetCacheStatuses.None
            };
            _db.DatasetCacheStates.Add(cached.CacheState);
        }

        var scheduleChanged = !wasCached
            || !string.Equals(dataset.CacheScheduleMode, cacheScheduleMode, StringComparison.OrdinalIgnoreCase)
            || dataset.CacheIntervalMinutes != cacheIntervalMinutes
            || !string.Equals(dataset.CacheDailyAtLocalTime, cacheDailyAtLocalTime, StringComparison.Ordinal);

        if (!wasCached)
        {
            cached.CacheState.NextRefreshAt = DateTimeOffset.UtcNow;
        }
        else if (scheduleChanged)
        {
            cached.CacheState.NextRefreshAt = ComputeNextRefresh(cached, DateTimeOffset.UtcNow);
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Datasets,
                Action = "dataset.cache.configure",
                Message = $"Configured cache for {cached.DisplayName}.",
                EntityType = "Dataset",
                EntityId = datasetId,
                EntityName = cached.DisplayName,
                Details = $"{{\"cached\":true,\"mode\":{JsonString(cacheScheduleMode)}}}"
            },
            cancellationToken);

        _db.ChangeTracker.Clear();
        return await LoadDatasetAsync(datasetId, cancellationToken);
    }

    public async Task<Dataset> RequestRefreshAsync(
        Guid datasetId,
        Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        _db.ChangeTracker.Clear();
        var dataset = await LoadDatasetAsync(datasetId, cancellationToken);
        if (!dataset.IsEnabled)
        {
            throw new ReportQueryException("Enable the dataset before caching it.");
        }

        if (!dataset.IsCached)
        {
            throw new ReportQueryException("Caching is not enabled for this dataset.");
        }

        var settings = await _cacheSettings.GetRuntimeAsync(cancellationToken);
        var provider = CacheProviders.Normalize(settings.DefaultProvider);
        if (!CacheProviders.IsImplemented(provider))
        {
            throw new ReportQueryException(
                $"The {provider} dataset cache is not implemented yet. Choose SQL Server, ClickHouse, Redis, PostgreSQL, or MongoDB in Settings.");
        }

        var state = await EnsureStateAsync(dataset, cancellationToken);
        if (string.Equals(state.Status, DatasetCacheStatuses.Refreshing, StringComparison.OrdinalIgnoreCase))
        {
            return dataset;
        }

        state.Status = DatasetCacheStatuses.Refreshing;
        state.LastRefreshStartedAt = DateTimeOffset.UtcNow;
        state.LastError = null;
        state.RefreshedByServer = Environment.MachineName;
        state.RefreshedByUserId = userId;
        await _db.SaveChangesAsync(cancellationToken);
        _refreshQueue.TryEnqueue(datasetId, userId);

        _db.ChangeTracker.Clear();
        return await LoadDatasetAsync(datasetId, cancellationToken);
    }

    public async Task<Dataset> RefreshAsync(Guid datasetId, Guid? userId = null, CancellationToken cancellationToken = default)
    {
        _db.ChangeTracker.Clear();
        var dataset = await LoadDatasetAsync(datasetId, cancellationToken);
        if (!dataset.IsEnabled)
        {
            throw new ReportQueryException("Enable the dataset before caching it.");
        }

        if (!dataset.IsCached)
        {
            throw new ReportQueryException("Caching is not enabled for this dataset.");
        }

        var state = await EnsureStateAsync(dataset, cancellationToken);
        var settings = await _cacheSettings.GetRuntimeAsync(cancellationToken);
        var provider = CacheProviders.Normalize(settings.DefaultProvider);
        if (!CacheProviders.IsImplemented(provider))
        {
            throw new ReportQueryException(
                $"The {provider} dataset cache is not implemented yet. Choose SQL Server, ClickHouse, Redis, PostgreSQL, or MongoDB in Settings.");
        }

        var tableName = DatasetCacheRouting.TableNameFor(dataset.Id);
        var stagingName = DatasetCacheRouting.StagingTableNameFor(dataset.Id);
        var server = Environment.MachineName;
        var started = DateTimeOffset.UtcNow;
        var sw = Stopwatch.StartNew();

        var connection = (SqlConnection)_db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            if (!await TryAcquireLockAsync(connection, dataset.Id, cancellationToken))
            {
                throw new ReportQueryException("Another server is already refreshing this cache.");
            }

            try
            {
                state.Status = DatasetCacheStatuses.Refreshing;
                state.LastRefreshStartedAt = started;
                state.LastError = null;
                state.RefreshedByServer = server;
                state.RefreshedByUserId = userId;
                await _db.SaveChangesAsync(cancellationToken);

                string cacheTableName;
                long rowCount;
                long? sizeBytes;
                if (provider == CacheProviders.ClickHouse)
                {
                    var stored = await _clickHouse.ReplaceAsync(
                        connection,
                        dataset,
                        settings.ClickHouse,
                        cancellationToken);
                    cacheTableName = stored.TableName;
                    rowCount = stored.RowCount;
                    sizeBytes = stored.SizeBytes;
                }
                else if (provider == CacheProviders.Redis)
                {
                    var stored = await _redis.ReplaceAsync(
                        connection,
                        dataset,
                        RedisCacheClient.Normalize(settings.Redis),
                        cancellationToken);
                    cacheTableName = stored.Key;
                    rowCount = stored.RowCount;
                    sizeBytes = stored.SizeBytes;
                }
                else if (provider == CacheProviders.Mongo)
                {
                    var stored = await _mongo.ReplaceAsync(
                        connection,
                        dataset,
                        MongoCacheClient.Normalize(settings.Mongo),
                        cancellationToken);
                    cacheTableName = stored.CollectionName;
                    rowCount = stored.RowCount;
                    sizeBytes = stored.SizeBytes;
                }
                else if (provider == CacheProviders.Postgres)
                {
                    var stored = await _postgres.ReplaceAsync(
                        connection,
                        dataset,
                        PostgresCacheClient.Normalize(settings.Postgres),
                        cancellationToken);
                    cacheTableName = stored.TableName;
                    rowCount = stored.RowCount;
                    sizeBytes = stored.SizeBytes;
                }
                else
                {
                    var sqlSnapshot = await RefreshSqlServerSnapshotAsync(
                        connection,
                        dataset,
                        tableName,
                        stagingName,
                        cancellationToken);
                    cacheTableName = tableName;
                    rowCount = sqlSnapshot.RowCount;
                    sizeBytes = sqlSnapshot.SizeBytes;
                }

                await DropOtherProviderSnapshotsAsync(
                    connection,
                    settings,
                    dataset.Id,
                    tableName,
                    stagingName,
                    provider,
                    cancellationToken);

                sw.Stop();
                var completed = DateTimeOffset.UtcNow;
                state.Status = DatasetCacheStatuses.Ready;
                state.CacheProvider = provider;
                state.CacheTableName = cacheTableName;
                state.LastRefreshCompletedAt = completed;
                state.LastSuccessAt = completed;
                state.RowCount = rowCount;
                state.DurationMs = (int)Math.Min(sw.ElapsedMilliseconds, int.MaxValue);
                state.SizeBytes = sizeBytes;
                state.CacheVersion += 1;
                state.LastError = null;
                state.NextRefreshAt = ComputeNextRefresh(dataset, completed);
                await _db.SaveChangesAsync(cancellationToken);

                await _log.WriteAsync(
                    new ActivityEvent
                    {
                        Category = ActivityCategories.Datasets,
                        Action = "dataset.cache.refresh",
                        Message = $"Refreshed cache for {dataset.DisplayName} ({rowCount} rows) on {server}.",
                        EntityType = "Dataset",
                        EntityId = dataset.Id,
                        EntityName = dataset.DisplayName,
                        Details = $"{{\"rows\":{rowCount},\"server\":{JsonString(server)},\"ms\":{state.DurationMs},\"provider\":{JsonString(provider)}}}"
                    },
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not ReportQueryException)
            {
                sw.Stop();
                state.Status = DatasetCacheStatuses.Failed;
                state.LastRefreshCompletedAt = DateTimeOffset.UtcNow;
                state.DurationMs = (int)Math.Min(sw.ElapsedMilliseconds, int.MaxValue);
                state.LastError = Truncate(ex.Message, 2000);
                state.NextRefreshAt = ComputeNextRefresh(dataset, DateTimeOffset.UtcNow);
                await _db.SaveChangesAsync(cancellationToken);
                _logger.LogWarning(ex, "Cache refresh failed for dataset {DatasetId}.", dataset.Id);
                throw new ReportQueryException($"Cache refresh failed: {Truncate(ex.Message, 400)}");
            }
            finally
            {
                await ReleaseLockAsync(connection, dataset.Id, cancellationToken);
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }

        _db.ChangeTracker.Clear();
        return await LoadDatasetAsync(datasetId, cancellationToken);
    }

    public async Task<Dataset> ClearAsync(Guid datasetId, Guid? userId = null, CancellationToken cancellationToken = default)
    {
        _db.ChangeTracker.Clear();
        var dataset = await LoadDatasetAsync(datasetId, cancellationToken);
        var state = await EnsureStateAsync(dataset, cancellationToken);
        var stagingName = DatasetCacheRouting.StagingTableNameFor(dataset.Id);
        var server = Environment.MachineName;

        var connection = (SqlConnection)_db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            if (!await TryAcquireLockAsync(connection, dataset.Id, cancellationToken))
            {
                throw new ReportQueryException("Another server is refreshing this cache; try again shortly.");
            }

            try
            {
                var settings = await _cacheSettings.GetRuntimeAsync(cancellationToken);
                await DropOtherProviderSnapshotsAsync(
                    connection,
                    settings,
                    dataset.Id,
                    DatasetCacheRouting.TableNameFor(dataset.Id),
                    stagingName,
                    keepProvider: string.Empty,
                    cancellationToken);

                state.Status = DatasetCacheStatuses.Cleared;
                state.CacheProvider = null;
                state.CacheTableName = null;
                state.RowCount = null;
                state.SizeBytes = null;
                state.DurationMs = null;
                state.LastError = null;
                state.LastRefreshCompletedAt = DateTimeOffset.UtcNow;
                state.RefreshedByServer = server;
                state.RefreshedByUserId = userId;
                state.NextRefreshAt = dataset.IsCached
                    ? ComputeNextRefresh(dataset, DateTimeOffset.UtcNow)
                    : null;
                await _db.SaveChangesAsync(cancellationToken);

                await _log.WriteAsync(
                    new ActivityEvent
                    {
                        Category = ActivityCategories.Datasets,
                        Action = "dataset.cache.clear",
                        Message = $"Cleared cache for {dataset.DisplayName} on {server}.",
                        EntityType = "Dataset",
                        EntityId = dataset.Id,
                        EntityName = dataset.DisplayName
                    },
                    cancellationToken);
            }
            finally
            {
                await ReleaseLockAsync(connection, dataset.Id, cancellationToken);
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }

        _db.ChangeTracker.Clear();
        return await LoadDatasetAsync(datasetId, cancellationToken);
    }

    public async Task SyncColumnCacheIndexAsync(
        Guid datasetId,
        Guid columnId,
        bool indexed,
        CancellationToken cancellationToken = default)
    {
        var dataset = await LoadDatasetAsync(datasetId, cancellationToken);
        var column = dataset.Columns.FirstOrDefault(item => item.Id == columnId);
        if (column is null)
        {
            return;
        }

        var tableName = dataset.CacheState?.CacheTableName;
        if (!dataset.IsCached
            || !string.Equals(dataset.CacheState?.Status, DatasetCacheStatuses.Ready, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(tableName)
            || !string.Equals(
                CacheProviders.Normalize(dataset.CacheState?.CacheProvider),
                CacheProviders.SqlServer,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var connection = (SqlConnection)_db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            if (indexed)
            {
                await TryCreateColumnIndexAsync(connection, tableName, column, cancellationToken);
            }
            else
            {
                await TryDropColumnIndexAsync(connection, tableName, column.ColumnName, cancellationToken);
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    public static DateTimeOffset? ComputeNextRefresh(Dataset dataset, DateTimeOffset fromUtc)
    {
        var mode = CacheScheduleModes.Normalize(dataset.CacheScheduleMode);
        if (mode == CacheScheduleModes.Manual || !dataset.IsCached)
        {
            return null;
        }

        var localZone = TimeZoneInfo.Local;
        var localNow = TimeZoneInfo.ConvertTime(fromUtc, localZone);

        if (mode == CacheScheduleModes.Interval)
        {
            var minutes = ClampInterval(dataset.CacheIntervalMinutes);
            return fromUtc.AddMinutes(minutes);
        }

        // DailyAt — server local clock
        if (!TryParseDailyTime(dataset.CacheDailyAtLocalTime, out var hour, out var minute))
        {
            hour = 2;
            minute = 0;
        }

        var nextLocal = new DateTime(localNow.Year, localNow.Month, localNow.Day, hour, minute, 0);
        if (nextLocal <= localNow.DateTime)
        {
            nextLocal = nextLocal.AddDays(1);
        }

        var unspecified = DateTime.SpecifyKind(nextLocal, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, localZone.GetUtcOffset(unspecified));
    }

    public static int ClampInterval(int? minutes)
    {
        var value = minutes is null or <= 0 ? 60 : minutes.Value;
        return Math.Clamp(value, MinIntervalMinutes, MaxIntervalMinutes);
    }

    public static bool TryParseDailyTime(string? value, out int hour, out int minute)
    {
        hour = 0;
        minute = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Trim().Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2
            || !int.TryParse(parts[0], out hour)
            || !int.TryParse(parts[1], out minute)
            || hour is < 0 or > 23
            || minute is < 0 or > 59)
        {
            return false;
        }

        return true;
    }

    public static string? NormalizeDailyTime(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!TryParseDailyTime(value, out var hour, out var minute))
        {
            throw new ReportQueryException("Daily cache time must be HH:mm (server local time).");
        }

        return $"{hour:D2}:{minute:D2}";
    }

    private async Task<(long RowCount, long? SizeBytes)> RefreshSqlServerSnapshotAsync(
        SqlConnection connection,
        Dataset dataset,
        string tableName,
        string stagingName,
        CancellationToken cancellationToken)
    {
        var source = DatasetSource.UncachedFrom(dataset);
        var staging = SqlIdentifiers.Qualify(DatasetCacheRouting.CacheSchema, stagingName);
        var final = SqlIdentifiers.Qualify(DatasetCacheRouting.CacheSchema, tableName);

        await ExecuteNonQueryAsync(connection, $"""
            IF OBJECT_ID(N'{EscapeLiteral(DatasetCacheRouting.CacheSchema)}.{EscapeLiteral(stagingName)}', N'U') IS NOT NULL
                DROP TABLE {staging};
            """, cancellationToken);

        await ExecuteNonQueryAsync(connection, $"""
            SELECT * INTO {staging} FROM {source};
            """, cancellationToken);

        await ExecuteNonQueryAsync(connection, $"""
            IF OBJECT_ID(N'{EscapeLiteral(DatasetCacheRouting.CacheSchema)}.{EscapeLiteral(tableName)}', N'U') IS NOT NULL
                DROP TABLE {final};
            EXEC sp_rename N'{EscapeLiteral(DatasetCacheRouting.CacheSchema)}.{EscapeLiteral(stagingName)}', N'{EscapeLiteral(tableName)}';
            """, cancellationToken);

        await CreateCacheIndexesAsync(connection, dataset, tableName, cancellationToken);
        var rowCount = await ExecuteScalarLongAsync(connection, $"SELECT COUNT_BIG(1) FROM {final}", cancellationToken);
        var sizeBytes = await EstimateSizeAsync(connection, tableName, cancellationToken);
        return (rowCount, sizeBytes);
    }

    private async Task<Dataset> LoadDatasetAsync(Guid id, CancellationToken cancellationToken)
        => await _db.Datasets
            .Include(d => d.Columns)
            .Include(d => d.Access)
                .ThenInclude(row => row.User)
            .Include(d => d.CacheState)
            .AsSplitQuery()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
            ?? throw new ReportQueryException("Dataset was not found.");

    private async Task<DatasetCacheState> EnsureStateAsync(Dataset dataset, CancellationToken cancellationToken)
    {
        if (dataset.CacheState is not null)
        {
            return dataset.CacheState;
        }

        var existing = await _db.DatasetCacheStates.FirstOrDefaultAsync(s => s.DatasetId == dataset.Id, cancellationToken);
        if (existing is not null)
        {
            dataset.CacheState = existing;
            return existing;
        }

        var state = new DatasetCacheState
        {
            DatasetId = dataset.Id,
            Status = DatasetCacheStatuses.None
        };
        _db.DatasetCacheStates.Add(state);
        dataset.CacheState = state;
        await _db.SaveChangesAsync(cancellationToken);
        return state;
    }

    /// <summary>
    /// Analytics reads these tables with scans, GROUP BY, and COUNT(DISTINCT) over most of the rows, which
    /// suits a clustered columnstore far better than a set of single-column rowstore indexes: it scans an
    /// order of magnitude faster, compresses heavily, and there is only one structure to rebuild on
    /// refresh. Falls back to nonclustered indexes when the server or the column types cannot take one.
    /// </summary>
    private async Task CreateCacheIndexesAsync(
        SqlConnection connection,
        Dataset dataset,
        string tableName,
        CancellationToken cancellationToken)
    {
        var qualified = SqlIdentifiers.Qualify(DatasetCacheRouting.CacheSchema, tableName);
        var createdColumnstore = false;

        if (!dataset.Columns.Any(column => BlocksColumnstore(column.SqlType)))
        {
            try
            {
                await ExecuteNonQueryAsync(connection, $"""
                    CREATE CLUSTERED COLUMNSTORE INDEX {SqlIdentifiers.Quote($"CCI_{tableName}")} ON {qualified};
                    """, cancellationToken);
                createdColumnstore = true;
            }
            catch (SqlException ex)
            {
                _logger.LogInformation(
                    ex,
                    "Columnstore is unavailable for dataset {DatasetId}; using nonclustered indexes instead.",
                    dataset.Id);
            }
        }

        var marked = dataset.Columns
            .Where(column => column.IsIndexed)
            .OrderBy(column => column.Ordinal)
            .ToList();
        foreach (var column in marked)
        {
            await TryCreateColumnIndexAsync(connection, tableName, column, cancellationToken);
        }

        if (createdColumnstore || marked.Count > 0)
        {
            return;
        }

        var columns = dataset.Columns
            .Where(column => IsIndexableSqlType(column.SqlType))
            .OrderBy(column => IndexRank(column.SqlType))
            .ThenBy(column => column.Ordinal)
            .Take(16)
            .ToList();

        for (var i = 0; i < columns.Count; i++)
        {
            await TryCreateColumnIndexAsync(connection, tableName, columns[i], cancellationToken, $"IX_{tableName}_{i}");
        }
    }

    private async Task TryCreateColumnIndexAsync(
        SqlConnection connection,
        string tableName,
        DatasetColumn column,
        CancellationToken cancellationToken,
        string? indexName = null)
    {
        if (!IsIndexableSqlType(column.SqlType))
        {
            _logger.LogInformation(
                "Skipping cache index on {Column} ({SqlType}) for dataset cache {Table}.",
                column.ColumnName,
                column.SqlType,
                tableName);
            return;
        }

        var qualified = SqlIdentifiers.Qualify(DatasetCacheRouting.CacheSchema, tableName);
        var name = SqlIdentifiers.Quote(indexName ?? CacheIndexName(tableName, column.ColumnName));
        var columnName = SqlIdentifiers.Quote(column.ColumnName);
        try
        {
            await ExecuteNonQueryAsync(connection, $"""
                IF EXISTS (
                    SELECT 1 FROM sys.indexes i
                    INNER JOIN sys.tables t ON t.object_id = i.object_id
                    INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
                    WHERE s.name = N'{EscapeLiteral(DatasetCacheRouting.CacheSchema)}'
                      AND t.name = N'{EscapeLiteral(tableName)}'
                      AND i.name = N'{EscapeLiteral(indexName ?? CacheIndexName(tableName, column.ColumnName))}')
                    DROP INDEX {name} ON {qualified};
                CREATE NONCLUSTERED INDEX {name} ON {qualified} ({columnName});
                """, cancellationToken);
        }
        catch (SqlException ex)
        {
            _logger.LogInformation(
                ex,
                "Could not create cache index on {Column} for {Table}.",
                column.ColumnName,
                tableName);
        }
    }

    private async Task TryDropColumnIndexAsync(
        SqlConnection connection,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        var qualified = SqlIdentifiers.Qualify(DatasetCacheRouting.CacheSchema, tableName);
        var physical = CacheIndexName(tableName, columnName);
        var name = SqlIdentifiers.Quote(physical);
        try
        {
            await ExecuteNonQueryAsync(connection, $"""
                IF EXISTS (
                    SELECT 1 FROM sys.indexes i
                    INNER JOIN sys.tables t ON t.object_id = i.object_id
                    INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
                    WHERE s.name = N'{EscapeLiteral(DatasetCacheRouting.CacheSchema)}'
                      AND t.name = N'{EscapeLiteral(tableName)}'
                      AND i.name = N'{EscapeLiteral(physical)}')
                    DROP INDEX {name} ON {qualified};
                """, cancellationToken);
        }
        catch (SqlException ex)
        {
            _logger.LogInformation(ex, "Could not drop cache index {Index} on {Table}.", physical, tableName);
        }
    }

    private static string CacheIndexName(string tableName, string columnName)
    {
        var safe = new string(columnName.Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
        if (string.IsNullOrEmpty(safe))
        {
            safe = "col";
        }

        var name = $"IX_{tableName}_{safe}";
        if (name.Length <= 120)
        {
            return name;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(columnName)))[..12];
        return Truncate($"IX_{tableName}_{hash}", 120);
    }

    /// <summary>Types a clustered columnstore index cannot store.</summary>
    private static bool BlocksColumnstore(string sqlType)
    {
        var type = (sqlType ?? string.Empty).Trim().ToLowerInvariant();
        var paren = type.IndexOf('(', StringComparison.Ordinal);
        if (paren >= 0)
        {
            type = type[..paren].Trim();
        }

        return type is "text" or "ntext" or "image" or "xml" or "sql_variant"
            or "geography" or "geometry" or "hierarchyid";
    }

    private static bool IsIndexableSqlType(string sqlType)
    {
        var type = (sqlType ?? string.Empty).Trim().ToLowerInvariant();
        if (type.Contains("max", StringComparison.Ordinal)
            || type is "text" or "ntext" or "image" or "xml" or "sql_variant"
            or "geography" or "geometry" or "hierarchyid" or "timestamp" or "rowversion")
        {
            return false;
        }

        var paren = type.IndexOf('(', StringComparison.Ordinal);
        if (paren >= 0)
        {
            type = type[..paren];
        }

        type = type.Trim();
        return type is "int" or "bigint" or "smallint" or "tinyint" or "bit"
            or "uniqueidentifier"
            or "date" or "datetime" or "datetime2" or "smalldatetime" or "time"
            or "nvarchar" or "varchar" or "nchar" or "char" or "sysname"
            or "decimal" or "numeric" or "money" or "smallmoney";
    }

    private static int IndexRank(string sqlType)
    {
        var type = (sqlType ?? string.Empty).Trim().ToLowerInvariant();
        var paren = type.IndexOf('(', StringComparison.Ordinal);
        if (paren >= 0)
        {
            type = type[..paren];
        }

        type = type.Trim();
        return type switch
        {
            "uniqueidentifier" or "int" or "bigint" => 0,
            "smallint" or "tinyint" or "bit" => 1,
            "date" or "datetime" or "datetime2" or "smalldatetime" => 2,
            _ => 3
        };
    }

    private async Task DropOtherProviderSnapshotsAsync(
        SqlConnection connection,
        CacheSettingsModel settings,
        Guid datasetId,
        string tableName,
        string stagingName,
        string keepProvider,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(keepProvider, CacheProviders.SqlServer, StringComparison.OrdinalIgnoreCase))
        {
            await DropCacheTablesAsync(connection, tableName, stagingName, cancellationToken);
        }

        if (!string.Equals(keepProvider, CacheProviders.ClickHouse, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await _clickHouse.DropDatasetTablesAsync(settings.ClickHouse, datasetId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogInformation(ex, "Could not drop previous ClickHouse cache for dataset {DatasetId}.", datasetId);
            }
        }

        if (!string.Equals(keepProvider, CacheProviders.Redis, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await _redis.DropDatasetKeysAsync(RedisCacheClient.Normalize(settings.Redis), datasetId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogInformation(ex, "Could not drop previous Redis cache for dataset {DatasetId}.", datasetId);
            }
        }

        if (!string.Equals(keepProvider, CacheProviders.Mongo, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await _mongo.DropDatasetCollectionsAsync(MongoCacheClient.Normalize(settings.Mongo), datasetId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogInformation(ex, "Could not drop previous MongoDB cache for dataset {DatasetId}.", datasetId);
            }
        }

        if (!string.Equals(keepProvider, CacheProviders.Postgres, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await _postgres.DropDatasetTablesAsync(PostgresCacheClient.Normalize(settings.Postgres), datasetId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogInformation(ex, "Could not drop previous PostgreSQL cache for dataset {DatasetId}.", datasetId);
            }
        }
    }

    private static async Task DropCacheTablesAsync(
        SqlConnection connection,
        string tableName,
        string stagingName,
        CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(connection, $"""
            IF OBJECT_ID(N'{EscapeLiteral(DatasetCacheRouting.CacheSchema)}.{EscapeLiteral(stagingName)}', N'U') IS NOT NULL
                DROP TABLE {SqlIdentifiers.Qualify(DatasetCacheRouting.CacheSchema, stagingName)};
            IF OBJECT_ID(N'{EscapeLiteral(DatasetCacheRouting.CacheSchema)}.{EscapeLiteral(tableName)}', N'U') IS NOT NULL
                DROP TABLE {SqlIdentifiers.Qualify(DatasetCacheRouting.CacheSchema, tableName)};
            """, cancellationToken);
    }

    private static async Task<bool> TryAcquireLockAsync(SqlConnection connection, Guid datasetId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Session',
                @LockTimeout = 0;
            SELECT @result;
            """;
        command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255)
        {
            Value = $"ReportBuilder:DatasetCache:{datasetId:N}"
        });
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return result >= 0;
    }

    private static async Task ReleaseLockAsync(SqlConnection connection, Guid datasetId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXEC sp_releaseapplock
                @Resource = @resource,
                @LockOwner = 'Session';
            """;
        command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255)
        {
            Value = $"ReportBuilder:DatasetCache:{datasetId:N}"
        });
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException)
        {
            // Lock may already be gone if the session closed.
        }
    }

    private static async Task ExecuteNonQueryAsync(SqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = RefreshCommandTimeoutSeconds;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> ExecuteScalarLongAsync(SqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = RefreshCommandTimeoutSeconds;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? 0 : Convert.ToInt64(value);
    }

    private static async Task<long?> EstimateSizeAsync(SqlConnection connection, string tableName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT SUM(a.total_pages) * 8 * 1024
            FROM sys.tables t
            INNER JOIN sys.indexes i ON t.object_id = i.object_id AND i.index_id IN (0, 1)
            INNER JOIN sys.partitions p ON i.object_id = p.object_id AND i.index_id = p.index_id
            INNER JOIN sys.allocation_units a ON p.partition_id = a.container_id
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name = @schema AND t.name = @table;
            """;
        command.Parameters.Add(new SqlParameter("@schema", SqlDbType.NVarChar, 128) { Value = DatasetCacheRouting.CacheSchema });
        command.Parameters.Add(new SqlParameter("@table", SqlDbType.NVarChar, 128) { Value = tableName });
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToInt64(value);
    }

    private static string EscapeLiteral(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    private static string JsonString(string value)
        => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
