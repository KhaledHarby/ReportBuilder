using Microsoft.EntityFrameworkCore;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Query;
using ReportBuilder.Core.Services;
using ReportBuilder.Infrastructure.Data;

namespace ReportBuilder.Infrastructure.Services;

public sealed class EmailTemplateService : IEmailTemplateService
{
    private const int MaxBodyLength = 20_000;
    private readonly ReportBuilderDbContext _db;

    public EmailTemplateService(ReportBuilderDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<EmailTemplateModel>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.EmailTemplates
            .AsNoTracking()
            .OrderByDescending(item => item.IsDefault)
            .ThenBy(item => item.Name)
            .ToListAsync(cancellationToken);
        return rows.Select(ToModel).ToList();
    }

    public async Task<IReadOnlyList<EmailTemplateOption>> ListOptionsAsync(CancellationToken cancellationToken = default)
    {
        return await _db.EmailTemplates
            .AsNoTracking()
            .OrderByDescending(item => item.IsDefault)
            .ThenBy(item => item.Name)
            .Select(item => new EmailTemplateOption
            {
                Id = item.Id,
                Name = item.Name,
                IsDefault = item.IsDefault,
                IsHtml = item.IsHtml
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<EmailTemplateModel> CreateAsync(EmailTemplateWrite request, CancellationToken cancellationToken = default)
    {
        var template = new EmailTemplate
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        Apply(template, request);
        var any = await _db.EmailTemplates.AnyAsync(cancellationToken);
        if (!any || request.IsDefault)
        {
            await ClearDefaultAsync(cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            template.IsDefault = true;
        }

        _db.EmailTemplates.Add(template);
        await _db.SaveChangesAsync(cancellationToken);
        return ToModel(template);
    }

    public async Task<EmailTemplateModel> UpdateAsync(
        Guid id,
        EmailTemplateWrite request,
        CancellationToken cancellationToken = default)
    {
        var template = await _db.EmailTemplates.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new ReportQueryException("Email template was not found.");
        Apply(template, request);
        if (request.IsDefault)
        {
            await ClearDefaultAsync(cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            template.IsDefault = true;
        }
        else if (template.IsDefault)
        {
            var other = await _db.EmailTemplates.AnyAsync(item => item.Id != id && item.IsDefault, cancellationToken);
            if (!other)
            {
                template.IsDefault = true;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return ToModel(template);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await _db.EmailTemplates.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new ReportQueryException("Email template was not found.");
        var wasDefault = template.IsDefault;
        _db.EmailTemplates.Remove(template);
        await _db.SaveChangesAsync(cancellationToken);
        if (wasDefault)
        {
            var next = await _db.EmailTemplates.OrderBy(item => item.Name).FirstOrDefaultAsync(cancellationToken);
            if (next is not null)
            {
                next.IsDefault = true;
                next.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
    }

    private async Task ClearDefaultAsync(CancellationToken cancellationToken)
    {
        var current = await _db.EmailTemplates.Where(item => item.IsDefault).ToListAsync(cancellationToken);
        foreach (var item in current)
        {
            item.IsDefault = false;
        }
    }

    private static void Apply(EmailTemplate template, EmailTemplateWrite request)
    {
        var name = request.Name.Trim();
        var subject = request.Subject.Trim();
        var body = request.Body ?? string.Empty;
        if (name.Length == 0 || subject.Length == 0 || body.Trim().Length == 0)
        {
            throw new ReportQueryException("Name, subject, and body are required.");
        }

        if (body.Length > MaxBodyLength)
        {
            throw new ReportQueryException("The email body is too long.");
        }

        template.Name = name;
        template.Subject = subject;
        template.Body = body;
        template.IsHtml = request.IsHtml;
        template.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static EmailTemplateModel ToModel(EmailTemplate template)
        => new()
        {
            Id = template.Id,
            Name = template.Name,
            Subject = template.Subject,
            Body = template.Body,
            IsHtml = template.IsHtml,
            IsDefault = template.IsDefault
        };
}
