namespace ReportBuilder.Core.Services;

public sealed class CacheProviderConnection
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Database { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool Secure { get; set; }
    public string ConnectionString { get; set; } = string.Empty;
    public int? RedisDatabase { get; set; }
    public bool HasPassword { get; set; }
}

public sealed class CacheSettingsModel
{
    public string DefaultProvider { get; set; } = Models.CacheProviders.SqlServer;
    public CacheProviderConnection ClickHouse { get; set; } = new() { Port = 8443, Database = "default", Username = "default", Secure = true };
    public CacheProviderConnection Timescale { get; set; } = new() { Port = 5432 };
    public CacheProviderConnection Redis { get; set; } = new() { Port = 6379 };
    public CacheProviderConnection Postgres { get; set; } = new() { Port = 5432, Database = "reportbuilder" };
    public CacheProviderConnection Mongo { get; set; } = new() { Port = 27017, Database = "reportbuilder" };
    public IReadOnlyList<string> ImplementedProviders { get; set; } = Models.CacheProviders.Implemented;
}

public interface ICacheSettingsService
{
    Task<CacheSettingsModel> GetAsync(CancellationToken cancellationToken = default);
    Task<CacheSettingsModel> GetRuntimeAsync(CancellationToken cancellationToken = default);
    Task<CacheSettingsModel> UpdateAsync(CacheSettingsModel request, Guid? userId, CancellationToken cancellationToken = default);
    Task TestAsync(string provider, CacheProviderConnection? connection, CancellationToken cancellationToken = default);
}
