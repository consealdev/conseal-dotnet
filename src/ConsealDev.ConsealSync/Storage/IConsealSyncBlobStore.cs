namespace ConsealDev.ConsealSync.Storage;

/// <summary>Where one blob lives: its user, collection and record, and its own id.</summary>
/// <param name="UserId">The signed-in user the blob belongs to.</param>
/// <param name="CollectionId">The collection of the record that refers to it.</param>
/// <param name="RecordId">The record that refers to it.</param>
/// <param name="BlobId">The blob's id, a random lowercase UUID the client mints.</param>
public sealed record BlobKey(string UserId, string CollectionId, string RecordId, string BlobId);

/// <summary>A presigned storage request, for the client to send as it is.</summary>
/// <param name="Url">The presigned URL.</param>
/// <param name="Headers">Headers the client sends with it, exactly as given.</param>
public sealed record PresignedBlobUrl(string Url, IReadOnlyDictionary<string, string> Headers);

/// <summary>
/// Where the blob routes keep blobs: object storage the client reaches through presigned URLs, so a blob's bytes
/// never pass through the app. Each blob is opaque ciphertext, sealed by the client.
/// </summary>
public interface IConsealSyncBlobStore
{
    /// <summary>
    /// A URL to <c>PUT</c> the blob to until <paramref name="expiresAt"/>, signed for exactly <paramref name="size"/>
    /// bytes, so the storage refuses an upload of any other size. Uploading again replaces the blob.
    /// </summary>
    Task<PresignedBlobUrl> CreateUploadUrlAsync(BlobKey key, long size, DateTimeOffset expiresAt, CancellationToken cancellationToken);

    /// <summary>A URL to <c>GET</c> the blob from until <paramref name="expiresAt"/>. A missing blob is the storage's <c>404</c>.</summary>
    Task<PresignedBlobUrl> CreateDownloadUrlAsync(BlobKey key, DateTimeOffset expiresAt, CancellationToken cancellationToken);

    /// <summary>Deletes the blob. Deleting one that isn't there succeeds.</summary>
    Task DeleteAsync(BlobKey key, CancellationToken cancellationToken);

    /// <summary>Deletes every blob of a record, as its tombstone does.</summary>
    Task DeleteRecordBlobsAsync(string userId, string collectionId, string recordId, CancellationToken cancellationToken);
}
