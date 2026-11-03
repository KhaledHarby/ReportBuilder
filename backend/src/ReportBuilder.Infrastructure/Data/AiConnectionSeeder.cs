using Microsoft.EntityFrameworkCore;
using ReportBuilder.Core.Entities;
using ReportBuilder.Infrastructure.Options;

namespace ReportBuilder.Infrastructure.Data;

internal static class AiConnectionSeeder
{
    public static async Task EnsureFromOptionsAsync(
        ReportBuilderDbContext db,
        OpenAIOptions options,
        CancellationToken cancellationToken)
    {
        if (await db.AiConnections.AnyAsync(cancellationToken))
        {
            return;
        }

        var apiKey = options.ApiKey?.Trim() ?? string.Empty;
        if (apiKey.Length == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        db.AiConnections.Add(new AiConnection
        {
            Id = Guid.NewGuid(),
            Name = "OpenAI",
            BaseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
                ? "https://api.openai.com/v1/"
                : options.BaseUrl.Trim(),
            Model = string.IsNullOrWhiteSpace(options.Model) ? "gpt-4o-mini" : options.Model.Trim(),
            ApiKey = apiKey,
            IsEnabled = true,
            IsDefault = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
