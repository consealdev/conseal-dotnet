using ConsealDev.ConsealSync.Tests.TestSupport;

namespace ConsealDev.ConsealSync.Tests.Storage;

public sealed class DynamoConcurrentWritersTests : ConcurrentWritersTests, IAsyncDisposable
{
    private readonly DynamoTables _tables = new();

    public ValueTask DisposeAsync() => _tables.DisposeAsync();

    private protected override async Task<TestHost> StartHostAsync()
    {
        await _tables.CreateAsync();
        return await TestHost.StartAsync(stores: _tables.Register);
    }
}
