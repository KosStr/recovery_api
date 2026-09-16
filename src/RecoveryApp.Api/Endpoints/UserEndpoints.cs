using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using RecoveryApp.Api.Filters;
using RecoveryApp.Application.Contracts;
using RecoveryApp.Application.Features.Settings;

namespace RecoveryApp.Api.Endpoints;

/// <summary>Per-user configuration endpoints.</summary>
public static class UserEndpoints
{
    /// <summary>Maps the <c>/api/v1/user</c> route group.</summary>
    /// <param name="app">The route builder to map onto.</param>
    /// <returns>The mapped group, for further configuration.</returns>
    public static RouteGroupBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/api/v1/user")
            .WithTags("User")
            .RequireAuthorization();

        group.MapGet("/settings", GetSettingsAsync)
            .WithName("GetUserSettings")
            .WithSummary("Read the caller's sleep and energy configuration.")
            .Produces<UserSettingsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/settings", UpdateSettingsAsync)
            .AddEndpointFilter<ValidationFilter<UpdateUserSettingsRequest>>()
            .WithName("UpdateUserSettings")
            .WithSummary("Replace the caller's sleep and energy configuration.")
            .WithDescription(
                "A full replacement, not a patch: send the complete document. The new revision stamp is "
                + "returned so the client can reconcile it against its local copy, and the row is picked up "
                + "by the next sync pull on the user's other devices.")
            .Produces<UserSettingsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return group;
    }

    private static async Task<Ok<UserSettingsResponse>> GetSettingsAsync(
        ISender sender,
        CancellationToken cancellationToken)
    {
        UserSettingsResponse response = await sender
            .Send(new GetUserSettingsQuery(), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(response);
    }

    private static async Task<Ok<UserSettingsResponse>> UpdateSettingsAsync(
        [FromBody] UpdateUserSettingsRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        UserSettingsResponse response = await sender
            .Send(new UpdateUserSettingsCommand(request), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(response);
    }
}
