namespace ConsealDev.ConsealSync.Configuration;

/// <summary>
/// Settings for the blob routes, bound from the <c>ConsealSync:Blobs</c> configuration section once
/// <see cref="Extensions.ConsealSyncBuilder.WithBlobs"/> turns them on. Without it, the blob routes answer
/// <c>404 blobs_disabled</c> and no object storage is needed.
/// </summary>
public sealed class ConsealSyncBlobOptions
{
    /// <summary>The configuration section the options bind from.</summary>
    public const string SectionName = "ConsealSync:Blobs";

    /// <summary>
    /// The ids of the collections that have blobs, each one of <see cref="ConsealSyncOptions.Collections"/>. Any other
    /// collection answers the blob routes with <c>404 blobs_disabled</c>.
    /// </summary>
    public List<string> Collections { get; set; } = [];

    /// <summary>The S3 bucket that holds the blobs.</summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>
    /// Put before every object key, such as <c>blobs/</c>, when the bucket holds other things too. Keys are
    /// <c>{prefix}{userId}/{collectionId}/{recordId}/{blobId}</c>, the user id escaped as a URI component.
    /// </summary>
    public string KeyPrefix { get; set; } = string.Empty;

    /// <summary>
    /// The largest sealed blob, in bytes: its content plus 28 for the IV and the tag. A larger upload answers
    /// <c>413 blob_too_large</c>. At most 5 GiB, the most one S3 <c>PUT</c> takes.
    /// </summary>
    public long MaxBlobBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>How long a presigned URL works: long enough for one upload or download, at most 7 days.</summary>
    public TimeSpan UrlLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// An S3-compatible endpoint, such as LocalStack's, for local development. Empty for AWS itself, with the region
    /// and credentials from the standard AWS chain.
    /// </summary>
    public string ServiceUrl { get; set; } = string.Empty;
}
