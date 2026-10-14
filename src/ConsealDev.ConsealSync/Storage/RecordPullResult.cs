namespace ConsealDev.ConsealSync.Storage;

/// <summary>What a pull read: a page of records, or nothing because the position is past every write.</summary>
public abstract record RecordPullResult
{
    private RecordPullResult()
    {
    }

    /// <summary>
    /// Records written after the pull's position, in ascending <c>Seq</c>, each in its latest state. The page never
    /// skips a write: every write given a <c>Seq</c> up to <paramref name="Watermark"/> is in this page or an earlier
    /// one, or was replaced by a later write of the same record, whose higher <c>Seq</c> a later pull returns.
    /// </summary>
    /// <param name="Items">The records, tombstones included.</param>
    /// <param name="Watermark">Where the next pull starts.</param>
    /// <param name="HasMore">Whether records may be waiting past <paramref name="Watermark"/> already.</param>
    public sealed record Page(IReadOnlyList<StoredRecord> Items, long Watermark, bool HasMore) : RecordPullResult;

    /// <summary>
    /// The position is past the highest <c>Seq</c> the collection has given, so it wasn't taken from this data: the
    /// collection was emptied, for one, since the cursor was issued.
    /// </summary>
    public sealed record BeyondHead() : RecordPullResult;
}
