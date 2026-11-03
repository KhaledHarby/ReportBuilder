using System.Net.Mail;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Query;
using ReportBuilder.Core.Services;
using ReportBuilder.Infrastructure.Data;

namespace ReportBuilder.Infrastructure.Services;

public sealed class SmtpProfileService : ISmtpProfileService
{
    private readonly ReportBuilderDbContext _db;

    public SmtpProfileService(ReportBuilderDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<SmtpProfileModel>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.SmtpProfiles
            .AsNoTracking()
            .OrderByDescending(item => item.IsDefault)
            .ThenBy(item => item.Name)
            .ToListAsync(cancellationToken);
        return rows.Select(ToModel).ToList();
    }

    public async Task<IReadOnlyList<SmtpProfileOption>> ListOptionsAsync(CancellationToken cancellationToken = default)
    {
        return await _db.SmtpProfiles
            .AsNoTracking()
            .OrderByDescending(item => item.IsDefault)
            .ThenBy(item => item.Name)
            .Select(item => new SmtpProfileOption
            {
                Id = item.Id,
                Name = item.Name,
                IsDefault = item.IsDefault
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<SmtpProfileModel> CreateAsync(
        SmtpProfileWrite request,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        var profile = new SmtpProfile
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        Apply(profile, request, keepPassword: false);
        var any = await _db.SmtpProfiles.AnyAsync(cancellationToken);
        if (!any || request.IsDefault)
        {
            await ClearDefaultAsync(cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            profile.IsDefault = true;
        }

        _db.SmtpProfiles.Add(profile);
        await _db.SaveChangesAsync(cancellationToken);
        return ToModel(profile);
    }

    public async Task<SmtpProfileModel> UpdateAsync(
        Guid id,
        SmtpProfileWrite request,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        var profile = await _db.SmtpProfiles.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new ReportQueryException("SMTP setting was not found.");
        Apply(profile, request, keepPassword: true);
        if (request.IsDefault)
        {
            await ClearDefaultAsync(cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            profile.IsDefault = true;
        }
        else if (profile.IsDefault)
        {
            var other = await _db.SmtpProfiles.AnyAsync(item => item.Id != id && item.IsDefault, cancellationToken);
            if (!other)
            {
                profile.IsDefault = true;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return ToModel(profile);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var profile = await _db.SmtpProfiles.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new ReportQueryException("SMTP setting was not found.");
        var wasDefault = profile.IsDefault;
        _db.SmtpProfiles.Remove(profile);
        await _db.SaveChangesAsync(cancellationToken);
        if (wasDefault)
        {
            var next = await _db.SmtpProfiles
                .OrderBy(item => item.Name)
                .FirstOrDefaultAsync(cancellationToken);
            if (next is not null)
            {
                next.IsDefault = true;
                next.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
    }

    public async Task TestAsync(SmtpProfileWrite request, CancellationToken cancellationToken = default)
    {
        var host = request.Host.Trim();
        var port = request.Port;
        if (host.Length == 0 || port is < 1 or > 65535)
        {
            throw new ReportQueryException("SMTP host and port are required.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, timeout.Token);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or TimeoutException)
        {
            throw new ReportQueryException($"Could not reach {host}:{port}.");
        }
    }

    private async Task ClearDefaultAsync(CancellationToken cancellationToken)
    {
        var current = await _db.SmtpProfiles.Where(item => item.IsDefault).ToListAsync(cancellationToken);
        foreach (var item in current)
        {
            item.IsDefault = false;
        }
    }

    private static void Apply(SmtpProfile profile, SmtpProfileWrite request, bool keepPassword)
    {
        var name = request.Name.Trim();
        var host = request.Host.Trim();
        var from = request.FromAddress.Trim();
        if (name.Length == 0 || host.Length == 0 || from.Length == 0)
        {
            throw new ReportQueryException("Name, host, and from address are required.");
        }

        if (request.Port is < 1 or > 65535)
        {
            throw new ReportQueryException("SMTP port must be between 1 and 65535.");
        }

        try
        {
            _ = new MailAddress(from);
        }
        catch (FormatException)
        {
            throw new ReportQueryException("From address is not a valid email.");
        }

        profile.Name = name;
        profile.Host = host;
        profile.Port = request.Port;
        profile.Username = BlankToNull(request.Username);
        if (!keepPassword || !string.IsNullOrWhiteSpace(request.Password))
        {
            profile.Password = BlankToNull(request.Password);
        }

        profile.FromAddress = from;
        profile.FromName = BlankToNull(request.FromName);
        profile.EnableSsl = request.EnableSsl;
        profile.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static SmtpProfileModel ToModel(SmtpProfile profile)
        => new()
        {
            Id = profile.Id,
            Name = profile.Name,
            Host = profile.Host,
            Port = profile.Port,
            Username = profile.Username,
            HasPassword = !string.IsNullOrEmpty(profile.Password),
            FromAddress = profile.FromAddress,
            FromName = profile.FromName,
            EnableSsl = profile.EnableSsl,
            IsDefault = profile.IsDefault
        };

    private static string? BlankToNull(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
