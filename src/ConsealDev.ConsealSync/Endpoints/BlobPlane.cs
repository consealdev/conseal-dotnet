using ConsealDev.ConsealSync.Configuration;
using ConsealDev.ConsealSync.Storage;
using Microsoft.Extensions.Options;

namespace ConsealDev.ConsealSync.Endpoints;

/// <summary>
/// Which collections have blobs, and their store. A collection has them when <c>WithBlobs</c> registered a store and
/// lists it; without <c>WithBlobs</c>, none does and nothing needs object storage.
/// </summary>
internal sealed class BlobPlane(IOptions<ConsealSyncBlobOptions> options, IConsealSyncBlobStore? store = null)
{
    public ConsealSyncBlobOptions Options { get; } = options.Value;

    /// <summary>The store, when the collection has blobs; otherwise null.</summary>
    public IConsealSyncBlobStore? StoreFor(string collectionId) =>
        store is not null && Options.Collections.Contains(collectionId) ? store : null;

    /// <summary>The collection's <c>maxBlobBytes</c> for <c>GET /</c>: null when it has no blobs.</summary>
    public long? MaxBlobBytes(string collectionId) => StoreFor(collectionId) is null ? null : Options.MaxBlobBytes;
}
