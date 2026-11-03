using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ReportBuilder.Core.Identity;
using ReportBuilder.Core.Query;
using ReportBuilder.Core.Services;

namespace ReportBuilder.Api;

public sealed class ReportExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        await WriteLogAsync(httpContext, exception, cancellationToken);

        if (exception is IdentityException identityException)
        {
            httpContext.Response.StatusCode = identityException.StatusCode;
            await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Title = "Identity",
                Detail = identityException.Message,
                Status = identityException.StatusCode
            }, cancellationToken);
            return true;
        }

        if (exception is ReportAiException aiException)
        {
            httpContext.Response.StatusCode = aiException.StatusCode;
            await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Title = "Copilot",
                Detail = aiException.Message,
                Status = aiException.StatusCode
            }, cancellationToken);
            return true;
        }

        if (exception is not ReportQueryException queryException)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Title = "Invalid report",
            Detail = queryException.Message,
            Status = StatusCodes.Status400BadRequest
        }, cancellationToken);

        return true;
    }

    private static async Task WriteLogAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var path = httpContext.Request.Path.Value ?? string.Empty;
        if (exception is IdentityException
            && path.StartsWith("/api/auth/login", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (exception is ReportAiException)
        {
            return;
        }

        var log = httpContext.RequestServices.GetService<IActivityLog>();
        if (log is null)
        {
            return;
        }

        var current = httpContext.RequestServices.GetService<ICurrentUser>();
        var status = exception switch
        {
            IdentityException identity => identity.StatusCode,
            ReportAiException ai => ai.StatusCode,
            ReportQueryException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };
        var category = exception is ReportAiException
            ? ActivityCategories.Ai
            : exception is ReportQueryException
                ? ActivityCategories.Reports
                : exception is IdentityException
                    ? ActivityCategories.Auth
                    : ActivityCategories.Http;
        var action = exception is ReportAiException ? "ai.failed" : "http.error";
        await log.WriteAsync(
            new ActivityEvent
            {
                Level = status >= 500 ? ActivityLevels.Error : ActivityLevels.Warning,
                Category = category,
                Action = action,
                Message = exception.Message,
                ActorUserId = current?.UserId,
                ActorUserName = current?.UserName,
                HttpMethod = httpContext.Request.Method,
                Path = path,
                StatusCode = status,
                IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
                CorrelationId = httpContext.TraceIdentifier,
                Details = $"{{\"type\":\"{exception.GetType().Name}\"}}"
            },
            cancellationToken);
    }
}
