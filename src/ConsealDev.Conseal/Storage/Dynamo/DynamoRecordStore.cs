using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using ConsealDev.Conseal.Wire;
using Microsoft.Extensions.Options;

namespace ConsealDev.Conseal.Storage.Dynamo;

/// <summary>
/// Records in DynamoDB, one partition per user and collection (see <see cref="ConsealSyncDynamoTables"/>).
/// </summary>
/// <remarks>
/// <para>
/// A write reads the partition's counter, then commits the record and the counter's next value in one transaction,
/// on condition that neither moved. Two writes to one partition can't both commit at the same counter value, so
/// <c>seq</c> follows commit order, and the loser retries.
/// </para>
/// <para>
/// A pull reads the counter first, then pages the index up to that value only. Every write up to it has committed
/// before the read starts, so a strongly consistent read of the index sees each of them, unless a later write
/// replaced the record, and that one's <c>seq</c> is past the bound. A write that commits while the page is read lands
/// past the bound too, so the read never shows it without the writes before it.
/// </para>
/// </remarks>
internal sealed class DynamoRecordStore(
    IAmazonDynamoDB client,
    IOptions<ConsealSyncDynamoOptions> options,
    TimeProvider time) : IConsealSyncRecordStore
{
    /// <summary>
    /// Half of DynamoDB's 400 KB item limit, less 2 KB. The <c>seq</c> index holds a copy of every record, and DynamoDB
    /// counts that copy against the same 400 KB, so each copy gets half. The 2 KB leaves room for the rest of the item:
    /// the keys with a user id of up to 256 bytes, the version, <c>seq</c>, IV, wrapped DEK and attribute names take
    /// under 1 KB.
    /// </summary>
    public const int RecordLimitBytes = 200 * 1024 - 2 * 1024;

    private const string ValidationError = "ValidationException";

    private const string CounterKey = "#counter";

    private readonly string _table = ConsealSyncDynamoTables.RecordsTableName(options.Value.TablePrefix);

    public int MaxRecordBytes => RecordLimitBytes;

    public async Task<RecordWriteResult> WriteAsync(string userId, string collectionId, RecordWrite write, CancellationToken cancellationToken)
    {
        var partition = PartitionKey(userId, collectionId);
        for (var attempt = 0; attempt < DynamoTransactions.MaxAttempts; attempt++)
        {
            var head = await ReadHeadAsync(partition, cancellationToken).ConfigureAwait(false);
            var seq = head + 1;
            var version = Ids.NewRandomId();
            var request = new TransactWriteItemsRequest
            {
                // The version is new on every attempt, so an SDK retry of this request can't apply it twice.
                ClientRequestToken = version,
                TransactItems = [AdvanceCounter(partition, head, seq), PutRecord(partition, write, version, seq)],
            };

            try
            {
                await client.TransactWriteItemsAsync(request, cancellationToken).ConfigureAwait(false);
                return new RecordWriteResult.Written(version, seq);
            }
            catch (TransactionCanceledException exception) when (DynamoTransactions.FailedCondition(exception, 1))
            {
                var old = DynamoTransactions.OldItem(exception, 1);
                var current = old is null ? await GetRecordAsync(partition, write.Id, cancellationToken).ConfigureAwait(false) : FromItem(old);
                return new RecordWriteResult.Conflict(current);
            }
            catch (TransactionCanceledException)
            {
                // Another write took the counter's next value, or held one of the items: read the counter again.
            }
            catch (AmazonDynamoDBException exception) when (IsItemTooLarge(exception))
            {
                return new RecordWriteResult.TooLarge();
            }
            await DynamoTransactions.BackOffAsync(attempt, cancellationToken).ConfigureAwait(false);
        }
        throw new ConsealSyncStoreBusyException(
            $"Other writes to the collection kept a write from committing in {DynamoTransactions.MaxAttempts} attempts.");
    }

    public async Task<RecordPullResult> PullAsync(string userId, string collectionId, long afterSeq, int limit, CancellationToken cancellationToken)
    {
        var partition = PartitionKey(userId, collectionId);
        var head = await ReadHeadAsync(partition, cancellationToken).ConfigureAwait(false);
        if (afterSeq > head) return new RecordPullResult.BeyondHead();
        if (afterSeq == head) return new RecordPullResult.Page([], head, HasMore: false);

        var response = await client.QueryAsync(new QueryRequest
        {
            TableName = _table,
            IndexName = ConsealSyncDynamoTables.SeqIndexName,
            ConsistentRead = true,
            KeyConditionExpression = "pk = :pk AND seq BETWEEN :from AND :to",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new() { S = partition },
                [":from"] = Number(afterSeq + 1),
                [":to"] = Number(head),
            },
            Limit = limit,
        }, cancellationToken).ConfigureAwait(false);

        var items = response.Items.Select(FromItem).ToList();
        // DynamoDB also stops a page at 1 MB, so a short page can still have more after it.
        var hasMore = response.LastEvaluatedKey is { Count: > 0 } && items.Count > 0;
        return new RecordPullResult.Page(items, hasMore ? items[^1].Seq : head, hasMore);
    }

    private async Task<long> ReadHeadAsync(string partition, CancellationToken cancellationToken)
    {
        var response = await client.GetItemAsync(new GetItemRequest
        {
            TableName = _table,
            Key = Key(partition, CounterKey),
            ConsistentRead = true,
            ProjectionExpression = "lastSeq",
        }, cancellationToken).ConfigureAwait(false);
        return response.Item is { Count: > 0 } item && item.TryGetValue("lastSeq", out var lastSeq)
            ? long.Parse(lastSeq.N, CultureInfo.InvariantCulture)
            : 0;
    }

    private async Task<StoredRecord?> GetRecordAsync(string partition, string recordId, CancellationToken cancellationToken)
    {
        var response = await client.GetItemAsync(new GetItemRequest
        {
            TableName = _table,
            Key = Key(partition, recordId),
            ConsistentRead = true,
        }, cancellationToken).ConfigureAwait(false);
        return response.Item is { Count: > 0 } item ? FromItem(item) : null;
    }

    private TransactWriteItem AdvanceCounter(string partition, long head, long seq)
    {
        var values = new Dictionary<string, AttributeValue> { [":next"] = Number(seq) };
        if (head > 0) values[":head"] = Number(head);
        return new TransactWriteItem
        {
            Update = new Update
            {
                TableName = _table,
                Key = Key(partition, CounterKey),
                UpdateExpression = "SET lastSeq = :next",
                ConditionExpression = head > 0 ? "lastSeq = :head" : "attribute_not_exists(lastSeq)",
                ExpressionAttributeValues = values,
            },
        };
    }

    private TransactWriteItem PutRecord(string partition, RecordWrite write, string version, long seq)
    {
        var item = Key(partition, write.Id);
        item["seq"] = Number(seq);
        item["version"] = new AttributeValue { S = version };
        if (write.Content is { } content)
        {
            item["ciphertext"] = new AttributeValue { B = new MemoryStream(content.Ciphertext) };
            item["iv"] = new AttributeValue { B = new MemoryStream(content.Iv) };
            item["wrappedDek"] = new AttributeValue { B = new MemoryStream(content.WrappedDek) };
        }
        else
        {
            // A tombstone keeps the row and drops the ciphertext, so other devices learn of the delete.
            item["deletedAt"] = new AttributeValue { S = time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture) };
        }

        var put = new Put
        {
            TableName = _table,
            Item = item,
            ConditionExpression = write.BaseVersion is null ? "attribute_not_exists(pk)" : "version = :base",
            ReturnValuesOnConditionCheckFailure = ReturnValuesOnConditionCheckFailure.ALL_OLD,
        };
        if (write.BaseVersion is not null)
        {
            put.ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":base"] = new() { S = write.BaseVersion } };
        }
        return new TransactWriteItem { Put = put };
    }

    private static StoredRecord FromItem(Dictionary<string, AttributeValue> item)
    {
        var content = item.TryGetValue("ciphertext", out var ciphertext)
            ? new SealedContent(ciphertext.B.ToArray(), item["iv"].B.ToArray(), item["wrappedDek"].B.ToArray())
            : null;
        DateTimeOffset? deletedAt = item.TryGetValue("deletedAt", out var deleted)
            ? DateTimeOffset.Parse(deleted.S, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            : null;
        return new StoredRecord(
            item["sk"].S,
            content,
            item["version"].S,
            long.Parse(item["seq"].N, CultureInfo.InvariantCulture),
            deletedAt);
    }

    /// <summary>DynamoDB names no error code of its own for an item over its size limit, only a message.</summary>
    private static bool IsItemTooLarge(AmazonDynamoDBException exception) =>
        exception.ErrorCode == ValidationError && exception.Message.Contains("item size", StringComparison.OrdinalIgnoreCase);

    private static string PartitionKey(string userId, string collectionId) => $"{userId}#{collectionId}";

    private static Dictionary<string, AttributeValue> Key(string partition, string sortKey) => new()
    {
        ["pk"] = new AttributeValue { S = partition },
        ["sk"] = new AttributeValue { S = sortKey },
    };

    private static AttributeValue Number(long value) => new() { N = value.ToString(CultureInfo.InvariantCulture) };
}
