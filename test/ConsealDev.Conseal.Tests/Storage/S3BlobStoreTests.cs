using System.Net;
using System.Security.Cryptography;
using Amazon.S3;
using ConsealDev.Conseal.Configuration;
using ConsealDev.Conseal.Storage;
using ConsealDev.Conseal.Storage.S3;
using ConsealDev.Conseal.Tests.TestSupport;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ConsealDev.Conseal.Tests.Storage;

/// <summary>The S3 store against a real S3-compatible store, which checks every presigned signature.</summary>
public sealed class S3BlobStoreTests : IAsyncLifetime
{
    private static readonly HttpClient Http = new();

    private readonly S3Bucket _bucket = new();
    private S3BlobStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await _bucket.CreateAsync();
        var options = new ConsealSyncBlobOptions();
        _bucket.Configure(options);
        options.KeyPrefix = "blobs/";
        _store = new S3BlobStore(_bucket.Client, Options.Create(options));
    }

    public ValueTask DisposeAsync() => _bucket.DisposeAsync();

    [Fact]
    public async Task A_blob_uploaded_through_its_presigned_URL_downloads_through_another()
    {
        var key = Key("user-1", Items.NewId());
        var sealedBlob = RandomNumberGenerator.GetBytes(1000);

        var upload = await _store.CreateUploadUrlAsync(key, sealedBlob.Length, Soon(), CancellationToken.None);
        var put = await SendAsync(HttpMethod.Put, upload, sealedBlob);
        var download = await _store.CreateDownloadUrlAsync(key, Soon(), CancellationToken.None);
        var got = await SendAsync(HttpMethod.Get, download);

        put.StatusCode.Should().Be(HttpStatusCode.OK);
        (await got.Content.ReadAsByteArrayAsync()).Should().Equal(sealedBlob);
        upload.Url.Should().Contain("X-Amz-SignedHeaders=content-length%3Bhost");
        (await _bucket.KeysAsync()).Should().Equal($"blobs/user-1/{TestHost.Notes}/{key.RecordId}/{key.BlobId}");
    }

    [Fact]
    public async Task An_upload_of_another_size_than_signed_is_refused_and_stores_nothing()
    {
        var key = Key("user-1", Items.NewId());
        var upload = await _store.CreateUploadUrlAsync(key, 100, Soon(), CancellationToken.None);

        var larger = await SendAsync(HttpMethod.Put, upload, new byte[101]);

        larger.IsSuccessStatusCode.Should().BeFalse();
        (await _bucket.KeysAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_deleted_blob_is_gone_and_deleting_it_again_succeeds()
    {
        var key = Key("user-1", Items.NewId());
        await UploadAsync(key);

        await _store.DeleteAsync(key, CancellationToken.None);
        await _store.DeleteAsync(key, CancellationToken.None);

        var got = await SendAsync(HttpMethod.Get, await _store.CreateDownloadUrlAsync(key, Soon(), CancellationToken.None));
        got.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deleting_a_record_s_blobs_leaves_other_records_and_other_users_blobs()
    {
        var recordId = Items.NewId();
        var mine = new[] { Key("user-1", recordId), Key("user-1", recordId) };
        // A record id that begins with the first one's: the prefix ends in a slash, so it isn't caught.
        var others = new[] { Key("user-1", Items.NewId()), Key("user-2", recordId), Key("user-1", recordId + "0") };
        foreach (var key in mine.Concat(others)) await UploadAsync(key);

        await _store.DeleteRecordBlobsAsync("user-1", TestHost.Notes, recordId, CancellationToken.None);

        (await _bucket.KeysAsync()).Should().BeEquivalentTo(others.Select(k => $"blobs/{k.UserId}/{k.CollectionId}/{k.RecordId}/{k.BlobId}"));
    }

    [Fact]
    public async Task Through_the_routes_a_blob_goes_up_and_comes_back_and_a_tombstone_deletes_it()
    {
        await using var host = await TestHost.StartAsync(sync =>
        {
            sync.AddInMemoryStores().WithBlobs(_bucket.Configure);
            sync.Services.AddSingleton(_bucket.Client);
        });
        // Storage checks a URL's expiry against the real clock.
        host.Time.Advance(DateTimeOffset.UtcNow - host.Time.Now);
        var client = host.Client();
        var (recordId, blobId) = (Items.NewId(), Items.NewId());
        var path = $"/sync/{TestHost.Notes}/{recordId}/blobs/{blobId}";
        var sealedBlob = RandomNumberGenerator.GetBytes(28 + 500);

        var upload = await client.PostAsync(path, new { size = sealedBlob.Length });
        var put = await SendAsync(HttpMethod.Put, Url(upload), sealedBlob);
        var created = await client.PostAsync($"/sync/{TestHost.Notes}", Items.Push(Items.Write(recordId, null)));
        var download = await client.GetAsync(path);
        var got = await SendAsync(HttpMethod.Get, Url(download));
        await client.PostAsync($"/sync/{TestHost.Notes}", Items.Push(Items.Delete(recordId, created.Body!["results"]![0]!["version"]!.GetValue<string>())));

        put.StatusCode.Should().Be(HttpStatusCode.OK);
        (await got.Content.ReadAsByteArrayAsync()).Should().Equal(sealedBlob);
        (await _bucket.KeysAsync()).Should().BeEmpty();
    }

    private static BlobKey Key(string userId, string recordId) => new(userId, TestHost.Notes, recordId, Items.NewId());

    private static DateTimeOffset Soon() => DateTimeOffset.UtcNow.AddMinutes(5);

    private async Task UploadAsync(BlobKey key)
    {
        var put = await SendAsync(HttpMethod.Put, await _store.CreateUploadUrlAsync(key, 28, Soon(), CancellationToken.None), new byte[28]);
        put.EnsureSuccessStatusCode();
    }

    private static PresignedBlobUrl Url(Reply reply) => new(
        reply.Body!["url"]!.GetValue<string>(),
        reply.Body["headers"]!.AsObject().ToDictionary(h => h.Key, h => h.Value!.GetValue<string>()));

    /// <summary>Sends a presigned request as a client would, with the headers it came with and nothing of the app's.</summary>
    private static async Task<HttpResponseMessage> SendAsync(HttpMethod method, PresignedBlobUrl url, byte[]? body = null)
    {
        using var request = new HttpRequestMessage(method, url.Url);
        if (body is not null) request.Content = new ByteArrayContent(body);
        foreach (var (name, value) in url.Headers) request.Headers.TryAddWithoutValidation(name, value);
        var response = await Http.SendAsync(request);
        await response.Content.LoadIntoBufferAsync();
        return response;
    }
}
