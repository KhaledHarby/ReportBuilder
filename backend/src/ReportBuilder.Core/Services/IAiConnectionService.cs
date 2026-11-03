namespace ReportBuilder.Core.Services;

public interface IAiConnectionService
{
    Task<IReadOnlyList<AiConnectionModel>> ListAsync(CancellationToken cancellationToken = default);
    Task<AiConnectionModel> CreateAsync(AiConnectionWrite request, CancellationToken cancellationToken = default);
    Task<AiConnectionModel> UpdateAsync(Guid id, AiConnectionWrite request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<string> TestAsync(AiConnectionWrite request, CancellationToken cancellationToken = default);
    Task<AiConnectionRuntime?> GetDefaultAsync(CancellationToken cancellationToken = default);
}

public sealed class AiConnectionModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public bool HasApiKey { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsDefault { get; set; }
}

public sealed class AiConnectionWrite
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsDefault { get; set; }
}

public sealed class AiConnectionRuntime
{
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}
