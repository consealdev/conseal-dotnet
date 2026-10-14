using ConsealDev.ConsealSync.Tests.TestSupport;

namespace ConsealDev.ConsealSync.Tests.Storage;

public sealed class InMemoryConcurrentWritersTests : ConcurrentWritersTests
{
    private protected override Task<TestHost> StartHostAsync() => TestHost.StartAsync();
}
