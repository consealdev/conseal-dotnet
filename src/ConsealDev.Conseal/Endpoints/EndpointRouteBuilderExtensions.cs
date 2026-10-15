using ConsealDev.Conseal.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ConsealDev.Conseal.Endpoints;

/// <summary>Maps the two route groups of the Conseal Sync contract.</summary>
public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the sync routes of <c>openapi.yaml</c> under <paramref name="prefix"/>: <c>GET /</c> for the collections
    /// and limits, <c>GET</c> and <c>POST /{collectionId}</c> to pull and push, and the blob routes under
    /// <c>/{collectionId}/{recordId}/blobs/{blobId}</c>, which answer <c>404 blobs_disabled</c> for a collection
    /// without blobs. Every route requires an authenticated user, from the host app's own sign-in.
    /// </summary>
    /// <param name="app">The app's routes.</param>
    /// <param name="prefix">The route prefix, <c>sync</c> by default.</param>
    /// <returns>The group, for the host's own conventions such as <c>RequireRateLimiting</c>.</returns>
    /// <exception cref="InvalidOperationException">No record store is registered.</exception>
    public static RouteGroupBuilder MapConsealSyncRoutes(this IEndpointRouteBuilder app, string prefix = "sync")
    {
        RequireService<IConsealSyncRecordStore>(app);
        var group = app.MapGroup(prefix).RequireAuthorization().AddEndpointFilter<StoreBusyFilter>();

        group.MapGet("", (HttpContext context, SyncRequestHandler handler) => handler.GetConfig(context));
        group.MapGet("{collectionId}", (HttpContext context, string collectionId, SyncRequestHandler handler) =>
            handler.PullAsync(context, collectionId));
        group.MapPost("{collectionId}", (HttpContext context, string collectionId, SyncRequestHandler handler) =>
            handler.PushAsync(context, collectionId));

        const string blob = "{collectionId}/{recordId}/blobs/{blobId}";
        group.MapPost(blob, (HttpContext context, string collectionId, string recordId, string blobId, BlobRequestHandler handler) =>
            handler.CreateUploadAsync(context, collectionId, recordId, blobId));
        group.MapGet(blob, (HttpContext context, string collectionId, string recordId, string blobId, BlobRequestHandler handler) =>
            handler.CreateDownloadAsync(context, collectionId, recordId, blobId));
        group.MapDelete(blob, (HttpContext context, string collectionId, string recordId, string blobId, BlobRequestHandler handler) =>
            handler.DeleteAsync(context, collectionId, recordId, blobId));

        return group;
    }

    /// <summary>
    /// Maps the circle routes of <c>circle.yaml</c> under <paramref name="prefix"/>: the AEK commitment, the
    /// devices and their wrapped AEKs, and the join ceremony. Every route requires an authenticated user, from the
    /// host app's own sign-in.
    /// </summary>
    /// <param name="app">The app's routes.</param>
    /// <param name="prefix">The route prefix, <c>circle</c> by default.</param>
    /// <returns>The group, for the host's own conventions such as <c>RequireRateLimiting</c>.</returns>
    /// <exception cref="InvalidOperationException">No circle store is registered.</exception>
    public static RouteGroupBuilder MapConsealCircleRoutes(this IEndpointRouteBuilder app, string prefix = "circle")
    {
        RequireService<IConsealCircleStore>(app);
        var group = app.MapGroup(prefix).RequireAuthorization().AddEndpointFilter<StoreBusyFilter>();

        group.MapGet("", (HttpContext context, CircleRequestHandler handler) => handler.GetCircleAsync(context));
        group.MapPost("", (HttpContext context, CircleRequestHandler handler) => handler.CreateCircleAsync(context));

        group.MapGet("devices", (HttpContext context, CircleRequestHandler handler) => handler.ListDevicesAsync(context));
        group.MapDelete("devices/{deviceId}", (HttpContext context, string deviceId, CircleRequestHandler handler) =>
            handler.RevokeDeviceAsync(context, deviceId));
        group.MapGet("devices/{deviceId}/wrapped-aek", (HttpContext context, string deviceId, CircleRequestHandler handler) =>
            handler.GetWrappedAekAsync(context, deviceId));
        group.MapPut("devices/{deviceId}/wrapped-aek", (HttpContext context, string deviceId, CircleRequestHandler handler) =>
            handler.SaveWrappedAekAsync(context, deviceId));

        group.MapGet("joins", (HttpContext context, CircleRequestHandler handler) => handler.ListJoinsAsync(context));
        group.MapPost("joins", (HttpContext context, CircleRequestHandler handler) => handler.CreateJoinAsync(context));
        group.MapGet("joins/{challengeId}", (HttpContext context, string challengeId, CircleRequestHandler handler) =>
            handler.GetJoinAsync(context, challengeId));
        group.MapPost("joins/{challengeId}/authorize", (HttpContext context, string challengeId, CircleRequestHandler handler) =>
            handler.AuthorizeJoinAsync(context, challengeId));
        group.MapPost("joins/{challengeId}/deny", (HttpContext context, string challengeId, CircleRequestHandler handler) =>
            handler.DenyJoinAsync(context, challengeId));

        return group;
    }

    private static void RequireService<T>(IEndpointRouteBuilder app) where T : class
    {
        if (app.ServiceProvider.GetService<T>() is null)
        {
            throw new InvalidOperationException(
                $"No {typeof(T).Name} is registered. Call AddConsealSync(...) and then AddDynamoStores(...) or AddInMemoryStores().");
        }
    }
}
