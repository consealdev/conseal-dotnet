using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using ConsealDev.Conseal.Configuration;

namespace ConsealDev.Conseal.Tests.TestSupport;

/// <summary>
/// A bucket of its own in a local S3-compatible store, such as LocalStack, emptied and deleted afterwards. The
/// endpoint comes from <c>CONSEAL_SYNC_S3_URL</c>; without it a test is skipped, except in CI, where it fails, since
/// CI runs LocalStack beside the tests.
/// </summary>
internal sealed class S3Bucket : IAsyncDisposable
{
    private const string UrlVariable = "CONSEAL_SYNC_S3_URL";

    private AmazonS3Client? _client;

    public string Url { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public IAmazonS3 Client => _client ?? throw new InvalidOperationException("Create the bucket first.");

    /// <summary>Blob options for this bucket, with blobs for <see cref="TestHost.Notes"/>.</summary>
    public void Configure(ConsealSyncBlobOptions options)
    {
        options.BucketName = Name;
        options.ServiceUrl = Url;
        options.Collections.Add(TestHost.Notes);
    }

    public async Task CreateAsync()
    {
        Url = RequireUrl();
        // LocalStack takes any access key with the secret "test", and checks signatures against it.
        _client = new AmazonS3Client(
            new BasicAWSCredentials("test", "test"),
            new AmazonS3Config { ServiceURL = Url, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
        Name = $"test-{Guid.NewGuid():N}";
        await _client.PutBucketAsync(Name);
    }

    public async Task<IReadOnlyList<string>> KeysAsync()
    {
        var response = await Client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = Name });
        return response.S3Objects?.Select(o => o.Key).Order(StringComparer.Ordinal).ToList() ?? [];
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is null) return;
        try
        {
            foreach (var key in await KeysAsync()) await _client.DeleteObjectAsync(Name, key);
            await _client.DeleteBucketAsync(Name);
        }
        catch (AmazonS3Exception)
        {
            // Never created: nothing to delete.
        }
        _client.Dispose();
    }

    private static string RequireUrl()
    {
        if (Environment.GetEnvironmentVariable(UrlVariable) is { Length: > 0 } url) return url;
        if (Environment.GetEnvironmentVariable("CI") == "true")
        {
            throw new InvalidOperationException($"{UrlVariable} is not set, and CI must run the S3 tests.");
        }
        Assert.Skip($"Set {UrlVariable} to a LocalStack or other S3-compatible endpoint to run the S3 tests.");
        return string.Empty;
    }
}
