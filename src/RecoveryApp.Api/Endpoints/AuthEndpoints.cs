using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using RecoveryApp.Api.Filters;
using RecoveryApp.Application.Contracts;
using RecoveryApp.Application.Features.Auth;

namespace RecoveryApp.Api.Endpoints;

/// <summary>Token exchange endpoints. These are the only anonymous routes in the API.</summary>
public static class AuthEndpoints
{
    /// <summary>Maps the <c>/api/v1/auth</c> route group.</summary>
    /// <param name="app">The route builder to map onto.</param>
    /// <returns>The mapped group, for further configuration.</returns>
    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/api/v1/auth")
            .WithTags("Auth")
            .AllowAnonymous();

        group.MapPost("/anonymous", CreateAnonymousAsync)
            .WithName("CreateAnonymousUser")
            .WithSummary("Start a guest session with no registration.")
            .WithDescription(
                "Creates an anonymous account, provisions its default settings and returns the same token "
                + "pair as a registered sign-in, so the client can record sessions and sync immediately. "
                + "Upgrade it later with POST /api/v1/auth/link-apple without losing any data.")
            .Produces<AuthTokensResponse>(StatusCodes.Status201Created);

        group.MapPost("/link-apple", LinkAppleAsync)
            .AddEndpointFilter<ValidationFilter<LinkAppleRequest>>()
            .RequireAuthorization()
            .WithName("LinkAppleAccount")
            .WithSummary("Attach an Apple identity to the calling guest account.")
            .WithDescription(
                "Promotes the authenticated guest to a registered account. If the Apple ID already belongs "
                + "to another account, that registered account wins: the guest's sessions and check-ins are "
                + "migrated onto it, the guest is retired, and the returned tokens authenticate the "
                + "surviving account — so the client must replace its stored credentials and user id when "
                + "`merged` is true.")
            .Produces<LinkAppleResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/apple", SignInWithAppleAsync)
            .AddEndpointFilter<ValidationFilter<AppleSignInRequest>>()
            .WithName("SignInWithApple")
            .WithSummary("Exchange a Sign in with Apple identity token for application tokens.")
            .WithDescription(
                "Verifies the identity token, creates the account on first use together with its default "
                + "settings row, and returns a JWT access token plus a single-use refresh token. Token "
                + "verification is stubbed in development: see StubAppleIdentityTokenVerifier.")
            .Produces<AuthTokensResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/refresh", RefreshAsync)
            .AddEndpointFilter<ValidationFilter<RefreshTokenRequest>>()
            .WithName("RefreshTokens")
            .WithSummary("Rotate a refresh token into a new token pair.")
            .WithDescription("The presented refresh token is revoked and replaced, so a stolen token can be used at most once.")
            .Produces<AuthTokensResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return group;
    }

    private static async Task<Created<AuthTokensResponse>> CreateAnonymousAsync(
        ISender sender,
        CancellationToken cancellationToken)
    {
        AuthTokensResponse response = await sender
            .Send(new CreateAnonymousUserCommand(), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created((string?)null, response);
    }

    private static async Task<Ok<LinkAppleResponse>> LinkAppleAsync(
        [FromBody] LinkAppleRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        LinkAppleResponse response = await sender
            .Send(new LinkAppleAccountCommand(request), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(response);
    }

    private static async Task<Ok<AuthTokensResponse>> SignInWithAppleAsync(
        [FromBody] AppleSignInRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        AuthTokensResponse response = await sender
            .Send(new SignInWithAppleCommand(request), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(response);
    }

    private static async Task<Ok<AuthTokensResponse>> RefreshAsync(
        [FromBody] RefreshTokenRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        AuthTokensResponse response = await sender
            .Send(new RefreshTokensCommand(request), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(response);
    }
}
