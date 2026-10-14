using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace ConsealDev.ConsealSync.Storage.Dynamo;

/// <summary>
/// The two DynamoDB tables the stores use, for an app's infrastructure code or local setup to create.
/// </summary>
/// <remarks>
/// <para>
/// <b>Records</b>, <c>{prefix}conseal-sync-records</c>: key <c>pk</c> = <c>{userId}#{collectionId}</c> and
/// <c>sk</c> = the record id, with a local secondary index <c>seq-index</c> on <c>seq</c>. A local index can be read
/// strongly consistently, which a global one can't, so a pull never sees a later write without an earlier one. Each
/// partition also holds a counter item, <c>sk</c> = <c>#counter</c>, whose <c>lastSeq</c> each write advances in the
/// same transaction as the record, so <c>seq</c> follows commit order. The index holds a copy of each record, and
/// DynamoDB counts both copies against its 400 KB item limit, which is why a record is at most about 200 KB. A local
/// index also caps a partition, one user's collection, at 10 GB.
/// </para>
/// <para>
/// <b>Circle</b>, <c>{prefix}conseal-sync-circle</c>: key <c>pk</c> = the user id and <c>sk</c> = <c>circle</c>,
/// <c>device#{deviceId}</c> or <c>join#{challengeId}</c>, with time to live on <c>ttl</c> to drop stale join
/// requests.
/// </para>
/// </remarks>
public static class ConsealSyncDynamoTables
{
    /// <summary>The records table's name after the prefix.</summary>
    public const string RecordsTableSuffix = "conseal-sync-records";

    /// <summary>The circle table's name after the prefix.</summary>
    public const string CircleTableSuffix = "conseal-sync-circle";

    /// <summary>The records table's local secondary index on <c>seq</c>.</summary>
    public const string SeqIndexName = "seq-index";

    /// <summary>The circle table's time-to-live attribute.</summary>
    public const string TimeToLiveAttribute = "ttl";

    /// <summary>The records table's name for a prefix.</summary>
    /// <param name="tablePrefix">The app's table prefix.</param>
    public static string RecordsTableName(string tablePrefix) => tablePrefix + RecordsTableSuffix;

    /// <summary>The circle table's name for a prefix.</summary>
    /// <param name="tablePrefix">The app's table prefix.</param>
    public static string CircleTableName(string tablePrefix) => tablePrefix + CircleTableSuffix;

    /// <summary>Both tables' definitions, billed on demand.</summary>
    /// <param name="tablePrefix">The app's table prefix.</param>
    public static IReadOnlyList<CreateTableRequest> Definitions(string tablePrefix) =>
    [
        new()
        {
            TableName = RecordsTableName(tablePrefix),
            BillingMode = BillingMode.PAY_PER_REQUEST,
            AttributeDefinitions =
            [
                new AttributeDefinition("pk", ScalarAttributeType.S),
                new AttributeDefinition("sk", ScalarAttributeType.S),
                new AttributeDefinition("seq", ScalarAttributeType.N),
            ],
            KeySchema = [new KeySchemaElement("pk", KeyType.HASH), new KeySchemaElement("sk", KeyType.RANGE)],
            LocalSecondaryIndexes =
            [
                new LocalSecondaryIndex
                {
                    IndexName = SeqIndexName,
                    KeySchema = [new KeySchemaElement("pk", KeyType.HASH), new KeySchemaElement("seq", KeyType.RANGE)],
                    // A pull reads the records straight from the index, so it holds all of each one.
                    Projection = new Projection { ProjectionType = ProjectionType.ALL },
                },
            ],
        },
        new()
        {
            TableName = CircleTableName(tablePrefix),
            BillingMode = BillingMode.PAY_PER_REQUEST,
            AttributeDefinitions =
            [
                new AttributeDefinition("pk", ScalarAttributeType.S),
                new AttributeDefinition("sk", ScalarAttributeType.S),
            ],
            KeySchema = [new KeySchemaElement("pk", KeyType.HASH), new KeySchemaElement("sk", KeyType.RANGE)],
        },
    ];

    /// <summary>
    /// Creates whichever of the two tables is missing, waits until both are active, and turns on time to live for
    /// the circle table. For local development and tests; production tables belong in the app's infrastructure code.
    /// </summary>
    /// <param name="client">The DynamoDB client.</param>
    /// <param name="tablePrefix">The app's table prefix.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    public static async Task CreateAsync(IAmazonDynamoDB client, string tablePrefix, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        foreach (var definition in Definitions(tablePrefix))
        {
            if (await StatusAsync(client, definition.TableName, cancellationToken).ConfigureAwait(false) is null)
            {
                await client.CreateTableAsync(definition, cancellationToken).ConfigureAwait(false);
            }
            while (await StatusAsync(client, definition.TableName, cancellationToken).ConfigureAwait(false) != TableStatus.ACTIVE)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }

        // DynamoDB refuses to turn time to live on twice, so a second run checks first.
        var circleTable = CircleTableName(tablePrefix);
        var timeToLive = await client.DescribeTimeToLiveAsync(new DescribeTimeToLiveRequest { TableName = circleTable }, cancellationToken)
            .ConfigureAwait(false);
        if (timeToLive.TimeToLiveDescription.TimeToLiveStatus == TimeToLiveStatus.DISABLED)
        {
            await client.UpdateTimeToLiveAsync(new UpdateTimeToLiveRequest
            {
                TableName = circleTable,
                TimeToLiveSpecification = new TimeToLiveSpecification { AttributeName = TimeToLiveAttribute, Enabled = true },
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<TableStatus?> StatusAsync(IAmazonDynamoDB client, string tableName, CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.DescribeTableAsync(tableName, cancellationToken).ConfigureAwait(false);
            return response.Table.TableStatus;
        }
        catch (ResourceNotFoundException)
        {
            return null;
        }
    }
}
