namespace ConsealDev.Conseal.Storage.Dynamo;

/// <summary>Where the DynamoDB stores find their tables.</summary>
public sealed class ConsealSyncDynamoOptions
{
    /// <summary>
    /// Put before both table names, such as <c>pinner-</c> for <c>pinner-conseal-sync-records</c>. Include the
    /// separator. Each app deploys its own tables from the same definition, never a shared one.
    /// </summary>
    public string TablePrefix { get; set; } = string.Empty;

    /// <summary>The DynamoDB endpoint, for DynamoDB Local in development; empty in production.</summary>
    public string? ServiceUrl { get; set; }
}
