using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace ReportBuilder.Api;

public sealed class ResponseServerHeaderMiddleware
{
    public const string HostNameHeader = "X-Hostname";

    private readonly RequestDelegate _next;
    private readonly string _server;
    private readonly string _hostName;

    public ResponseServerHeaderMiddleware(RequestDelegate next)
    {
        _next = next;
        _server = BuildHeaderValue();
        _hostName = ResolveHostName();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.Server = _server;
            context.Response.Headers[HostNameHeader] = _hostName;
            return Task.CompletedTask;
        });

        await _next(context);
    }

    private static string ResolveHostName()
    {
        var machine = Environment.MachineName.Trim();
        if (machine.Length > 0)
        {
            return machine;
        }

        try
        {
            var host = Dns.GetHostName()?.Trim();
            return string.IsNullOrWhiteSpace(host) ? "unknown" : host;
        }
        catch (SocketException)
        {
            return "unknown";
        }
    }

    private static string BuildHeaderValue()
    {
        var host = MaskHostName(Environment.MachineName);
        var ip = MaskIpAddress(ResolveHostIp());
        return string.IsNullOrWhiteSpace(ip) ? host : host + "/" + ip;
    }

    private static string ResolveHostIp()
    {
        try
        {
            foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (network.OperationalStatus != OperationalStatus.Up
                    || network.NetworkInterfaceType is NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (var address in network.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily == AddressFamily.InterNetwork
                        && !IPAddress.IsLoopback(address.Address)
                        && !IsLinkLocal(address.Address))
                    {
                        return address.Address.ToString();
                    }
                }
            }
        }
        catch (NetworkInformationException)
        {
            // Fall through to DNS lookup.
        }

        try
        {
            var addresses = Dns.GetHostAddresses(Dns.GetHostName());
            var ipv4 = addresses.FirstOrDefault(item =>
                item.AddressFamily == AddressFamily.InterNetwork
                && !IPAddress.IsLoopback(item)
                && !IsLinkLocal(item));
            if (ipv4 is not null)
            {
                return ipv4.ToString();
            }

            var ipv6 = addresses.FirstOrDefault(item =>
                item.AddressFamily == AddressFamily.InterNetworkV6
                && !IPAddress.IsLoopback(item)
                && !item.IsIPv6LinkLocal);
            return ipv6?.ToString() ?? string.Empty;
        }
        catch (SocketException)
        {
            return string.Empty;
        }
    }

    private static bool IsLinkLocal(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }

    internal static string MaskHostName(string hostName)
    {
        if (string.IsNullOrWhiteSpace(hostName))
        {
            return "****";
        }

        return string.Join('.', hostName.Trim().Split('.').Select(MaskLabel));
    }

    private static string MaskLabel(string label)
    {
        if (label.Length <= 2)
        {
            return label[0] + new string('*', label.Length - 1);
        }

        if (label.Length <= 4)
        {
            return label[0] + new string('*', label.Length - 2) + label[^1];
        }

        return label[..2] + new string('*', label.Length - 4) + label[^2..];
    }

    internal static string MaskIpAddress(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return string.Empty;
        }

        if (IPAddress.TryParse(ip, out var parsed) && parsed.AddressFamily == AddressFamily.InterNetwork)
        {
            var parts = ip.Split('.');
            return parts.Length == 4 ? $"{parts[0]}.*.*.{parts[3]}" : MaskLabel(ip);
        }

        if (IPAddress.TryParse(ip, out parsed) && parsed.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var parts = ip.Split(':');
            if (parts.Length < 3)
            {
                return MaskLabel(ip);
            }

            var masked = new StringBuilder(parts[0]);
            for (var i = 1; i < parts.Length - 1; i++)
            {
                masked.Append(":*");
            }

            return masked.Append(':').Append(parts[^1]).ToString();
        }

        return MaskLabel(ip);
    }
}
