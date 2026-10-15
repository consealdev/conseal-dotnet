using ConsealDev.Conseal.Storage;
using ConsealDev.Conseal.Storage.InMemory;

namespace ConsealDev.Conseal.Tests.Storage;

public sealed class InMemoryCircleStoreTests : CircleStoreContractTests
{
    protected override Task<IConsealCircleStore> CreateStoreAsync() => Task.FromResult<IConsealCircleStore>(new InMemoryCircleStore());
}
