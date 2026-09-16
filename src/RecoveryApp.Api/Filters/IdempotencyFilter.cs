using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecoveryApp.Application.Abstractions;
using RecoveryApp.Application.Common;
using RecoveryApp.Domain.Entities;

namespace RecoveryApp.Api.Filters;

/// <summary>
/// Makes a mutating endpoint safe to retry. The first call with a given <c>Idempotency-Key</c> runs
/// normally and its response is stored; a replay of the same key returns the stored response without
/// touching the data again, and a replay carrying a different body is rejected with <c>409</c>.
/// </summary>
/// <remarks>
/// The sync push endpoint is already convergent by construction — last-writer-wins on a client
/// generated key means re-applying a batch is a no-op — so this filter is the belt to that braces:
/// it also guarantees the client sees the same counters and conflict list on every retry.
/// </remarks>
/// <param name="jsonOptions">The serializer settings the endpoint itself would have used.</param>
public sealed class IdempotencyFilter(IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions) : IEndpointFilter
{
    /// <summary>The request header carrying the key.</summary>
    public const string HeaderName = "Idempotency-Key";

    private const int MaxKeyLength = 128;

    private readonly JsonSerializerOptions _serializerOptions = jsonOptions.Value.SerializerOptions;

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        HttpContext http = context.HttpContext;

        if (!http.Request.Headers.TryGetValue(HeaderName, out var header) || string.IsNullOrWhiteSpace(header))
        {
            return await next(context).ConfigureAwait(false);
        }

        string key = header.ToString().Trim();

        if (key.Length > MaxKeyLength)
        {
            return Results.Problem(
                title: "Invalid idempotency key",
                detail: $"'{HeaderName}' must be at most {MaxKeyLength} characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        ICurrentUser currentUser = http.RequestServices.GetRequiredService<ICurrentUser>();
        IAppDbContext db = http.RequestServices.GetRequiredService<IAppDbContext>();
        TimeProvider timeProvider = http.RequestServices.GetRequiredService<TimeProvider>();

        Guid userId = currentUser.RequireUserId();
        string endpoint = http.Request.Path.Value ?? string.Empty;
        string requestHash = HashArguments(context.Arguments);

        IdempotencyRecord? existing = await db.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.UserId == userId && r.Key == key, http.RequestAborted)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            if (!string.Equals(existing.Endpoint, endpoint, StringComparison.Ordinal)
                || !string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
            {
                throw new IdempotencyConflictException(
                    $"'{HeaderName}' {key} was already used for a different request.");
            }

            http.Response.Headers["Idempotency-Replayed"] = "true";
            return Results.Text(existing.ResponseBody, "application/json", Encoding.UTF8, existing.StatusCode);
        }

        object? result = await next(context).ConfigureAwait(false);

        if (result is IValueHttpResult { Value: { } value })
        {
            db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                UserId = userId,
                Key = key,
                Endpoint = endpoint,
                RequestHash = requestHash,
                ResponseBody = JsonSerializer.Serialize(value, _serializerOptions),
                StatusCode = (result as IStatusCodeHttpResult)?.StatusCode ?? StatusCodes.Status200OK,
                CreatedAt = timeProvider.GetUtcNow(),
            });

            await db.SaveChangesAsync(http.RequestAborted).ConfigureAwait(false);
        }

        return result;
    }

    private string HashArguments(IList<object?> arguments)
    {
        // Hashing the bound arguments rather than re-reading the request stream keeps the body
        // buffered only once, in the model binder.
        object?[] payload = [.. arguments.Where(IsPayload)];

        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, _serializerOptions))));
    }

    private static bool IsPayload(object? argument) => argument is not (
        HttpContext or HttpRequest or HttpResponse or ClaimsPrincipal or CancellationToken);
}
