namespace ConsealDev.Conseal.Storage;

/// <summary>
/// Where the sync routes keep records: opaque ciphertext keyed by user, collection and record id. An implementation
/// gives every write a new version and the collection's next <c>Seq</c>, in the order writes commit, and answers each
/// pull from a strongly consistent read, so a pull never skips a write.
/// </summary>
public interface IConsealSyncRecordStore
{
    /// <summary>
    /// The largest decoded ciphertext a record may have, in bytes: the storage's own item limit less the space the
    /// rest of a stored record takes, so a record the routes accept is always one the storage can hold.
    /// </summary>
    int MaxRecordBytes { get; }

    /// <summary>Writes or tombstones one record, if it is still at the write's base version.</summary>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="collectionId">An allowlisted collection id.</param>
    /// <param name="write">The write.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The new version and <c>Seq</c>, or the current record when the version moved on.</returns>
    /// <exception cref="ConsealSyncStoreBusyException">Other writes to the collection kept the store from committing.</exception>
    Task<RecordWriteResult> WriteAsync(string userId, string collectionId, RecordWrite write, CancellationToken cancellationToken);

    /// <summary>Reads the records written after <paramref name="afterSeq"/>, in ascending <c>Seq</c>.</summary>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="collectionId">An allowlisted collection id.</param>
    /// <param name="afterSeq">The watermark of the previous page; 0 to start from the beginning.</param>
    /// <param name="limit">The most records to return. The store may return fewer, to stay within its own limits.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A page, or <see cref="RecordPullResult.BeyondHead"/> when <paramref name="afterSeq"/> is past every write.</returns>
    Task<RecordPullResult> PullAsync(string userId, string collectionId, long afterSeq, int limit, CancellationToken cancellationToken);
}
