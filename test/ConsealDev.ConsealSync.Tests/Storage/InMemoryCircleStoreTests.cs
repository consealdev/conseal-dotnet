using ConsealDev.ConsealSync.Storage;
using ConsealDev.ConsealSync.Storage.InMemory;

namespace ConsealDev.ConsealSync.Tests.Storage;

public sealed class InMemoryCircleStoreTests : CircleStoreContractTests
{
    protected override Task<IConsealCircleStore> CreateStoreAsync() => Task.FromResult<IConsealCircleStore>(new InMemoryCircleStore());
}
