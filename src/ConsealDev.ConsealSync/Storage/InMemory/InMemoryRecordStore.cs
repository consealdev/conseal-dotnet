using ConsealDev.ConsealSync.Storage.Dynamo;
using ConsealDev.ConsealSync.Wire;

namespace ConsealDev.ConsealSync.Storage.InMemory;

/// <summary>Records in memory, for tests and local development. One lock orders every write, as a commit order.</summary>
internal sealed class InMemoryRecordStore(TimeProvider time) : IConsealSyncRecordStore
{
    private readonly Dictionary<string, Partition> _partitions = [];
    private readonly Lock _lock = new();

    /// <summary>DynamoDB's limit, so an app tested against memory keeps to what production storage holds.</summary>
    public int MaxRecordBytes => DynamoRecordStore.RecordLimitBytes;

    public Task<RecordWriteResult> WriteAsync(string userId, string collectionId, RecordWrite write, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var partition = PartitionOf(userId, collectionId);
            partition.ById.TryGetValue(write.Id, out var current);
            if (current?.Version != write.BaseVersion)
            {
                return Task.FromResult<RecordWriteResult>(new RecordWriteResult.Conflict(current is null ? null : Copy(current)));
            }

            var seq = ++partition.LastSeq;
            var stored = new StoredRecord(
                write.Id,
                write.Content is null ? null : Copy(write.Content),
                Ids.NewRandomId(),
                seq,
                write.Content is null ? time.GetUtcNow() : null);
            if (current is not null) partition.BySeq.Remove(current.Seq);
            partition.BySeq.Add(seq, stored);
            partition.ById[write.Id] = stored;
            return Task.FromResult<RecordWriteResult>(new RecordWriteResult.Written(stored.Version, seq));
        }
    }

    public Task<RecordPullResult> PullAsync(string userId, string collectionId, long afterSeq, int limit, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var partition = PartitionOf(userId, collectionId);
            if (afterSeq > partition.LastSeq) return Task.FromResult<RecordPullResult>(new RecordPullResult.BeyondHead());

            var waiting = partition.BySeq.Where(entry => entry.Key > afterSeq).Take(limit + 1).Select(entry => Copy(entry.Value)).ToList();
            var hasMore = waiting.Count > limit;
            var items = hasMore ? waiting[..limit] : waiting;
            var watermark = hasMore ? items[^1].Seq : partition.LastSeq;
            return Task.FromResult<RecordPullResult>(new RecordPullResult.Page(items, watermark, hasMore));
        }
    }

    private Partition PartitionOf(string userId, string collectionId)
    {
        var key = $"{userId}#{collectionId}";
        if (!_partitions.TryGetValue(key, out var partition))
        {
            partition = new Partition();
            _partitions[key] = partition;
        }
        return partition;
    }

    private static StoredRecord Copy(StoredRecord record) =>
        record with { Content = record.Content is null ? null : Copy(record.Content) };

    private static SealedContent Copy(SealedContent content) =>
        new(content.Ciphertext.ToArray(), content.Iv.ToArray(), content.WrappedDek.ToArray());

    private sealed class Partition
    {
        public long LastSeq { get; set; }

        public SortedDictionary<long, StoredRecord> BySeq { get; } = [];

        public Dictionary<string, StoredRecord> ById { get; } = [];
    }
}
