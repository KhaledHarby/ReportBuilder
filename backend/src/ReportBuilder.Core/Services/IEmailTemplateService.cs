namespace ReportBuilder.Core.Services;

public interface IEmailTemplateService
{
    Task<IReadOnlyList<EmailTemplateModel>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EmailTemplateOption>> ListOptionsAsync(CancellationToken cancellationToken = default);
    Task<EmailTemplateModel> CreateAsync(EmailTemplateWrite request, CancellationToken cancellationToken = default);
    Task<EmailTemplateModel> UpdateAsync(Guid id, EmailTemplateWrite request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class EmailTemplateModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsHtml { get; set; }
    public bool IsDefault { get; set; }
}

public sealed class EmailTemplateWrite
{
    public string Name { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsHtml { get; set; }
    public bool IsDefault { get; set; }
}

public sealed class EmailTemplateOption
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsHtml { get; set; }
}
