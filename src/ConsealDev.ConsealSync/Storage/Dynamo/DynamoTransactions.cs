using Amazon.DynamoDBv2.Model;

namespace ConsealDev.ConsealSync.Storage.Dynamo;

/// <summary>What the stores need to read a cancelled transaction, and to retry one.</summary>
internal static class DynamoTransactions
{
    /// <summary>How many times a store tries a write that other writes keep getting in the way of.</summary>
    public const int MaxAttempts = 25;

    private const string ConditionalCheckFailed = "ConditionalCheckFailed";
    private const int MaxDelayMilliseconds = 200;

    /// <summary>Whether the transaction's item at <paramref name="index"/> failed its condition.</summary>
    public static bool FailedCondition(TransactionCanceledException exception, int index) =>
        exception.CancellationReasons is { } reasons && index < reasons.Count && reasons[index].Code == ConditionalCheckFailed;

    /// <summary>The item at <paramref name="index"/> as it stood when its condition failed, if DynamoDB returned it.</summary>
    public static Dictionary<string, AttributeValue>? OldItem(TransactionCanceledException exception, int index) =>
        exception.CancellationReasons is { } reasons && index < reasons.Count && reasons[index].Item is { Count: > 0 } item ? item : null;

    /// <summary>A short random wait that grows with each attempt, so writers that collided don't collide again.</summary>
    public static Task BackOffAsync(int attempt, CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(1, Math.Min(MaxDelayMilliseconds, 10 * (attempt + 1)) + 1)), cancellationToken);
}
