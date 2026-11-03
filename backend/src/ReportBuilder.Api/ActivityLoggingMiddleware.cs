using System.Diagnostics;
using ReportBuilder.Core.Identity;
using ReportBuilder.Core.Services;

namespace ReportBuilder.Api;

public sealed class ActivityLoggingMiddleware
{
    private readonly RequestDelegate _next;

    public ActivityLoggingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IActivityLog log, ICurrentUser currentUser)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (ShouldSkip(context.Request.Method, path))
        {
            await _next(context);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            await _next(context);
        }
        finally
        {
            var status = context.Response.StatusCode;
            if (status >= 400 && !IsLoginFailure(context.Request.Method, path, status))
            {
                var duration = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                var level = status >= 500 ? ActivityLevels.Error : ActivityLevels.Warning;
                await log.WriteAsync(
                    new ReportBuilder.Core.Services.ActivityEvent
                    {
                        Level = level,
                        Category = ActivityCategories.Http,
                        Action = "http.request",
                        Message = $"{context.Request.Method} {path} returned {status}.",
                        ActorUserId = currentUser.UserId,
                        ActorUserName = currentUser.UserName,
                        HttpMethod = context.Request.Method,
                        Path = path,
                        StatusCode = status,
                        DurationMs = duration,
                        IpAddress = ClientIp(context),
                        CorrelationId = context.TraceIdentifier
                    });
            }
        }
    }

    private static bool ShouldSkip(string method, string path)
    {
        if (path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return HttpMethods.IsGet(method)
            && path.StartsWith("/api/logs", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLoginFailure(string method, string path, int status) =>
        HttpMethods.IsPost(method)
        && path.StartsWith("/api/auth/login", StringComparison.OrdinalIgnoreCase)
        && status is 401 or 403;

    private static string? ClientIp(HttpContext context)
    {
        var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            return forwarded.Split(',')[0].Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString();
    }
}
