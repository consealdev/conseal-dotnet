using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using ConsealDev.ConsealSync.Extensions;
using ConsealDev.ConsealSync.Storage.Dynamo;
using Microsoft.Extensions.Options;

namespace ConsealDev.ConsealSync.Tests.TestSupport;

/// <summary>
/// Both tables in DynamoDB Local, under a prefix of their own so tests never share data, deleted afterwards. The
/// endpoint comes from <c>CONSEAL_SYNC_DYNAMODB_URL</c>; without it a test is skipped, except in CI, where it fails,
/// since CI runs DynamoDB Local beside the tests.
/// </summary>
internal sealed class DynamoTables : IAsyncDisposable
{
    private const string UrlVariable = "CONSEAL_SYNC_DYNAMODB_URL";

    private IAmazonDynamoDB? _client;

    public string Url { get; private set; } = string.Empty;

    public string Prefix { get; private set; } = string.Empty;

    public IAmazonDynamoDB Client => _client ?? throw new InvalidOperationException("Create the tables first.");

    public IOptions<ConsealSyncDynamoOptions> StoreOptions =>
        Options.Create(new ConsealSyncDynamoOptions { ServiceUrl = Url, TablePrefix = Prefix });

    public async Task CreateAsync()
    {
        Url = RequireUrl();
        _client = new AmazonDynamoDBClient(new BasicAWSCredentials("local", "local"), new AmazonDynamoDBConfig { ServiceURL = Url });
        Prefix = $"test-{Guid.NewGuid():N}-";
        await ConsealSyncDynamoTables.CreateAsync(_client, Prefix, CancellationToken.None);
    }

    /// <summary>Points a test host's stores at these tables.</summary>
    public void Register(ConsealSyncBuilder sync) => sync.AddDynamoStores(options =>
    {
        options.ServiceUrl = Url;
        options.TablePrefix = Prefix;
    });

    public async ValueTask DisposeAsync()
    {
        if (_client is null) return;
        foreach (var name in new[] { ConsealSyncDynamoTables.RecordsTableName(Prefix), ConsealSyncDynamoTables.CircleTableName(Prefix) })
        {
            try
            {
                await _client.DeleteTableAsync(name);
            }
            catch (ResourceNotFoundException)
            {
                // Never created: nothing to delete.
            }
        }
        _client.Dispose();
    }

    private static string RequireUrl()
    {
        if (Environment.GetEnvironmentVariable(UrlVariable) is { Length: > 0 } url) return url;
        if (Environment.GetEnvironmentVariable("CI") == "true")
        {
            throw new InvalidOperationException($"{UrlVariable} is not set, and CI must run the DynamoDB tests.");
        }
        Assert.Skip($"Set {UrlVariable} to a DynamoDB Local endpoint to run the DynamoDB tests.");
        return string.Empty;
    }
}
