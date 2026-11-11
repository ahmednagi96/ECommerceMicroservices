using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Storage;

namespace Basket.API.ErrorHandling;

public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // هل الخطأ سببه إن الداتابيز مش متاحة؟
        var isDatabaseDown = exception is SqlException or RetryLimitExceededException;

        int status;
        string title;

        if (isDatabaseDown)
        {
            status = StatusCodes.Status503ServiceUnavailable;
            title = "Service temporarily unavailable";
            logger.LogWarning(exception, "Database is unavailable");
        }
        else
        {
            status = StatusCodes.Status500InternalServerError;
            title = "An unexpected error occurred";
            logger.LogError(exception, "Unhandled exception");
        }

        httpContext.Response.StatusCode = status;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = { Status = status, Title = title }
        });
    }
}