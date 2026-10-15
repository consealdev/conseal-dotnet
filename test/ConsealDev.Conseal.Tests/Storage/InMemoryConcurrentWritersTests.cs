using ConsealDev.Conseal.Tests.TestSupport;

namespace ConsealDev.Conseal.Tests.Storage;

public sealed class InMemoryConcurrentWritersTests : ConcurrentWritersTests
{
    private protected override Task<TestHost> StartHostAsync() => TestHost.StartAsync();
}
