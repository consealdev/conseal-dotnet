namespace ConsealDev.Conseal.Storage;

/// <summary>
/// What became of a <see cref="RecordWrite"/>: written, refused because the version moved on, or refused because the
/// storage can't hold it.
/// </summary>
public abstract record RecordWriteResult
{
    private RecordWriteResult()
    {
    }

    /// <summary>The write is stored.</summary>
    /// <param name="Version">The record's new version.</param>
    /// <param name="Seq">The record's new place in the collection's order.</param>
    public sealed record Written(string Version, long Seq) : RecordWriteResult;

    /// <summary>The record isn't at the write's base version, so nothing was written.</summary>
    /// <param name="Current">The record as the server holds it; null when there is none.</param>
    public sealed record Conflict(StoredRecord? Current) : RecordWriteResult;

    /// <summary>
    /// The storage refused the record as too large, though it was within <c>MaxRecordBytes</c>. The routes answer the
    /// item with <c>413 record_too_large</c>, which a client doesn't retry, rather than an error it would retry forever.
    /// </summary>
    public sealed record TooLarge() : RecordWriteResult;
}
