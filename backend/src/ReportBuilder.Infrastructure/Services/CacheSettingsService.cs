using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Models;
using ReportBuilder.Core.Query;
using ReportBuilder.Core.Services;
using ReportBuilder.Infrastructure.Cache;
using ReportBuilder.Infrastructure.Data;

namespace ReportBuilder.Infrastructure.Services;

public sealed class CacheSettingsService : ICacheSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly ReportBuilderDbContext _db;
    private readonly IActivityLog _log;
    private readonly ClickHouseHttpClient _clickHouse;
    private readonly RedisCacheClient _redis;
    private readonly MongoCacheClient _mongo;
    private readonly PostgresCacheClient _postgres;

    public CacheSettingsService(
        ReportBuilderDbContext db,
        IActivityLog log,
        ClickHouseHttpClient clickHouse,
        RedisCacheClient redis,
        MongoCacheClient mongo,
        PostgresCacheClient postgres)
    {
        _db = db;
        _log = log;
        _clickHouse = clickHouse;
        _redis = redis;
        _mongo = mongo;
        _postgres = postgres;
    }

    public async Task<CacheSettingsModel> GetAsync(CancellationToken cancellationToken = default)
    {
        var entity = await EnsureAsync(cancellationToken);
        return ToModel(entity, includeSecrets: false);
    }

    public async Task<CacheSettingsModel> GetRuntimeAsync(CancellationToken cancellationToken = default)
        => await GetSecretsAsync(cancellationToken);

    public async Task<CacheSettingsModel> UpdateAsync(
        CacheSettingsModel request,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        var entity = await EnsureAsync(cancellationToken);
        var current = ToModel(entity, includeSecrets: true);
        var next = Merge(current, request);
        next.ClickHouse = ClickHouseHttpClient.Normalize(next.ClickHouse);
        next.Redis = RedisCacheClient.Normalize(next.Redis);
        next.Mongo = MongoCacheClient.Normalize(next.Mongo);
        next.Postgres = PostgresCacheClient.Normalize(next.Postgres);
        var provider = CacheProviders.Normalize(next.DefaultProvider);
        if (!CacheProviders.IsImplemented(provider))
        {
            throw new ReportQueryException(
                $"The {provider} dataset cache is not implemented yet. Choose SQL Server, ClickHouse, Redis, PostgreSQL, or MongoDB.");
        }

        if (provider == CacheProviders.ClickHouse)
        {
            ClickHouseHttpClient.Validate(next.ClickHouse);
        }

        if (provider == CacheProviders.Redis)
        {
            RedisCacheClient.Validate(next.Redis);
        }

        if (provider == CacheProviders.Mongo)
        {
            MongoCacheClient.Validate(next.Mongo);
        }

        if (provider == CacheProviders.Postgres)
        {
            PostgresCacheClient.Validate(next.Postgres);
        }

        entity.DefaultProvider = provider;
        entity.ProvidersJson = JsonSerializer.Serialize(ToStored(next), JsonOptions);
        entity.ModifiedBy = userId;
        entity.ModifiedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.System,
                Action = "settings.cache.update",
                Message = $"Default dataset cache set to {provider}.",
                Details = $"{{\"defaultProvider\":\"{provider}\"}}"
            },
            cancellationToken);

        return ToModel(entity, includeSecrets: false);
    }

    public async Task TestAsync(
        string provider,
        CacheProviderConnection? connection,
        CancellationToken cancellationToken = default)
    {
        var stored = await GetSecretsAsync(cancellationToken);
        var key = CacheProviders.Normalize(provider);
        if (key == CacheProviders.SqlServer)
        {
            return;
        }

        if (key == CacheProviders.ClickHouse)
        {
            var merged = ClickHouseHttpClient.Normalize(
                MergeConnection(stored.ClickHouse, connection, defaultPort: 8123, "default", "default"));

            ClickHouseHttpClient.Validate(merged);
            await _clickHouse.PingAsync(merged, cancellationToken);
            return;
        }

        if (key == CacheProviders.Redis)
        {
            var merged = RedisCacheClient.Normalize(MergeConnection(stored.Redis, connection, defaultPort: 6379));
            RedisCacheClient.Validate(merged);
            await _redis.PingAsync(merged, cancellationToken);
            return;
        }

        if (key == CacheProviders.Mongo)
        {
            var merged = MongoCacheClient.Normalize(
                MergeConnection(stored.Mongo, connection, defaultPort: 27017, "reportbuilder"));
            MongoCacheClient.Validate(merged);
            await _mongo.PingAsync(merged, cancellationToken);
            return;
        }

        if (key == CacheProviders.Postgres)
        {
            var merged = PostgresCacheClient.Normalize(
                MergeConnection(stored.Postgres, connection, defaultPort: 5432, "reportbuilder"));
            PostgresCacheClient.Validate(merged);
            await _postgres.PingAsync(merged, cancellationToken);
            return;
        }

        throw new ReportQueryException($"The {key} dataset cache is not implemented yet.");
    }

    internal async Task<CacheSettingsModel> GetSecretsAsync(CancellationToken cancellationToken = default)
    {
        var entity = await EnsureAsync(cancellationToken);
        return ToModel(entity, includeSecrets: true);
    }

    private async Task<CacheSettings> EnsureAsync(CancellationToken cancellationToken)
    {
        var entity = await _db.CacheSettings.FirstOrDefaultAsync(row => row.Id == CacheSettings.DefaultId, cancellationToken);
        if (entity is not null)
        {
            return entity;
        }

        entity = new CacheSettings
        {
            Id = CacheSettings.DefaultId,
            DefaultProvider = CacheProviders.SqlServer,
            ProvidersJson = "{}",
            ModifiedAt = DateTimeOffset.UtcNow
        };
        _db.CacheSettings.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    private static CacheSettingsModel ToModel(CacheSettings entity, bool includeSecrets)
    {
        StoredProviders stored;
        try
        {
            stored = JsonSerializer.Deserialize<StoredProviders>(entity.ProvidersJson, JsonOptions) ?? new StoredProviders();
        }
        catch (JsonException)
        {
            stored = new StoredProviders();
        }

        var model = new CacheSettingsModel
        {
            DefaultProvider = CacheProviders.Normalize(entity.DefaultProvider),
            ClickHouse = Sanitize(
                ClickHouseHttpClient.Normalize(
                    ApplyDefaults(stored.ClickHouse ?? new CacheProviderConnection { Secure = true }, 8443, "default", "default")),
                includeSecrets),
            Timescale = Sanitize(ApplyDefaults(stored.Timescale, 5432), includeSecrets),
            Redis = Sanitize(RedisCacheClient.Normalize(ApplyDefaults(stored.Redis, 6379)), includeSecrets),
            Postgres = Sanitize(
                PostgresCacheClient.Normalize(ApplyDefaults(stored.Postgres, 5432, "reportbuilder")),
                includeSecrets),
            Mongo = Sanitize(
                MongoCacheClient.Normalize(ApplyDefaults(stored.Mongo, 27017, "reportbuilder")),
                includeSecrets),
            ImplementedProviders = CacheProviders.Implemented
        };
        return model;
    }

    private static StoredProviders ToStored(CacheSettingsModel model) => new()
    {
        ClickHouse = Copy(model.ClickHouse),
        Timescale = Copy(model.Timescale),
        Redis = Copy(model.Redis),
        Postgres = Copy(model.Postgres),
        Mongo = Copy(model.Mongo)
    };

    private static CacheSettingsModel Merge(CacheSettingsModel current, CacheSettingsModel request)
        => new()
        {
            DefaultProvider = request.DefaultProvider,
            ClickHouse = MergeConnection(current.ClickHouse, request.ClickHouse, 8123, "default", "default"),
            Timescale = MergeConnection(current.Timescale, request.Timescale, 5432),
            Redis = MergeConnection(current.Redis, request.Redis, 6379),
            Postgres = MergeConnection(current.Postgres, request.Postgres, 5432, "reportbuilder"),
            Mongo = MergeConnection(current.Mongo, request.Mongo, 27017, "reportbuilder"),
            ImplementedProviders = CacheProviders.Implemented
        };

    private static CacheProviderConnection MergeConnection(
        CacheProviderConnection current,
        CacheProviderConnection? incoming,
        int defaultPort,
        string? defaultDatabase = null,
        string? defaultUsername = null)
    {
        incoming ??= new CacheProviderConnection();
        var merged = ApplyDefaults(Copy(incoming), defaultPort, defaultDatabase, defaultUsername);
        merged.Host = incoming.Host?.Trim() ?? string.Empty;
        merged.Database = string.IsNullOrWhiteSpace(incoming.Database) ? (defaultDatabase ?? string.Empty) : incoming.Database.Trim();
        merged.Username = string.IsNullOrWhiteSpace(incoming.Username) ? (defaultUsername ?? string.Empty) : incoming.Username.Trim();
        merged.ConnectionString = incoming.ConnectionString?.Trim() ?? string.Empty;
        merged.Secure = incoming.Secure;
        merged.RedisDatabase = incoming.RedisDatabase;
        merged.Port = incoming.Port <= 0 ? defaultPort : incoming.Port;
        if (string.IsNullOrWhiteSpace(incoming.Password))
        {
            merged.Password = current.Password;
        }

        merged.HasPassword = !string.IsNullOrEmpty(merged.Password);
        return merged;
    }

    private static CacheProviderConnection ApplyDefaults(
        CacheProviderConnection? source,
        int defaultPort,
        string? defaultDatabase = null,
        string? defaultUsername = null)
    {
        var connection = source is null ? new CacheProviderConnection() : Copy(source);
        if (connection.Port <= 0)
        {
            connection.Port = defaultPort;
        }

        if (string.IsNullOrWhiteSpace(connection.Database) && defaultDatabase is not null)
        {
            connection.Database = defaultDatabase;
        }

        if (string.IsNullOrWhiteSpace(connection.Username) && defaultUsername is not null)
        {
            connection.Username = defaultUsername;
        }

        connection.HasPassword = !string.IsNullOrEmpty(connection.Password);
        return connection;
    }

    private static CacheProviderConnection Sanitize(CacheProviderConnection connection, bool includeSecrets)
    {
        var copy = Copy(connection);
        copy.HasPassword = !string.IsNullOrEmpty(copy.Password);
        if (!includeSecrets)
        {
            copy.Password = string.Empty;
        }

        return copy;
    }

    private static CacheProviderConnection Copy(CacheProviderConnection source) => new()
    {
        Host = source.Host,
        Port = source.Port,
        Database = source.Database,
        Username = source.Username,
        Password = source.Password,
        Secure = source.Secure,
        ConnectionString = source.ConnectionString,
        RedisDatabase = source.RedisDatabase,
        HasPassword = source.HasPassword
    };

    private sealed class StoredProviders
    {
        public CacheProviderConnection? ClickHouse { get; set; }
        public CacheProviderConnection? Timescale { get; set; }
        public CacheProviderConnection? Redis { get; set; }
        public CacheProviderConnection? Postgres { get; set; }
        public CacheProviderConnection? Mongo { get; set; }
    }
}
