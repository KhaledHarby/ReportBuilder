using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Authentication;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ReportBuilder.Core.Query;
using ReportBuilder.Core.Services;
using StackExchange.Redis;

namespace ReportBuilder.Infrastructure.Cache;

public sealed partial class RedisCacheClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ConcurrentDictionary<string, Lazy<Task<ConnectionMultiplexer>>> _pool = new(StringComparer.Ordinal);
    private readonly ILogger<RedisCacheClient> _logger;

    public RedisCacheClient(ILogger<RedisCacheClient> logger)
    {
        _logger = logger;
    }

    public async Task PingAsync(CacheProviderConnection connection, CancellationToken cancellationToken)
    {
        var database = await GetDatabaseAsync(connection, cancellationToken);
        try
        {
            await database.PingAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ReportQueryException)
        {
            throw new ReportQueryException(
                "Could not reach Redis. Check host, port, TLS, and credentials. "
                + Truncate(ex.GetBaseException().Message, 240));
        }
    }

    public async Task<RedisStoreResult> ReplaceSnapshotAsync(
        CacheProviderConnection connection,
        Guid datasetId,
        DatasetCacheSnapshot snapshot,
        CancellationToken cancellationToken)
        => await ReplaceSnapshotAsync(
            connection,
            datasetId,
            snapshot.Columns,
            StreamSnapshot(snapshot),
            cancellationToken);

    public async Task<RedisStoreResult> ReplaceSnapshotAsync(
        CacheProviderConnection connection,
        Guid datasetId,
        IReadOnlyList<string> columns,
        IAsyncEnumerable<object?[]> rows,
        CancellationToken cancellationToken)
    {
        var database = await GetDatabaseAsync(connection, cancellationToken);
        var final = DatasetCacheRouting.RedisKeyFor(datasetId);
        var staging = DatasetCacheRouting.RedisStagingKeyFor(datasetId);
        var finalRows = RowsKey(final);
        var stagingRows = RowsKey(staging);

        await database.KeyDeleteAsync([staging, stagingRows]);

        const int batch = 500;
        var buffer = new RedisValue[batch];
        var offset = 0;
        var rowCount = 0L;
        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            var mapped = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < columns.Count; i++)
            {
                mapped[columns[i]] = i < row.Length ? row[i] : null;
            }

            buffer[offset++] = JsonSerializer.Serialize(mapped, JsonOptions);
            rowCount++;
            if (offset < buffer.Length)
            {
                continue;
            }

            await database.ListRightPushAsync(stagingRows, buffer);
            offset = 0;
        }

        if (offset > 0)
        {
            await database.ListRightPushAsync(stagingRows, buffer.AsSpan(0, offset).ToArray());
        }

        var meta = JsonSerializer.Serialize(new SnapshotMeta
        {
            Columns = columns.ToList(),
            RowCount = rowCount
        }, JsonOptions);
        await database.StringSetAsync(staging, meta);

        var tran = database.CreateTransaction();
        _ = tran.KeyDeleteAsync(final);
        _ = tran.KeyDeleteAsync(finalRows);
        _ = tran.KeyRenameAsync(staging, final);
        if (rowCount > 0)
        {
            _ = tran.KeyRenameAsync(stagingRows, finalRows);
        }

        if (!await tran.ExecuteAsync())
        {
            throw new ReportQueryException("Redis rejected the cache swap. Try refresh again.");
        }

        long? sizeBytes = null;
        try
        {
            var metaBytes = await MemoryUsageAsync(database, final);
            var rowBytes = await MemoryUsageAsync(database, finalRows);
            if (metaBytes is not null || rowBytes is not null)
            {
                sizeBytes = (metaBytes ?? 0) + (rowBytes ?? 0);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogInformation(ex, "Could not estimate Redis memory for {Key}.", final);
        }

        return new RedisStoreResult(final, rowCount, sizeBytes);
    }

    private static async IAsyncEnumerable<object?[]> StreamSnapshot(DatasetCacheSnapshot snapshot)
    {
        foreach (var row in snapshot.Rows)
        {
            var values = new object?[snapshot.Columns.Count];
            for (var i = 0; i < snapshot.Columns.Count; i++)
            {
                values[i] = row.TryGetValue(snapshot.Columns[i], out var value) ? value : null;
            }

            yield return values;
        }

        await Task.CompletedTask;
    }

    public async Task<DatasetCacheSnapshot> GetSnapshotAsync(
        CacheProviderConnection connection,
        string key,
        CancellationToken cancellationToken)
    {
        var database = await GetDatabaseAsync(connection, cancellationToken);
        var metaJson = await database.StringGetAsync(key);
        if (metaJson.IsNullOrEmpty)
        {
            throw new ReportQueryException("The Redis dataset cache was not found. Refresh the cache and try again.");
        }

        SnapshotMeta? meta;
        try
        {
            meta = JsonSerializer.Deserialize<SnapshotMeta>((string)metaJson!, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ReportQueryException($"Redis cache metadata is invalid: {ex.Message}");
        }

        var columns = meta?.Columns ?? [];
        var values = await database.ListRangeAsync(RowsKey(key));
        cancellationToken.ThrowIfCancellationRequested();
        var rows = new List<IReadOnlyDictionary<string, object?>>(values.Length);
        foreach (var value in values)
        {
            if (value.IsNullOrEmpty)
            {
                continue;
            }

            Dictionary<string, JsonElement>? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>((string)value!, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new ReportQueryException($"Redis cache row is invalid: {ex.Message}");
            }

            var mapped = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (parsed is null)
            {
                rows.Add(mapped);
                continue;
            }

            foreach (var column in columns.Count > 0 ? columns : parsed.Keys.ToList())
            {
                mapped[column] = parsed.TryGetValue(column, out var element)
                    ? Normalize(element)
                    : null;
            }

            rows.Add(mapped);
        }

        return new DatasetCacheSnapshot
        {
            Columns = columns,
            Rows = rows
        };
    }

    public async Task DropDatasetKeysAsync(
        CacheProviderConnection connection,
        Guid datasetId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connection.Host))
        {
            return;
        }

        var database = await GetDatabaseAsync(connection, cancellationToken);
        var final = DatasetCacheRouting.RedisKeyFor(datasetId);
        var staging = DatasetCacheRouting.RedisStagingKeyFor(datasetId);
        await database.KeyDeleteAsync([final, RowsKey(final), staging, RowsKey(staging)]);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public static CacheProviderConnection Normalize(CacheProviderConnection connection)
    {
        var copy = new CacheProviderConnection
        {
            Host = connection.Host ?? string.Empty,
            Port = connection.Port,
            Database = connection.Database ?? string.Empty,
            Username = connection.Username ?? string.Empty,
            Password = connection.Password ?? string.Empty,
            Secure = connection.Secure,
            ConnectionString = connection.ConnectionString ?? string.Empty,
            RedisDatabase = connection.RedisDatabase,
            HasPassword = connection.HasPassword
        };

        var raw = copy.Host.Trim();
        if (Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            && (uri.Scheme.Equals("redis", StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals("rediss", StringComparison.OrdinalIgnoreCase)))
        {
            copy.Secure = uri.Scheme.Equals("rediss", StringComparison.OrdinalIgnoreCase);
            copy.Host = uri.IdnHost;
            if (!uri.IsDefaultPort)
            {
                copy.Port = uri.Port;
            }

            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                var parts = uri.UserInfo.Split(':', 2);
                copy.Username = Uri.UnescapeDataString(parts[0]);
                if (parts.Length > 1 && !string.IsNullOrEmpty(parts[1]))
                {
                    copy.Password = Uri.UnescapeDataString(parts[1]);
                }
            }

            if (uri.AbsolutePath.Length > 1
                && int.TryParse(uri.AbsolutePath.Trim('/'), out var db)
                && db is >= 0 and <= 15)
            {
                copy.RedisDatabase = db;
            }
        }
        else
        {
            raw = raw.TrimEnd('/');
            var colon = raw.LastIndexOf(':');
            if (colon > 0
                && raw.IndexOf(':') == colon
                && int.TryParse(raw[(colon + 1)..], out var parsedPort)
                && parsedPort is > 0 and < 65536)
            {
                copy.Host = raw[..colon];
                copy.Port = parsedPort;
            }
            else
            {
                copy.Host = raw;
            }
        }

        copy.Host = copy.Host.Trim();
        var azure = copy.Host.Contains("redis.cache.windows.net", StringComparison.OrdinalIgnoreCase)
            || copy.Host.Contains("redis.azure.net", StringComparison.OrdinalIgnoreCase);
        if (azure)
        {
            copy.Secure = true;
            if (copy.Port is <= 0 or 6379)
            {
                copy.Port = 6380;
            }
        }

        if (copy.Port <= 0)
        {
            copy.Port = copy.Secure ? 6380 : 6379;
        }

        if (copy.RedisDatabase is null or < 0 or > 15)
        {
            copy.RedisDatabase = 0;
        }

        return copy;
    }

    public static void Validate(CacheProviderConnection connection)
    {
        if (string.IsNullOrWhiteSpace(connection.Host))
        {
            throw new ReportQueryException(
                "Redis host is required. Use the hostname, host:port, or a redis:// / rediss:// URL.");
        }
    }

    public void Dispose()
    {
        foreach (var entry in _pool.Values)
        {
            if (!entry.IsValueCreated)
            {
                continue;
            }

            try
            {
                if (entry.Value.IsCompletedSuccessfully)
                {
                    entry.Value.Result.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not dispose a Redis connection.");
            }
        }

        _pool.Clear();
    }

    private async Task<IDatabase> GetDatabaseAsync(CacheProviderConnection connection, CancellationToken cancellationToken)
    {
        var normalized = Normalize(connection);
        Validate(normalized);
        var key = Fingerprint(normalized);
        var lazy = _pool.GetOrAdd(key, _ => new Lazy<Task<ConnectionMultiplexer>>(
            () => ConnectAsync(normalized),
            LazyThreadSafetyMode.ExecutionAndPublication));

        ConnectionMultiplexer multiplexer;
        try
        {
            multiplexer = await lazy.Value.WaitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _pool.TryRemove(key, out _);
            if (ex is ReportQueryException)
            {
                throw;
            }

            throw new ReportQueryException(
                "Could not reach Redis. Check host, port, TLS, and credentials. "
                + Truncate(ex.GetBaseException().Message, 240));
        }

        return multiplexer.GetDatabase(normalized.RedisDatabase ?? 0);
    }

    private static async Task<ConnectionMultiplexer> ConnectAsync(CacheProviderConnection connection)
    {
        var options = new ConfigurationOptions
        {
            AbortOnConnectFail = true,
            ConnectTimeout = 10_000,
            SyncTimeout = 120_000,
            AsyncTimeout = 120_000,
            Ssl = connection.Secure,
            SslHost = connection.Host,
            SslProtocols = SslProtocols.Tls12,
            User = string.IsNullOrWhiteSpace(connection.Username) ? string.Empty : connection.Username.Trim(),
            Password = connection.Password ?? string.Empty,
            DefaultDatabase = connection.RedisDatabase ?? 0
        };
        options.EndPoints.Add(connection.Host.Trim(), connection.Port);
        return await ConnectionMultiplexer.ConnectAsync(options);
    }

    private static async Task<long?> MemoryUsageAsync(IDatabase database, RedisKey key)
    {
        var name = key.ToString();
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var result = await database.ExecuteAsync("MEMORY", "USAGE", name);
        return result.IsNull ? null : (long)result;
    }

    private static RedisKey RowsKey(RedisKey key) => (key.ToString() ?? string.Empty) + ":rows";

    private static string Fingerprint(CacheProviderConnection connection)
        => string.Join('|',
            connection.Host.Trim().ToLowerInvariant(),
            connection.Port.ToString(CultureInfo.InvariantCulture),
            connection.Username.Trim(),
            (connection.RedisDatabase ?? 0).ToString(CultureInfo.InvariantCulture),
            connection.Secure ? "s" : "p",
            connection.Password);

    private static object? Normalize(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => ParseString(element.GetString()),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l
                : element.TryGetDecimal(out var m) ? m
                : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array or JsonValueKind.Object => element.GetRawText(),
            _ => element.ToString()
        };

    private static object? ParseString(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)
            && (value.Contains('T', StringComparison.Ordinal) || value.Contains('-', StringComparison.Ordinal)))
        {
            return date;
        }

        return value;
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    private sealed class SnapshotMeta
    {
        public List<string> Columns { get; set; } = [];
        public long RowCount { get; set; }
    }
}

public readonly record struct RedisStoreResult(string Key, long RowCount, long? SizeBytes);
