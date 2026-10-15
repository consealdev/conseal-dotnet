using Amazon.DynamoDBv2.Model;
using ConsealDev.Conseal.Storage.Dynamo;
using ConsealDev.Conseal.Tests.TestSupport;
using FluentAssertions;

namespace ConsealDev.Conseal.Tests.Storage;

public sealed class ConsealSyncDynamoTablesTests : IAsyncDisposable
{
    private readonly DynamoTables _tables = new();

    public ValueTask DisposeAsync() => _tables.DisposeAsync();

    [Fact]
    public async Task Creating_the_tables_again_changes_nothing()
    {
        await _tables.CreateAsync();

        var again = () => ConsealSyncDynamoTables.CreateAsync(_tables.Client, _tables.Prefix, CancellationToken.None);

        await again.Should().NotThrowAsync();
    }

    [Fact]
    public async Task The_circle_table_drops_stale_join_requests_by_time_to_live()
    {
        await _tables.CreateAsync();

        var description = await _tables.Client.DescribeTimeToLiveAsync(
            new DescribeTimeToLiveRequest { TableName = ConsealSyncDynamoTables.CircleTableName(_tables.Prefix) });

        description.TimeToLiveDescription.AttributeName.Should().Be(ConsealSyncDynamoTables.TimeToLiveAttribute);
    }
}
