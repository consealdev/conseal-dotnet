using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using ConsealDev.Conseal.Configuration;
using ConsealDev.Conseal.Storage;
using ConsealDev.Conseal.Wire;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace ConsealDev.Conseal.Endpoints;

/// <summary>
/// The blob routes of <c>openapi.yaml</c>: presigned URLs to upload and download a blob, and deleting one. The bytes
/// go between the client and object storage directly; these routes only sign the way.
/// </summary>
internal sealed class BlobRequestHandler(IOptions<ConsealSyncOptions> options, BlobPlane blobs, TimeProvider time)
{
    /// <summary>The upload request is <c>{"size": n}</c>: anything near this is something else.</summary>
    private const int MaxBodyBytes = 1024;

    private readonly ConsealSyncOptions _options = options.Value;

    public async Task<IResult> CreateUploadAsync(HttpContext context, string collectionId, string recordId, string blobId)
    {
        if (!TryAccept(context, collectionId, recordId, blobId, out var key, out var store, out var refusal)) return refusal;

        var body = await RequestBody.ReadAsync(context.Request, MaxBodyBytes, context.RequestAborted).ConfigureAwait(false);
        if (ParseSize(body) is not { } size)
        {
            return ApiResults.Invalid(
                $"The body must be a JSON object whose size is the sealed blob's length in bytes, at least {ConsealSyncBlobOptionsValidator.MinSealedBytes}.");
        }
        if (size > blobs.Options.MaxBlobBytes)
        {
            return ApiResults.Error(
                StatusCodes.Status413PayloadTooLarge,
                ErrorCodes.BlobTooLarge,
                $"A sealed blob is at most {blobs.Options.MaxBlobBytes} bytes.");
        }

        var expiresAt = ExpiresAt();
        var url = await store.CreateUploadUrlAsync(key, size, expiresAt, context.RequestAborted).ConfigureAwait(false);
        return ApiResults.Ok(new SyncWire.BlobUrl(url.Url, HttpMethods.Put, url.Headers, expiresAt));
    }

    public async Task<IResult> CreateDownloadAsync(HttpContext context, string collectionId, string recordId, string blobId)
    {
        if (!TryAccept(context, collectionId, recordId, blobId, out var key, out var store, out var refusal)) return refusal;

        var expiresAt = ExpiresAt();
        var url = await store.CreateDownloadUrlAsync(key, expiresAt, context.RequestAborted).ConfigureAwait(false);
        return ApiResults.Ok(new SyncWire.BlobUrl(url.Url, HttpMethods.Get, url.Headers, expiresAt));
    }

    public async Task<IResult> DeleteAsync(HttpContext context, string collectionId, string recordId, string blobId)
    {
        if (!TryAccept(context, collectionId, recordId, blobId, out var key, out var store, out var refusal)) return refusal;

        await store.DeleteAsync(key, context.RequestAborted).ConfigureAwait(false);
        return Results.NoContent();
    }

    /// <summary>The blob's key and store; false with the answer that refuses the request.</summary>
    private bool TryAccept(
        HttpContext context,
        string collectionId,
        string recordId,
        string blobId,
        [NotNullWhen(true)] out BlobKey? key,
        [NotNullWhen(true)] out IConsealSyncBlobStore? store,
        [NotNullWhen(false)] out IResult? refusal)
    {
        key = null;
        store = null;
        refusal = null;
        if (RequestUser.Find(context, _options) is not { } userId)
        {
            refusal = ApiResults.Unauthenticated();
        }
        else if (collectionId != ConsealSyncOptions.KeyringCollection && !_options.Collections.Contains(collectionId))
        {
            refusal = ApiResults.Error(StatusCodes.Status404NotFound, ErrorCodes.UnknownCollection, $"The server accepts no collection '{collectionId}'.");
        }
        else if ((store = blobs.StoreFor(collectionId)) is null)
        {
            refusal = ApiResults.Error(StatusCodes.Status404NotFound, ErrorCodes.BlobsDisabled, $"Collection '{collectionId}' has no blobs on this server.");
        }
        else if (!Ids.IsClientUuid(recordId) || !Ids.IsClientUuid(blobId))
        {
            store = null;
            refusal = ApiResults.Invalid("The record and blob ids must be lowercase version 4 UUIDs.");
        }
        else
        {
            key = new BlobKey(userId, collectionId, recordId, blobId);
        }
        return refusal is null;
    }

    private DateTimeOffset ExpiresAt() => time.GetUtcNow() + blobs.Options.UrlLifetime;

    /// <summary>The upload request's size; null when the body isn't <c>{"size": n}</c> with n a sealed blob's length.</summary>
    private static long? ParseSize(byte[]? body)
    {
        if (body is null) return null;
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("size", out var size)
                || size.ValueKind != JsonValueKind.Number
                || !size.TryGetInt64(out var value)
                || value < ConsealSyncBlobOptionsValidator.MinSealedBytes)
            {
                return null;
            }
            return value;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
