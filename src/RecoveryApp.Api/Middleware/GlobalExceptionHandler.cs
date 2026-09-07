using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecoveryApp.Application.Common;

namespace RecoveryApp.Api.Middleware;

/// <summary>
/// Translates the exceptions the application layer raises into RFC 9457 problem documents, so the
/// mobile client sees one predictable error envelope instead of a stack trace.
/// </summary>
/// <param name="problemDetailsService">Writes the negotiated problem document.</param>
/// <param name="logger">Logger.</param>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        (int status, string title, string? detail) = Map(exception);

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception while processing {Method} {Path}.",
                httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogInformation("Request failed with {Status}: {Title}", status, title);
        }

        httpContext.Response.StatusCode = status;

        ProblemDetails problem = new()
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}",
        };

        if (exception is ValidationException validation)
        {
            problem.Extensions["errors"] = validation.Errors
                .GroupBy(error => error.PropertyName, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.ErrorMessage).ToArray(),
                    StringComparer.Ordinal);
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        }).ConfigureAwait(false);
    }

    private static (int Status, string Title, string? Detail) Map(Exception exception) => exception switch
    {
        ValidationException => (StatusCodes.Status400BadRequest, "One or more validation errors occurred.", null),
        AuthenticationFailedException => (StatusCodes.Status401Unauthorized, "Authentication failed.", exception.Message),
        NotFoundException => (StatusCodes.Status404NotFound, "Resource not found.", exception.Message),
        IdempotencyConflictException => (StatusCodes.Status409Conflict, "Idempotency key conflict.", exception.Message),
        DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "The resource was modified concurrently.", null),
        BadHttpRequestException bad => (bad.StatusCode, "Malformed request.", bad.Message),
        OperationCanceledException => (StatusCodesExtras.ClientClosedRequest, "The request was cancelled.", null),
        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", null),
    };
}

/// <summary>Status codes the framework does not define as constants.</summary>
internal static class StatusCodesExtras
{
    /// <summary>Nginx's non-standard code for a client that hung up mid-request.</summary>
    public const int ClientClosedRequest = 499;
}
