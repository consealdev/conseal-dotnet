using ConsealDev.Conseal.Storage;

namespace ConsealDev.Conseal.Tests.TestSupport;

/// <summary>A record store whose storage refuses every write as too large, whatever its limit said.</summary>
internal sealed class TooLargeRecordStore : IConsealSyncRecordStore
{
    public int MaxRecordBytes => 1024 * 1024;

    public Task<RecordWriteResult> WriteAsync(string userId, string collectionId, RecordWrite write, CancellationToken cancellationToken) =>
        Task.FromResult<RecordWriteResult>(new RecordWriteResult.TooLarge());

    public Task<RecordPullResult> PullAsync(string userId, string collectionId, long afterSeq, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<RecordPullResult>(new RecordPullResult.Page([], 0, HasMore: false));
}
