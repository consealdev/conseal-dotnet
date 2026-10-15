using ConsealDev.Conseal.Storage;

namespace ConsealDev.Conseal.Tests.TestSupport;

/// <summary>A blob store that signs nothing: it hands out URLs naming what was asked, and records every call.</summary>
internal sealed class RecordingBlobStore : IConsealSyncBlobStore
{
    public List<string> Calls { get; } = [];

    /// <summary>When set, deleting a record's blobs throws it, as storage that's down would.</summary>
    public Exception? DeleteRecordBlobsFails { get; set; }

    public Task<PresignedBlobUrl> CreateUploadUrlAsync(BlobKey key, long size, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        Calls.Add($"upload {Path(key)} {size}");
        return Task.FromResult(new PresignedBlobUrl($"https://storage.test/{Path(key)}?put={size}", new Dictionary<string, string> { ["x-test"] = "signed" }));
    }

    public Task<PresignedBlobUrl> CreateDownloadUrlAsync(BlobKey key, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        Calls.Add($"download {Path(key)}");
        return Task.FromResult(new PresignedBlobUrl($"https://storage.test/{Path(key)}?get", new Dictionary<string, string>()));
    }

    public Task DeleteAsync(BlobKey key, CancellationToken cancellationToken)
    {
        Calls.Add($"delete {Path(key)}");
        return Task.CompletedTask;
    }

    public Task DeleteRecordBlobsAsync(string userId, string collectionId, string recordId, CancellationToken cancellationToken)
    {
        Calls.Add($"delete record {userId}/{collectionId}/{recordId}");
        return DeleteRecordBlobsFails is null ? Task.CompletedTask : Task.FromException(DeleteRecordBlobsFails);
    }

    private static string Path(BlobKey key) => $"{key.UserId}/{key.CollectionId}/{key.RecordId}/{key.BlobId}";
}
