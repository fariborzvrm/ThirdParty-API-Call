using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace InquiryService.Api.Infrastructure;

/// <summary>
/// Central exception handler: turns any unhandled exception into a clean 500 ProblemDetails
/// response without leaking stack traces or internal details to the caller.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        if (exception is OperationCanceledException)
        {
            // Client disconnected or the request was aborted; there is nobody to answer.
            logger.LogInformation("Request {Method} {Path} was cancelled", httpContext.Request.Method, httpContext.Request.Path);
            return true;
        }

        logger.LogError(exception,
            "Unhandled exception while processing {Method} {Path}",
            httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7807",
            Title = "An unexpected error occurred.",
            Status = StatusCodes.Status500InternalServerError,
            Detail = "The service failed to process the request.",
            Instance = httpContext.Request.Path
        }, ct);

        return true;
    }
}