// SPDX-License-Identifier: AGPL-3.0-or-later OR LicenseRef-Commercial

using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using ConsealDev.Conseal.Configuration;
using Microsoft.Extensions.Options;

namespace ConsealDev.Conseal.Storage.S3;

/// <summary>
/// Keeps blobs in an S3 bucket, or an S3-compatible store, as objects keyed
/// <c>{prefix}{userId}/{collectionId}/{recordId}/{blobId}</c>, and hands out Signature Version 4 presigned URLs.
/// </summary>
internal sealed class S3BlobStore : IConsealSyncBlobStore
{
    /// <summary>The most keys one <c>DeleteObjects</c> request takes.</summary>
    private const int MaxDeleteKeys = 1000;

    private static readonly IReadOnlyDictionary<string, string> NoHeaders = new Dictionary<string, string>();

    private readonly IAmazonS3 _s3;
    private readonly ConsealSyncBlobOptions _options;
    private readonly Protocol _protocol;

    public S3BlobStore(IAmazonS3 s3, IOptions<ConsealSyncBlobOptions> options)
    {
        // The SDK presigns with Signature Version 2 in us-east-1 unless this is set, process-wide, and a version 2 URL
        // doesn't sign an upload's size. Version 4 is what AWS asks for everywhere.
        AWSConfigsS3.UseSignatureVersion4 = true;

        _s3 = s3;
        _options = options.Value;
        // A local endpoint is often plain HTTP, and the SDK writes https:// into every URL it presigns otherwise.
        _protocol = _options.ServiceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? Protocol.HTTP : Protocol.HTTPS;
    }

    public async Task<PresignedBlobUrl> CreateUploadUrlAsync(BlobKey key, long size, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        var request = Presign(key, HttpVerb.PUT, expiresAt);
        // Signed, not sent: the client's own Content-Length must match it, or the storage refuses the upload.
        request.Headers.ContentLength = size;
        return new PresignedBlobUrl(await _s3.GetPreSignedURLAsync(request).ConfigureAwait(false), NoHeaders);
    }

    public async Task<PresignedBlobUrl> CreateDownloadUrlAsync(BlobKey key, DateTimeOffset expiresAt, CancellationToken cancellationToken) =>
        new(await _s3.GetPreSignedURLAsync(Presign(key, HttpVerb.GET, expiresAt)).ConfigureAwait(false), NoHeaders);

    public Task DeleteAsync(BlobKey key, CancellationToken cancellationToken) =>
        _s3.DeleteObjectAsync(_options.BucketName, ObjectKey(key), cancellationToken);

    public async Task DeleteRecordBlobsAsync(string userId, string collectionId, string recordId, CancellationToken cancellationToken)
    {
        var listing = new ListObjectsV2Request { BucketName = _options.BucketName, Prefix = RecordPrefix(userId, collectionId, recordId) };
        ListObjectsV2Response page;
        do
        {
            page = await _s3.ListObjectsV2Async(listing, cancellationToken).ConfigureAwait(false);
            var keys = page.S3Objects?.Select(o => new KeyVersion { Key = o.Key }).ToList() ?? [];
            foreach (var batch in keys.Chunk(MaxDeleteKeys))
            {
                await _s3.DeleteObjectsAsync(new DeleteObjectsRequest { BucketName = _options.BucketName, Objects = [.. batch] }, cancellationToken)
                    .ConfigureAwait(false);
            }
            listing.ContinuationToken = page.NextContinuationToken;
        }
        while (page.IsTruncated == true);
    }

    private GetPreSignedUrlRequest Presign(BlobKey key, HttpVerb verb, DateTimeOffset expiresAt) => new()
    {
        BucketName = _options.BucketName,
        Key = ObjectKey(key),
        Verb = verb,
        Expires = expiresAt.UtcDateTime,
        Protocol = _protocol,
    };

    private string ObjectKey(BlobKey key) => RecordPrefix(key.UserId, key.CollectionId, key.RecordId) + key.BlobId;

    /// <summary>A record's blobs share a prefix, so a tombstone lists and deletes them together.</summary>
    private string RecordPrefix(string userId, string collectionId, string recordId) =>
        $"{_options.KeyPrefix}{Uri.EscapeDataString(userId)}/{collectionId}/{recordId}/";
}
