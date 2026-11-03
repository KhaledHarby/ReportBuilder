using Microsoft.EntityFrameworkCore;

namespace ReportBuilder.Infrastructure.Data;

internal static class EmailTemplateSeeder
{
    public const string DeliveryName = "Report delivery";
    public const string PasswordResetName = "Password reset";

    public static async Task EnsureDeliveryTemplateAsync(ReportBuilderDbContext db, CancellationToken cancellationToken)
    {
        var name = DeliveryName;
        var subject = "{reportName}";
        var body = DeliveryHtml;
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            IF NOT EXISTS (SELECT 1 FROM [rb].[EmailTemplates] WHERE [Name] = {name})
            BEGIN
                DECLARE @isDefault bit = CASE
                    WHEN EXISTS (SELECT 1 FROM [rb].[EmailTemplates] WHERE [IsDefault] = 1) THEN 0
                    ELSE 1
                END;
                INSERT INTO [rb].[EmailTemplates]
                    ([Id], [Name], [Subject], [Body], [IsHtml], [IsDefault], [CreatedAt], [UpdatedAt])
                VALUES
                    ({id}, {name}, {subject}, {body}, 1, @isDefault, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            END
            """,
            cancellationToken);
    }

    public static async Task EnsurePasswordResetTemplateAsync(ReportBuilderDbContext db, CancellationToken cancellationToken)
    {
        var name = PasswordResetName;
        var subject = "Reset your password";
        var body = PasswordResetHtml;
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            IF NOT EXISTS (SELECT 1 FROM [rb].[EmailTemplates] WHERE [Name] = {name})
            BEGIN
                INSERT INTO [rb].[EmailTemplates]
                    ([Id], [Name], [Subject], [Body], [IsHtml], [IsDefault], [CreatedAt], [UpdatedAt])
                VALUES
                    ({id}, {name}, {subject}, {body}, 1, 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            END
            """,
            cancellationToken);
    }

    private const string DeliveryHtml = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{reportName}</title>
        </head>
        <body style="margin:0;padding:0;background-color:#f3f6f7;">
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:#f3f6f7;padding:24px 12px;">
        <tr>
        <td align="center">
        <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="width:100%;max-width:600px;background-color:#ffffff;border:1px solid #d7e3e8;">
        <tr>
        <td style="background-color:#005470;padding:22px 28px;">
        <p style="margin:0;font-family:Segoe UI,Tahoma,Arial,sans-serif;font-size:12px;letter-spacing:0.08em;text-transform:uppercase;color:#b7d4de;">Report Builder</p>
        <p style="margin:8px 0 0;font-family:Segoe UI,Tahoma,Arial,sans-serif;font-size:22px;line-height:1.3;font-weight:600;color:#ffffff;">{reportName}</p>
        </td>
        </tr>
        <tr>
        <td style="padding:28px;font-family:Segoe UI,Tahoma,Arial,sans-serif;color:#1c2b33;">
        <p style="margin:0 0 16px;font-size:16px;line-height:1.5;">Hello {displayName},</p>
        <p style="margin:0 0 20px;font-size:15px;line-height:1.6;color:#3d4f58;">Your scheduled report is ready.</p>
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:#f4f7f8;border:1px solid #e3ecef;">
        <tr>
        <td style="padding:12px 16px;font-size:13px;color:#5c6e78;width:140px;">Generated</td>
        <td style="padding:12px 16px;font-size:14px;color:#1c2b33;">{reportDate}</td>
        </tr>
        <tr>
        <td style="padding:12px 16px;font-size:13px;color:#5c6e78;border-top:1px solid #e3ecef;">Username</td>
        <td style="padding:12px 16px;font-size:14px;color:#1c2b33;border-top:1px solid #e3ecef;">{userName}</td>
        </tr>
        <tr>
        <td style="padding:12px 16px;font-size:13px;color:#5c6e78;border-top:1px solid #e3ecef;">Email</td>
        <td style="padding:12px 16px;font-size:14px;color:#1c2b33;border-top:1px solid #e3ecef;">{email}</td>
        </tr>
        </table>
        <p style="margin:20px 0 0;font-size:15px;line-height:1.6;color:#1c2b33;">{fileNote}</p>
        <p style="margin:16px 0 0;font-size:14px;line-height:1.5;word-break:break-all;"><a href="{reportUrl}" style="color:#0f8a90;text-decoration:underline;">{reportUrl}</a></p>
        </td>
        </tr>
        <tr>
        <td style="padding:16px 28px 22px;border-top:1px solid #e6eef1;font-family:Segoe UI,Tahoma,Arial,sans-serif;font-size:12px;line-height:1.5;color:#7b8b93;">Sent by Report Builder.</td>
        </tr>
        </table>
        </td>
        </tr>
        </table>
        </body>
        </html>
        """;

    private const string PasswordResetHtml = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Reset your password</title>
        </head>
        <body style="margin:0;padding:0;background-color:#f3f6f7;">
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:#f3f6f7;padding:24px 12px;">
        <tr>
        <td align="center">
        <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="width:100%;max-width:600px;background-color:#ffffff;border:1px solid #d7e3e8;">
        <tr>
        <td style="background-color:#005470;padding:22px 28px;">
        {logo}
        <p style="margin:0;font-family:Segoe UI,Tahoma,Arial,sans-serif;font-size:12px;letter-spacing:0.08em;text-transform:uppercase;color:#b7d4de;">{studioName}</p>
        <p style="margin:8px 0 0;font-family:Segoe UI,Tahoma,Arial,sans-serif;font-size:22px;line-height:1.3;font-weight:600;color:#ffffff;">Reset your password</p>
        <p style="margin:6px 0 0;font-family:Segoe UI,Tahoma,Arial,sans-serif;font-size:16px;line-height:1.4;color:#d7eef3;" dir="rtl">إعادة تعيين كلمة المرور</p>
        </td>
        </tr>
        <tr>
        <td style="padding:28px;font-family:Segoe UI,Tahoma,Arial,sans-serif;color:#1c2b33;">
        <p style="margin:0 0 12px;font-size:16px;line-height:1.5;">Hello {displayName},</p>
        <p style="margin:0 0 12px;font-size:16px;line-height:1.6;" dir="rtl">مرحباً {displayNameArabic}،</p>
        <p style="margin:0 0 8px;font-size:15px;line-height:1.6;color:#3d4f58;">Use the button below to choose a new password for {userName}. This link works once and expires in {expiresMinutes} minutes.</p>
        <p style="margin:0 0 20px;font-size:15px;line-height:1.6;color:#3d4f58;" dir="rtl">استخدم الزر أدناه لاختيار كلمة مرور جديدة للحساب {userName}. يعمل الرابط مرة واحدة وينتهي خلال {expiresMinutes} دقيقة.</p>
        <p style="margin:0 0 20px;">
        <a href="{resetUrl}" style="display:inline-block;background-color:#005470;color:#ffffff;font-family:Segoe UI,Tahoma,Arial,sans-serif;font-size:15px;font-weight:600;text-decoration:none;padding:12px 22px;">Reset password · إعادة التعيين</a>
        </p>
        <p style="margin:0;font-size:13px;line-height:1.5;color:#5c6e78;word-break:break-all;">{resetUrl}</p>
        </td>
        </tr>
        <tr>
        <td style="padding:16px 28px 22px;border-top:1px solid #e6eef1;font-family:Segoe UI,Tahoma,Arial,sans-serif;font-size:12px;line-height:1.5;color:#7b8b93;">If you did not ask for this, you can ignore this message. Sent by {studioName}.</td>
        </tr>
        </table>
        </td>
        </tr>
        </table>
        </body>
        </html>
        """;
}
