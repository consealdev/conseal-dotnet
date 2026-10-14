using ConsealDev.ConsealSync.Storage;

namespace ConsealDev.ConsealSync.Wire;

/// <summary>The sync routes' response bodies, shaped as <c>openapi.yaml</c> defines them.</summary>
internal static class SyncWire
{
    public sealed record Config(IReadOnlyList<CollectionLimits> Collections, int MaxBatchItems, int MaxPageItems);

    public sealed record CollectionLimits(string Id, int MaxRecordBytes, long? MaxBlobBytes);

    public sealed record Record(
        string Id,
        string? Ciphertext,
        string? Iv,
        string? WrappedDek,
        string Version,
        long Seq,
        DateTimeOffset? DeletedAt)
    {
        public static Record From(StoredRecord record) => new(
            record.Id,
            record.Content is null ? null : Convert.ToBase64String(record.Content.Ciphertext),
            record.Content is null ? null : Convert.ToBase64String(record.Content.Iv),
            record.Content is null ? null : Convert.ToBase64String(record.Content.WrappedDek),
            record.Version,
            record.Seq,
            record.DeletedAt);
    }

    public sealed record Page(IReadOnlyList<Record> Items, string Cursor, bool HasMore);

    /// <summary>Each result is a <see cref="Written"/>, <see cref="Conflicted"/> or <see cref="Rejected"/>.</summary>
    public sealed record PushResponse(IReadOnlyList<object> Results);

    public sealed record Written(string Id, int Status, string Version, long Seq);

    public sealed record Conflicted(string Id, int Status, Record? Current);

    public sealed record Rejected(string Id, int Status, ErrorBody Error);

    public sealed record BlobUrl(string Url, string Method, IReadOnlyDictionary<string, string> Headers, DateTimeOffset ExpiresAt);
}
