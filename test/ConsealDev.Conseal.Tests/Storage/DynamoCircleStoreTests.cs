using ConsealDev.Conseal.Storage;
using ConsealDev.Conseal.Storage.Dynamo;
using ConsealDev.Conseal.Tests.TestSupport;

namespace ConsealDev.Conseal.Tests.Storage;

public sealed class DynamoCircleStoreTests : CircleStoreContractTests, IAsyncDisposable
{
    private readonly DynamoTables _tables = new();

    public ValueTask DisposeAsync() => _tables.DisposeAsync();

    protected override async Task<IConsealCircleStore> CreateStoreAsync()
    {
        await _tables.CreateAsync();
        return new DynamoCircleStore(_tables.Client, _tables.StoreOptions);
    }
}
