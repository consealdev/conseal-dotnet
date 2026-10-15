using ConsealDev.Conseal.Storage;
using ConsealDev.Conseal.Storage.InMemory;

namespace ConsealDev.Conseal.Tests.Storage;

public sealed class InMemoryRecordStoreTests : RecordStoreContractTests
{
    private protected override Task<IConsealSyncRecordStore> CreateStoreAsync() =>
        Task.FromResult<IConsealSyncRecordStore>(new InMemoryRecordStore(Time));
}
