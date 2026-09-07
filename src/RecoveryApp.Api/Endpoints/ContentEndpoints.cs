using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using RecoveryApp.Application.Contracts;
using RecoveryApp.Application.Features.Content;
using RecoveryApp.Domain.Enums;

namespace RecoveryApp.Api.Endpoints;

/// <summary>Read-only audio catalogue endpoints.</summary>
public static class ContentEndpoints
{
    private const int CatalogMaxAgeSeconds = 300;

    /// <summary>Maps the <c>/api/v1/content</c> route group.</summary>
    /// <param name="app">The route builder to map onto.</param>
    /// <returns>The mapped group, for further configuration.</returns>
    public static RouteGroupBuilder MapContentEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/api/v1/content")
            .WithTags("Content")
            .RequireAuthorization();

        group.MapGet("/tracks", GetTracksAsync)
            .WithName("GetAudioTracks")
            .WithSummary("List the audio catalogue.")
            .WithDescription(
                "Returns the catalogue, optionally narrowed to one category. The response carries a strong "
                + "ETag and a Cache-Control max-age; send the ETag back as If-None-Match and the endpoint "
                + "answers 304 without reading a single row, which is what keeps the client's cold start cheap.")
            .Produces<AudioTrackCatalogResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return group;
    }

    private static async Task<Results<Ok<AudioTrackCatalogResponse>, StatusCodeHttpResult>> GetTracksAsync(
        HttpContext http,
        ISender sender,
        CancellationToken cancellationToken,
        [FromQuery] AudioCategory? category = null,
        [FromQuery] bool includePremium = true)
    {
        string? ifNoneMatch = http.Request.Headers.IfNoneMatch.ToString();

        AudioTrackCatalogResult result = await sender
            .Send(new GetAudioTracksQuery(category, includePremium, ifNoneMatch), cancellationToken)
            .ConfigureAwait(false);

        http.Response.Headers.ETag = result.ETag;
        http.Response.Headers.CacheControl = $"private, max-age={CatalogMaxAgeSeconds}";
        http.Response.Headers[HeaderNames.Vary] = HeaderNames.Authorization;

        return result.Catalog is null
            ? TypedResults.StatusCode(StatusCodes.Status304NotModified)
            : TypedResults.Ok(result.Catalog);
    }
}
