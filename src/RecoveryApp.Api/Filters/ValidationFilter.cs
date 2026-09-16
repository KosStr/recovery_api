using FluentValidation;
using FluentValidation.Results;

namespace RecoveryApp.Api.Filters;

/// <summary>
/// Runs the FluentValidation validator registered for <typeparamref name="TRequest"/> before the
/// handler executes, and answers <c>400</c> with an RFC 9457 problem document listing every field
/// error rather than failing on the first one.
/// </summary>
/// <typeparam name="TRequest">The bound request body type.</typeparam>
/// <param name="validator">The validator for <typeparamref name="TRequest"/>.</param>
public sealed class ValidationFilter<TRequest>(IValidator<TRequest> validator) : IEndpointFilter
    where TRequest : class
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (context.Arguments.OfType<TRequest>().FirstOrDefault() is not { } request)
        {
            return Results.Problem(
                title: "Malformed request",
                detail: $"A {typeof(TRequest).Name} body is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        ValidationResult result = await validator
            .ValidateAsync(request, context.HttpContext.RequestAborted)
            .ConfigureAwait(false);

        if (result.IsValid)
        {
            return await next(context).ConfigureAwait(false);
        }

        Dictionary<string, string[]> errors = result.Errors
            .GroupBy(error => error.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.ErrorMessage).ToArray(),
                StringComparer.Ordinal);

        return Results.ValidationProblem(
            errors,
            title: "One or more validation errors occurred.",
            statusCode: StatusCodes.Status400BadRequest);
    }
}
