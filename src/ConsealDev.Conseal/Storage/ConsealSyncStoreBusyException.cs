namespace ConsealDev.Conseal.Storage;

/// <summary>
/// Concurrent writes to one collection kept a store from committing a write within its retries. Transient: the
/// routes answer <c>503</c> with <c>Retry-After</c>, and the client tries again.
/// </summary>
public sealed class ConsealSyncStoreBusyException(string message, Exception? innerException = null)
    : Exception(message, innerException);
