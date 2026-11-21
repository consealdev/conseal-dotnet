// SPDX-License-Identifier: AGPL-3.0-or-later OR LicenseRef-Commercial

using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace ConsealDev.Conseal.Storage.Dynamo;

/// <summary>
/// Circles in DynamoDB, one partition per user (see <see cref="ConsealSyncDynamoTables"/>). The circle item counts
/// the devices still in the circle, and every step that adds or revokes a device changes that count in the same
/// transaction, so the last device can't be revoked however the requests interleave.
/// </summary>
internal sealed class DynamoCircleStore(IAmazonDynamoDB client, IOptions<ConsealSyncDynamoOptions> options) : IConsealCircleStore
{
    private const string CircleKey = "circle";
    private const string DevicePrefix = "device#";
    private const string JoinPrefix = "join#";

    /// <summary>How long DynamoDB keeps a join request after it expires, before dropping it.</summary>
    private static readonly TimeSpan JoinRetention = TimeSpan.FromDays(1);

    private readonly string _table = ConsealSyncDynamoTables.CircleTableName(options.Value.TablePrefix);

    public async Task<string?> GetCommitmentAsync(string userId, CancellationToken cancellationToken)
    {
        var item = await GetItemAsync(userId, CircleKey, cancellationToken).ConfigureAwait(false);
        return item?["commitment"].S;
    }

    public async Task<bool> CreateCircleAsync(string userId, string aekCommitment, DeviceSave founder, CancellationToken cancellationToken)
    {
        var circle = Key(userId, CircleKey);
        circle["commitment"] = new AttributeValue { S = aekCommitment };
        circle["activeDevices"] = Number(1);

        for (var attempt = 0; attempt < DynamoTransactions.MaxAttempts; attempt++)
        {
            try
            {
                await client.TransactWriteItemsAsync(new TransactWriteItemsRequest
                {
                    TransactItems =
                    [
                        new TransactWriteItem { Put = new Put { TableName = _table, Item = circle, ConditionExpression = "attribute_not_exists(pk)" } },
                        new TransactWriteItem { Put = NewDevice(userId, founder) },
                    ],
                }, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (TransactionCanceledException exception)
                when (DynamoTransactions.FailedCondition(exception, 0) || DynamoTransactions.FailedCondition(exception, 1))
            {
                return false;
            }
            catch (TransactionCanceledException)
            {
                // Another transaction held one of the items.
            }
            await DynamoTransactions.BackOffAsync(attempt, cancellationToken).ConfigureAwait(false);
        }
        throw Busy();
    }

    public async Task<CircleDevice?> GetDeviceAsync(string userId, string deviceId, CancellationToken cancellationToken)
    {
        var item = await GetItemAsync(userId, DevicePrefix + deviceId, cancellationToken).ConfigureAwait(false);
        return item is null ? null : FromDeviceItem(item);
    }

    public async Task<IReadOnlyList<CircleDevice>> ListDevicesAsync(string userId, CancellationToken cancellationToken)
    {
        var items = await QueryPrefixAsync(userId, DevicePrefix, cancellationToken).ConfigureAwait(false);
        return items.Select(FromDeviceItem).ToList();
    }

    public async Task<DeviceSaveResult> SaveDeviceAsync(string userId, DeviceSave device, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < DynamoTransactions.MaxAttempts; attempt++)
        {
            var existing = await GetDeviceAsync(userId, device.DeviceId, cancellationToken).ConfigureAwait(false);
            if (existing?.RevokedAt is not null) return DeviceSaveResult.Revoked;

            if (existing is not null)
            {
                try
                {
                    await client.UpdateItemAsync(UpdateDevice(userId, device), cancellationToken).ConfigureAwait(false);
                    return DeviceSaveResult.Updated;
                }
                catch (ConditionalCheckFailedException)
                {
                    // Revoked in the meantime: read it again.
                }
            }
            else
            {
                try
                {
                    await client.TransactWriteItemsAsync(new TransactWriteItemsRequest
                    {
                        TransactItems = [CountDevice(userId, +1, condition: "attribute_exists(pk)"), new TransactWriteItem { Put = NewDevice(userId, device) }],
                    }, cancellationToken).ConfigureAwait(false);
                    return DeviceSaveResult.Added;
                }
                catch (TransactionCanceledException exception) when (DynamoTransactions.FailedCondition(exception, 0))
                {
                    return DeviceSaveResult.NoCircle;
                }
                catch (TransactionCanceledException)
                {
                    // Added at the same time, or another transaction held an item: read it again.
                }
            }
            await DynamoTransactions.BackOffAsync(attempt, cancellationToken).ConfigureAwait(false);
        }
        throw Busy();
    }

    public async Task<DeviceRevokeResult> RevokeDeviceAsync(string userId, string deviceId, DateTimeOffset revokedAt, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < DynamoTransactions.MaxAttempts; attempt++)
        {
            var revoke = new Update
            {
                TableName = _table,
                Key = Key(userId, DevicePrefix + deviceId),
                UpdateExpression = "SET revokedAt = :at REMOVE wrappedAek",
                ConditionExpression = "attribute_exists(pk) AND attribute_not_exists(revokedAt)",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":at"] = Time(revokedAt) },
            };
            try
            {
                await client.TransactWriteItemsAsync(new TransactWriteItemsRequest
                {
                    TransactItems = [CountDevice(userId, -1, condition: "activeDevices > :one"), new TransactWriteItem { Update = revoke }],
                }, cancellationToken).ConfigureAwait(false);
                return DeviceRevokeResult.Revoked;
            }
            catch (TransactionCanceledException exception) when (DynamoTransactions.FailedCondition(exception, 1))
            {
                var device = await GetDeviceAsync(userId, deviceId, cancellationToken).ConfigureAwait(false);
                if (device is null) return DeviceRevokeResult.NotFound;
                if (device.RevokedAt is not null) return DeviceRevokeResult.AlreadyRevoked;
                // Changed in the meantime: try again.
            }
            catch (TransactionCanceledException exception) when (DynamoTransactions.FailedCondition(exception, 0))
            {
                return DeviceRevokeResult.LastDevice;
            }
            catch (TransactionCanceledException)
            {
                // Another transaction held one of the items.
            }
            await DynamoTransactions.BackOffAsync(attempt, cancellationToken).ConfigureAwait(false);
        }
        throw Busy();
    }

    public Task AddJoinAsync(string userId, JoinChallenge join, CancellationToken cancellationToken) =>
        client.PutItemAsync(new PutItemRequest { TableName = _table, Item = JoinItem(userId, join) }, cancellationToken);

    public async Task<JoinChallenge?> GetJoinAsync(string userId, string challengeId, CancellationToken cancellationToken)
    {
        var item = await GetItemAsync(userId, JoinPrefix + challengeId, cancellationToken).ConfigureAwait(false);
        return item is null ? null : FromJoinItem(item);
    }

    public async Task<IReadOnlyList<JoinChallenge>> ListJoinsAsync(string userId, CancellationToken cancellationToken)
    {
        var items = await QueryPrefixAsync(userId, JoinPrefix, cancellationToken).ConfigureAwait(false);
        return items.Select(FromJoinItem).OrderBy(join => join.ReceivedAt).ToList();
    }

    public async Task<bool> ReplaceJoinAsync(string userId, JoinChallenge join, JoinState expected, CancellationToken cancellationToken)
    {
        try
        {
            await client.PutItemAsync(new PutItemRequest
            {
                TableName = _table,
                Item = JoinItem(userId, join),
                ConditionExpression = "#state = :expected",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#state"] = "state" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":expected"] = new() { S = StateText(expected) } },
            }, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    public Task DeleteJoinAsync(string userId, string challengeId, CancellationToken cancellationToken) =>
        client.DeleteItemAsync(new DeleteItemRequest { TableName = _table, Key = Key(userId, JoinPrefix + challengeId) }, cancellationToken);

    private async Task<Dictionary<string, AttributeValue>?> GetItemAsync(string userId, string sortKey, CancellationToken cancellationToken)
    {
        var response = await client.GetItemAsync(new GetItemRequest
        {
            TableName = _table,
            Key = Key(userId, sortKey),
            ConsistentRead = true,
        }, cancellationToken).ConfigureAwait(false);
        return response.Item is { Count: > 0 } item ? item : null;
    }

    private async Task<List<Dictionary<string, AttributeValue>>> QueryPrefixAsync(string userId, string prefix, CancellationToken cancellationToken)
    {
        var items = new List<Dictionary<string, AttributeValue>>();
        Dictionary<string, AttributeValue>? start = null;
        do
        {
            var response = await client.QueryAsync(new QueryRequest
            {
                TableName = _table,
                ConsistentRead = true,
                KeyConditionExpression = "pk = :pk AND begins_with(sk, :prefix)",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":pk"] = new() { S = userId },
                    [":prefix"] = new() { S = prefix },
                },
                ExclusiveStartKey = start,
            }, cancellationToken).ConfigureAwait(false);
            items.AddRange(response.Items);
            start = response.LastEvaluatedKey is { Count: > 0 } next ? next : null;
        }
        while (start is not null);
        return items;
    }

    private TransactWriteItem CountDevice(string userId, int change, string condition)
    {
        var values = new Dictionary<string, AttributeValue> { [":change"] = Number(change) };
        if (condition.Contains(":one", StringComparison.Ordinal)) values[":one"] = Number(1);
        return new TransactWriteItem
        {
            Update = new Update
            {
                TableName = _table,
                Key = Key(userId, CircleKey),
                UpdateExpression = "SET activeDevices = activeDevices + :change",
                ConditionExpression = condition,
                ExpressionAttributeValues = values,
            },
        };
    }

    private Put NewDevice(string userId, DeviceSave device)
    {
        var item = Key(userId, DevicePrefix + device.DeviceId);
        item["deviceId"] = new AttributeValue { S = device.DeviceId };
        item["wrappedAek"] = new AttributeValue { S = device.WrappedAek };
        item["registeredAt"] = Time(device.At);
        if (device.Name is not null) item["name"] = new AttributeValue { S = device.Name };
        if (device.Platform is not null) item["platform"] = new AttributeValue { S = device.Platform };
        return new Put { TableName = _table, Item = item, ConditionExpression = "attribute_not_exists(pk)" };
    }

    private UpdateItemRequest UpdateDevice(string userId, DeviceSave device)
    {
        var set = new List<string> { "wrappedAek = :wrappedAek" };
        var names = new Dictionary<string, string>();
        var values = new Dictionary<string, AttributeValue> { [":wrappedAek"] = new() { S = device.WrappedAek } };
        if (device.Name is not null)
        {
            // "name" is a DynamoDB reserved word.
            set.Add("#name = :name");
            names["#name"] = "name";
            values[":name"] = new AttributeValue { S = device.Name };
        }
        if (device.Platform is not null)
        {
            set.Add("platform = :platform");
            values[":platform"] = new AttributeValue { S = device.Platform };
        }

        var request = new UpdateItemRequest
        {
            TableName = _table,
            Key = Key(userId, DevicePrefix + device.DeviceId),
            UpdateExpression = "SET " + string.Join(", ", set),
            ConditionExpression = "attribute_exists(pk) AND attribute_not_exists(revokedAt)",
            ExpressionAttributeValues = values,
        };
        if (names.Count > 0) request.ExpressionAttributeNames = names;
        return request;
    }

    private Dictionary<string, AttributeValue> JoinItem(string userId, JoinChallenge join)
    {
        var item = Key(userId, JoinPrefix + join.ChallengeId);
        item["challengeId"] = new AttributeValue { S = join.ChallengeId };
        item["deviceId"] = new AttributeValue { S = join.DeviceId };
        item["request"] = new AttributeValue { S = join.Request };
        item["state"] = new AttributeValue { S = StateText(join.State) };
        item["receivedAt"] = Time(join.ReceivedAt);
        item["expiresAt"] = Time(join.ExpiresAt);
        item[ConsealSyncDynamoTables.TimeToLiveAttribute] = Number((join.ExpiresAt + JoinRetention).ToUnixTimeSeconds());
        if (join.SealedAek is not null) item["sealedAek"] = new AttributeValue { S = join.SealedAek };
        if (join.PassphraseCheck is not null) item["passphraseCheck"] = new AttributeValue { S = join.PassphraseCheck };
        return item;
    }

    private static CircleDevice FromDeviceItem(Dictionary<string, AttributeValue> item) => new(
        item["deviceId"].S,
        item.TryGetValue("wrappedAek", out var wrappedAek) ? wrappedAek.S : null,
        item.TryGetValue("name", out var name) ? name.S : null,
        item.TryGetValue("platform", out var platform) ? platform.S : null,
        ParseTime(item["registeredAt"]),
        item.TryGetValue("revokedAt", out var revokedAt) ? ParseTime(revokedAt) : null);

    private static JoinChallenge FromJoinItem(Dictionary<string, AttributeValue> item) => new(
        item["challengeId"].S,
        item["deviceId"].S,
        item["request"].S,
        Enum.Parse<JoinState>(item["state"].S, ignoreCase: true),
        ParseTime(item["receivedAt"]),
        ParseTime(item["expiresAt"]),
        item.TryGetValue("sealedAek", out var sealedAek) ? sealedAek.S : null,
        item.TryGetValue("passphraseCheck", out var check) ? check.S : null);

    private static string StateText(JoinState state) => state.ToString().ToLowerInvariant();

    private static Dictionary<string, AttributeValue> Key(string userId, string sortKey) => new()
    {
        ["pk"] = new AttributeValue { S = userId },
        ["sk"] = new AttributeValue { S = sortKey },
    };

    private static AttributeValue Number(long value) => new() { N = value.ToString(CultureInfo.InvariantCulture) };

    private static AttributeValue Time(DateTimeOffset value) => new() { S = value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) };

    private static DateTimeOffset ParseTime(AttributeValue value) =>
        DateTimeOffset.Parse(value.S, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static ConsealSyncStoreBusyException Busy() =>
        new($"Other writes to the circle kept a write from committing in {DynamoTransactions.MaxAttempts} attempts.");
}
