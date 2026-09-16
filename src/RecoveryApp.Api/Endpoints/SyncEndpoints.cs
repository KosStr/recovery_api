using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using RecoveryApp.Api.Filters;
using RecoveryApp.Application.Common;
using RecoveryApp.Application.Contracts;
using RecoveryApp.Application.Features.Sync;

namespace RecoveryApp.Api.Endpoints;

/// <summary>The two-way sync protocol: push local changes up, pull server deltas down.</summary>
public static class SyncEndpoints
{
    /// <summary>Maps the <c>/api/v1/sync</c> route group.</summary>
    /// <param name="app">The route builder to map onto.</param>
    /// <returns>The mapped group, for further configuration.</returns>
    public static RouteGroupBuilder MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/api/v1/sync")
            .WithTags("Sync")
            .RequireAuthorization();

        group.MapPost("/push", PushAsync)
            .AddEndpointFilter<ValidationFilter<SyncPushRequest>>()
            .AddEndpointFilter<IdempotencyFilter>()
            .WithName("PushSyncChanges")
            .WithSummary("Upload a batch of offline changes.")
            .WithDescription(
                "Upserts sessions and energy check-ins keyed by their client generated UUIDv7. Conflicts are "
                + "resolved last-writer-wins on updatedAt: a strictly newer client revision overwrites the "
                + "server row, anything else is reported back in `conflicts` and left alone. That makes a "
                + "replayed batch a no-op; sending an Idempotency-Key additionally replays the original "
                + $"response body. At most {SyncLimits.MaxPushBatchSize} rows per entity type.")
            .Produces<SyncPushResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/pull", PullAsync)
            .WithName("PullSyncChanges")
            .WithSummary("Download everything that changed since a cursor.")
            .WithDescription(
                "Returns sessions, energy check-ins and the settings row whose revision stamp is newer than "
                + "`since`, tombstones included, oldest revision first. Feed `nextCursor` back into the next "
                + "call and keep going while `hasMore` is true. Omit `since` to bootstrap a device.")
            .Produces<SyncPullResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return group;
    }

    private static async Task<Ok<SyncPushResponse>> PushAsync(
        [FromBody] SyncPushRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        SyncPushResponse response = await sender
            .Send(new PushSyncChangesCommand(request), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(response);
    }

    private static async Task<Ok<SyncPullResponse>> PullAsync(
        ISender sender,
        CancellationToken cancellationToken,
        [FromQuery] DateTimeOffset? since = null,
        [FromQuery] int limit = SyncLimits.DefaultPullPageSize)
    {
        SyncPullResponse response = await sender
            .Send(new PullSyncChangesQuery(since ?? DateTimeOffset.UnixEpoch, limit), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(response);
    }
}
