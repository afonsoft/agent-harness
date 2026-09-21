using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Taskboard.Server.Serialization;

namespace Taskboard.Server.Middleware;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, code) = MapException(exception);

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/problem+json";

        var problem = new
        {
            type = "https://taskboard.ai/errors",
            title = GetTitle(statusCode),
            status = statusCode,
            detail = exception.Message,
            code,
            instance = httpContext.Request.Path
        };

        await JsonSerializer.SerializeAsync(httpContext.Response.Body, problem, ApiJsonOptions.Default, cancellationToken);
        return true;
    }

    private static (int StatusCode, string Code) MapException(Exception exception)
    {
        if (exception is DomainException domainException)
        {
            var statusCode = domainException.Code switch
            {
                var c when c == TaskboardDomainErrorCodes.VersionConflict
                    || c == TaskboardDomainErrorCodes.InvalidPipelineState => 409,
                var c when c == TaskboardDomainErrorCodes.AgentNotEligible => 422,
                _ => 400
            };

            return (statusCode, domainException.Code);
        }

        if (exception is Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            return (409, TaskboardDomainErrorCodes.VersionConflict);
        }

        if (exception is ArgumentException or ArgumentNullException)
        {
            return (400, TaskboardDomainErrorCodes.InvalidValue);
        }

        return (500, "INTERNAL_ERROR");
    }

    private static string GetTitle(int statusCode) => statusCode switch
    {
        400 => "Bad Request",
        404 => "Not Found",
        405 => "Method Not Allowed",
        409 => "Conflict",
        500 => "Internal Server Error",
        _ => "Error"
    };
}
