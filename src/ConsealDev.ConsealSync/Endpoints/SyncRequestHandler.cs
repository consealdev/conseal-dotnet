using System.Globalization;
using ConsealDev.ConsealSync.Configuration;
using ConsealDev.ConsealSync.Storage;
using ConsealDev.ConsealSync.Sync;
using ConsealDev.ConsealSync.Wire;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ConsealDev.ConsealSync.Endpoints;

/// <summary>The sync routes of <c>openapi.yaml</c>, blob routes aside: the config, pulls and pushes.</summary>
internal sealed class SyncRequestHandler(
    IOptions<ConsealSyncOptions> options,
    IConsealSyncRecordStore store,
    SyncCursor cursors,
    BlobPlane blobs,
    ILogger<SyncRequestHandler> logger)
{
    /// <summary>The page size when the client names none, as the contract defaults <c>limit</c>.</summary>
    private const int DefaultPageItems = 500;

    private readonly ConsealSyncOptions _options = options.Value;

    /// <summary>The collections the routes accept: the keyring, which the spec reserves, and the app's.</summary>
    private readonly IReadOnlyList<string> _collections = [ConsealSyncOptions.KeyringCollection, .. options.Value.Collections];

    public IResult GetConfig(HttpContext context)
    {
        if (RequestUser.Find(context, _options) is null) return ApiResults.Unauthenticated();

        var collections = _collections
            .Select(id => new SyncWire.CollectionLimits(id, store.MaxRecordBytes, blobs.MaxBlobBytes(id)))
            .ToList();
        return ApiResults.Ok(new SyncWire.Config(collections, _options.MaxBatchItems, _options.MaxPageItems));
    }

    public async Task<IResult> PullAsync(HttpContext context, string collectionId)
    {
        var userId = RequestUser.Find(context, _options);
        if (userId is null) return ApiResults.Unauthenticated();
        if (!_collections.Contains(collectionId)) return UnknownCollection(collectionId);

        var query = context.Request.Query;
        var limit = DefaultPageItems;
        if (query.TryGetValue("limit", out var limitValues))
        {
            if (limitValues.Count != 1
                || !long.TryParse(limitValues[0], NumberStyles.None, CultureInfo.InvariantCulture, out var asked)
                || asked < 1)
            {
                return ApiResults.Invalid("limit must be a whole number of at least 1.");
            }
            limit = (int)Math.Min(asked, int.MaxValue);
        }
        limit = Math.Min(limit, _options.MaxPageItems);

        long afterSeq = 0;
        if (query.TryGetValue("cursor", out var cursorValues)
            && (cursorValues.Count != 1 || !cursors.TryDecode(cursorValues[0]!, userId, collectionId, out afterSeq)))
        {
            return InvalidCursor("The cursor isn't one this server issued for this user and collection.");
        }

        var result = await store.PullAsync(userId, collectionId, afterSeq, limit, context.RequestAborted).ConfigureAwait(false);
        return result switch
        {
            RecordPullResult.Page page => ApiResults.Ok(new SyncWire.Page(
                page.Items.Select(SyncWire.Record.From).ToList(),
                cursors.Encode(userId, collectionId, page.Watermark),
                page.HasMore)),
            _ => InvalidCursor("The cursor is past every write in the collection, so it wasn't issued for this data."),
        };
    }

    public async Task<IResult> PushAsync(HttpContext context, string collectionId)
    {
        var userId = RequestUser.Find(context, _options);
        if (userId is null) return ApiResults.Unauthenticated();
        if (!_collections.Contains(collectionId)) return UnknownCollection(collectionId);

        var cancellationToken = context.RequestAborted;
        var body = await RequestBody.ReadAsync(context.Request, _options.MaxRequestBytes, cancellationToken).ConfigureAwait(false);
        if (body is null)
        {
            return ApiResults.Error(
                StatusCodes.Status413PayloadTooLarge,
                ErrorCodes.RequestTooLarge,
                $"A push body is at most {_options.MaxRequestBytes} bytes. Send fewer items.");
        }

        var parsed = PushRequest.Parse(body, _options.MaxBatchItems, store.MaxRecordBytes);
        if (parsed is PushParse.Failed failed) return ApiResults.Error(failed.Status, failed.Code, failed.Message);

        // One at a time: writes to one collection commit in order anyway, so running them at once only adds retries.
        var results = new List<object>();
        foreach (var item in ((PushParse.Items)parsed).All)
        {
            results.Add(await ResultAsync(userId, collectionId, item, cancellationToken).ConfigureAwait(false));
        }
        return ApiResults.Ok(new SyncWire.PushResponse(results));
    }

    private async Task<object> ResultAsync(string userId, string collectionId, PushItem item, CancellationToken cancellationToken)
    {
        if (item is PushItem.Rejected rejected)
        {
            return new SyncWire.Rejected(rejected.Id, rejected.Status, new ErrorBody(rejected.Code, rejected.Message));
        }

        var write = ((PushItem.Valid)item).Write;
        var outcome = await store.WriteAsync(userId, collectionId, write, cancellationToken).ConfigureAwait(false);
        if (outcome is RecordWriteResult.Written && write.Content is null)
        {
            await DeleteBlobsAsync(userId, collectionId, write.Id, cancellationToken).ConfigureAwait(false);
        }
        return outcome switch
        {
            RecordWriteResult.Written written => new SyncWire.Written(write.Id, StatusCodes.Status200OK, written.Version, written.Seq),
            RecordWriteResult.Conflict conflict => new SyncWire.Conflicted(
                write.Id,
                StatusCodes.Status409Conflict,
                conflict.Current is null ? null : SyncWire.Record.From(conflict.Current)),
            RecordWriteResult.TooLarge => new SyncWire.Rejected(
                write.Id,
                StatusCodes.Status413PayloadTooLarge,
                new ErrorBody(ErrorCodes.RecordTooLarge, "The storage can't hold a record this large. Move content to a blob.")),
            _ => throw new InvalidOperationException($"The record store returned an unknown result, {outcome.GetType().Name}."),
        };
    }

    /// <summary>
    /// Deletes a tombstoned record's blobs. A failure is logged rather than failing the push: the tombstone stands, and
    /// a blob left behind is ciphertext that costs storage, not privacy.
    /// </summary>
    private async Task DeleteBlobsAsync(string userId, string collectionId, string recordId, CancellationToken cancellationToken)
    {
        if (blobs.StoreFor(collectionId) is not { } blobStore) return;
        try
        {
            await blobStore.DeleteRecordBlobsAsync(userId, collectionId, recordId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Deleting the blobs of a tombstoned record failed; they stay in storage.");
        }
    }

    private static IResult UnknownCollection(string collectionId) =>
        ApiResults.Error(StatusCodes.Status404NotFound, ErrorCodes.UnknownCollection, $"The server accepts no collection '{collectionId}'.");

    private static IResult InvalidCursor(string message) =>
        ApiResults.Error(StatusCodes.Status400BadRequest, ErrorCodes.InvalidCursor, message);
}
