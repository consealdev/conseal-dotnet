using ConsealDev.ConsealSync.Storage;
using ConsealDev.ConsealSync.Storage.InMemory;

namespace ConsealDev.ConsealSync.Tests.Storage;

public sealed class InMemoryRecordStoreTests : RecordStoreContractTests
{
    private protected override Task<IConsealSyncRecordStore> CreateStoreAsync() =>
        Task.FromResult<IConsealSyncRecordStore>(new InMemoryRecordStore(Time));
}
