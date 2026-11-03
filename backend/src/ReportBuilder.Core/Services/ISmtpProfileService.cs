namespace ReportBuilder.Core.Services;

public interface ISmtpProfileService
{
    Task<IReadOnlyList<SmtpProfileModel>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SmtpProfileOption>> ListOptionsAsync(CancellationToken cancellationToken = default);
    Task<SmtpProfileModel> CreateAsync(SmtpProfileWrite request, Guid? userId, CancellationToken cancellationToken = default);
    Task<SmtpProfileModel> UpdateAsync(Guid id, SmtpProfileWrite request, Guid? userId, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task TestAsync(SmtpProfileWrite request, CancellationToken cancellationToken = default);
}

public sealed class SmtpProfileModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string? Username { get; set; }
    public bool HasPassword { get; set; }
    public string FromAddress { get; set; } = string.Empty;
    public string? FromName { get; set; }
    public bool EnableSsl { get; set; }
    public bool IsDefault { get; set; }
}

public sealed class SmtpProfileWrite
{
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = string.Empty;
    public string? FromName { get; set; }
    public bool EnableSsl { get; set; } = true;
    public bool IsDefault { get; set; }
}

public sealed class SmtpProfileOption
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}
