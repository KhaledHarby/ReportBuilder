using System.Text.Json;
using System.Text.RegularExpressions;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Services;

namespace ReportBuilder.Infrastructure.Services;

internal sealed record MailActivity(
    Guid? ActorUserId = null,
    string? ActorUserName = null,
    Guid? TargetUserId = null,
    string? TargetUserName = null,
    Guid? EntityId = null,
    string? EntityName = null);

internal static class SmtpMailSender
{
    public static async Task SendAsync(
        SmtpProfile profile,
        string to,
        string subject,
        string body,
        string? fileName,
        byte[]? content,
        string? contentType,
        CancellationToken cancellationToken,
        IActivityLog activity,
        MailActivity? context = null,
        bool isHtml = false,
        byte[]? logo = null,
        string? logoContentType = null)
    {
        try
        {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(profile.FromName ?? string.Empty, profile.FromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;

        var builder = new BodyBuilder();
        if (isHtml)
        {
            builder.HtmlBody = body;
            builder.TextBody = Regex.Replace(body, "<[^>]+>", " ");
            if (logo is { Length: > 0 } && body.Contains("cid:report-logo", StringComparison.Ordinal))
            {
                var type = ContentType.Parse(string.IsNullOrWhiteSpace(logoContentType) ? "image/png" : logoContentType);
                var image = (MimePart)builder.LinkedResources.Add("report-logo", logo, type);
                image.ContentId = "report-logo";
                image.ContentTransferEncoding = ContentEncoding.Base64;
                image.FileName = null;
                image.ContentType.Name = null;
                image.ContentDisposition = new ContentDisposition(ContentDisposition.Inline);
            }
        }
        else
        {
            builder.TextBody = body;
        }
        if (content is { Length: > 0 } && !string.IsNullOrWhiteSpace(fileName))
        {
            builder.Attachments.Add(fileName, content, ContentType.Parse(contentType ?? "application/octet-stream"));
        }

        message.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        client.Timeout = 30_000;
        var socket = profile.EnableSsl
            ? profile.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls
            : SecureSocketOptions.None;
            await client.ConnectAsync(profile.Host, profile.Port, socket, cancellationToken);
            if (!string.IsNullOrWhiteSpace(profile.Username))
            {
                await client.AuthenticateAsync(profile.Username, profile.Password ?? string.Empty, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            await activity.WriteAsync(
                Entry(context, to, subject, fileName, profile.FromAddress, error: null),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await activity.WriteAsync(
                Entry(context, to, subject, fileName, profile.FromAddress, ex.Message),
                CancellationToken.None);
            throw;
        }
    }

    private static ActivityEvent Entry(
        MailActivity? context,
        string to,
        string subject,
        string? fileName,
        string? from,
        string? error)
    {
        var failed = !string.IsNullOrWhiteSpace(error);
        var title = string.IsNullOrWhiteSpace(subject) ? "(no subject)" : subject.Trim();
        return new ActivityEvent
        {
            Level = failed ? ActivityLevels.Error : ActivityLevels.Information,
            Category = ActivityCategories.Mail,
            Action = failed ? "email.failed" : "email.sent",
            Message = failed
                ? $"Failed to send \"{title}\" to {to}."
                : $"Sent \"{title}\" to {to}.",
            ActorUserId = context?.ActorUserId,
            ActorUserName = context?.ActorUserName,
            TargetUserId = context?.TargetUserId,
            TargetUserName = context?.TargetUserName ?? to,
            EntityType = context?.EntityId is null ? null : "Report",
            EntityId = context?.EntityId,
            EntityName = context?.EntityName,
            Details = JsonSerializer.Serialize(new
            {
                to,
                subject = title,
                from,
                attachment = string.IsNullOrWhiteSpace(fileName) ? null : fileName,
                error
            })
        };
    }
}
