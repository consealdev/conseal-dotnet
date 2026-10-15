namespace ConsealDev.Conseal.Storage;

/// <summary>A record as the server holds it, in its latest state.</summary>
/// <param name="Id">The record's id, a lowercase version 4 UUID.</param>
/// <param name="Content">The sealed content; null for a tombstone.</param>
/// <param name="Version">The server's version, new on every write.</param>
/// <param name="Seq">The record's place in its collection's order.</param>
/// <param name="DeletedAt">When the server tombstoned the record, by its own clock; null while it is live.</param>
public sealed record StoredRecord(string Id, SealedContent? Content, string Version, long Seq, DateTimeOffset? DeletedAt);
