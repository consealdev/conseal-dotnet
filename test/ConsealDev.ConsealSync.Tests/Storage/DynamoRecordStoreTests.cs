using System.Security.Cryptography;
using ConsealDev.ConsealSync.Storage;
using ConsealDev.ConsealSync.Storage.Dynamo;
using ConsealDev.ConsealSync.Tests.TestSupport;
using FluentAssertions;

namespace ConsealDev.ConsealSync.Tests.Storage;

public sealed class DynamoRecordStoreTests : RecordStoreContractTests, IAsyncDisposable
{
    private readonly DynamoTables _tables = new();

    public ValueTask DisposeAsync() => _tables.DisposeAsync();

    [Fact]
    public async Task A_record_at_the_limit_fits_with_the_longest_user_and_collection_ids()
    {
        var store = (DynamoRecordStore)await CreateStoreAsync();
        var userId = new string('u', 256);
        var collectionId = new string('c', 64);
        var id = Items.NewId();

        var created = await store.WriteAsync(userId, collectionId, new RecordWrite(id, null, Content(store.MaxRecordBytes)), default);
        var replaced = await store.WriteAsync(
            userId, collectionId, new RecordWrite(id, ((RecordWriteResult.Written)created).Version, Content(store.MaxRecordBytes)), default);

        created.Should().BeOfType<RecordWriteResult.Written>();
        replaced.Should().BeOfType<RecordWriteResult.Written>();
    }

    [Fact]
    public async Task A_record_the_storage_cant_hold_is_refused_as_too_large_rather_than_failing()
    {
        var store = await CreateStoreAsync();

        var result = await store.WriteAsync("user-1", "7f3a9c21", new RecordWrite(Items.NewId(), null, Content(300 * 1024)), default);

        result.Should().BeOfType<RecordWriteResult.TooLarge>();
    }

    private protected override async Task<IConsealSyncRecordStore> CreateStoreAsync()
    {
        await _tables.CreateAsync();
        return new DynamoRecordStore(_tables.Client, _tables.StoreOptions, Time);
    }

    private static SealedContent Content(int ciphertextBytes) =>
        new(RandomNumberGenerator.GetBytes(ciphertextBytes), RandomNumberGenerator.GetBytes(12), RandomNumberGenerator.GetBytes(60));
}
