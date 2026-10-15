namespace ConsealDev.Conseal.Storage;

/// <summary>One write to a record, applied only if the record is still at <paramref name="BaseVersion"/>.</summary>
/// <param name="Id">The record's id.</param>
/// <param name="BaseVersion">The version this write replaces; null when the record must not exist yet.</param>
/// <param name="Content">The content to store; null to tombstone the record.</param>
public sealed record RecordWrite(string Id, string? BaseVersion, SealedContent? Content);
