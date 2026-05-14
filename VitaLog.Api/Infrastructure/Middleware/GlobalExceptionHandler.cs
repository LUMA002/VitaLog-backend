using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace VitaLog.Api.Infrastructure.Middleware;

public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception. TraceId: {TraceId}", httpContext.TraceIdentifier);

        var (status, title, detail) = exception switch
        {
            UnauthorizedAccessException e => (
                StatusCodes.Status403Forbidden,
                "Forbidden",
                e.Message),

            ValidationException e => (
                StatusCodes.Status400BadRequest,
                "Validation failed",
                "The requested operation could not be completed."),
                // string.Join(" | ", e.Errors.Select(x => $"{x.PropertyName}: {x.ErrorMessage}"))),

            InvalidOperationException e => (
                StatusCodes.Status400BadRequest,
                "Invalid operation",
                e.Message),

            KeyNotFoundException e => (
                StatusCodes.Status404NotFound,
                "Not found",
                "The requested resource does not exist."),
                // e.Message),

            _ => (
                StatusCodes.Status500InternalServerError,
                "Server error",
                "An unexpected error occurred.")
        };

        var problem = new ProblemDetails
        {
            Title = title,
            Status = status,
            Detail = detail,
            Instance = httpContext.Request.Path
        };

        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        httpContext.Response.StatusCode = status;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }
}