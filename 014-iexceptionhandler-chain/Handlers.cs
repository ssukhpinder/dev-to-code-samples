using Microsoft.AspNetCore.Diagnostics;

namespace ExceptionPipeline;

// First link in the chain: knows the domain exceptions and their status codes.
// Returns false for anything it doesn't recognize, which passes the exception
// to the next registered handler.
public sealed class DomainExceptionHandler(IProblemDetailsService problemDetails)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken ct)
    {
        (int status, string title) = exception switch
        {
            ProductNotFoundException => (StatusCodes.Status404NotFound, "Product not found"),
            StaleInventoryException  => (StatusCodes.Status409Conflict, "Inventory changed"),
            _ => (0, "")
        };

        if (status == 0)
            return false; // not ours — let the next handler look at it

        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails =
            {
                Status = status,
                Title = title,
                Detail = exception.Message // domain messages are written to be shown
            }
        });
    }
}

// Last link: catches everything else. Logs the real exception, returns a
// deliberately vague 500 — internals stay on the server.
public sealed class UnhandledExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<UnhandledExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken ct)
    {
        logger.LogError(exception,
            "Unhandled exception for {Method} {Path}, traceId {TraceId}",
            context.Request.Method, context.Request.Path,
            System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails =
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Something went wrong on our side.",
                Detail = "The error has been logged. Quote the traceId if you contact support."
            }
        });
    }
}
