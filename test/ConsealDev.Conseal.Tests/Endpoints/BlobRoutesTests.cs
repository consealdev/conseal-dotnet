using System.Net;
using ConsealDev.Conseal.Extensions;
using ConsealDev.Conseal.Storage;
using ConsealDev.Conseal.Tests.TestSupport;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ConsealDev.Conseal.Tests.Endpoints;

public sealed class BlobRoutesTests : IAsyncLifetime
{
    private const string Notes = TestHost.Notes;
    private const string Lists = TestHost.Lists;

    private readonly RecordingBlobStore _store = new();
    private TestHost _host = null!;
    private ApiClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await TestHost.StartAsync(sync => WithRecordingBlobs(sync, _store));
        _client = _host.Client();
    }

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task An_app_without_WithBlobs_answers_every_blob_route_with_404_blobs_disabled()
    {
        await using var host = await TestHost.StartAsync();
        var client = host.Client();
        var path = BlobPath(Notes, Items.NewId(), Items.NewId());

        var replies = new[] { await client.PostAsync(path, new { size = 100 }), await client.GetAsync(path), await client.DeleteAsync(path) };
        var config = await client.GetAsync("/sync");

        replies.Select(r => (r.Status, r.Code)).Should().AllBeEquivalentTo((HttpStatusCode.NotFound, "blobs_disabled"));
        config.Body!["collections"]!.AsArray().Select(c => c!["maxBlobBytes"]).Should().AllSatisfy(max => max.Should().BeNull());
    }

    [Fact]
    public async Task Config_gives_maxBlobBytes_to_the_collections_with_blobs_only()
    {
        var reply = await _client.GetAsync("/sync");

        var limits = reply.Body!["collections"]!.AsArray().ToDictionary(c => c!["id"]!.GetValue<string>(), c => c!["maxBlobBytes"]?.GetValue<long>());
        limits.Should().Equal(new Dictionary<string, long?> { ["keyring"] = null, [Notes] = 10 * 1024 * 1024, [Lists] = null });
    }

    [Fact]
    public async Task An_upload_URL_is_signed_for_the_size_asked_and_works_for_the_URL_lifetime()
    {
        var (recordId, blobId) = (Items.NewId(), Items.NewId());

        var reply = await _client.PostAsync(BlobPath(Notes, recordId, blobId), new { size = 1234 });

        reply.Status.Should().Be(HttpStatusCode.OK);
        reply.Body!["url"]!.GetValue<string>().Should().Be($"https://storage.test/user-1/{Notes}/{recordId}/{blobId}?put=1234");
        reply.Body["method"]!.GetValue<string>().Should().Be("PUT");
        reply.Body["headers"]!["x-test"]!.GetValue<string>().Should().Be("signed");
        reply.Body["expiresAt"]!.GetValue<DateTimeOffset>().Should().Be(_host.Time.Now.AddMinutes(5));
        _store.Calls.Should().Equal($"upload user-1/{Notes}/{recordId}/{blobId} 1234");
    }

    [Fact]
    public async Task A_download_URL_and_a_delete_go_to_the_blob_named()
    {
        var (recordId, blobId) = (Items.NewId(), Items.NewId());
        var path = BlobPath(Notes, recordId, blobId);

        var download = await _client.GetAsync(path);
        var deleted = await _client.DeleteAsync(path);

        download.Body!["method"]!.GetValue<string>().Should().Be("GET");
        download.Body["url"]!.GetValue<string>().Should().Be($"https://storage.test/user-1/{Notes}/{recordId}/{blobId}?get");
        deleted.Status.Should().Be(HttpStatusCode.NoContent);
        _store.Calls.Should().Equal($"download user-1/{Notes}/{recordId}/{blobId}", $"delete user-1/{Notes}/{recordId}/{blobId}");
    }

    [Fact]
    public async Task Each_user_s_blobs_are_kept_under_their_own_id()
    {
        var path = BlobPath(Notes, Items.NewId(), Items.NewId());

        await _host.Client("user-2").GetAsync(path);

        _store.Calls.Single().Should().StartWith("download user-2/");
    }

    [Fact]
    public async Task A_blob_over_maxBlobBytes_answers_413_blob_too_large()
    {
        var reply = await _client.PostAsync(BlobPath(Notes, Items.NewId(), Items.NewId()), new { size = 10 * 1024 * 1024 + 1 });

        reply.Status.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        reply.Code.Should().Be("blob_too_large");
        _store.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{"size":27}""")]
    [InlineData("""{"size":"100"}""")]
    [InlineData("""{"size":1.5}""")]
    [InlineData("""{}""")]
    [InlineData("""[100]""")]
    [InlineData("""not json""")]
    public async Task An_upload_request_that_isnt_a_sealed_size_answers_400(string body)
    {
        var reply = await _client.PostRawAsync(BlobPath(Notes, Items.NewId(), Items.NewId()), body);

        reply.Status.Should().Be(HttpStatusCode.BadRequest);
        reply.Code.Should().Be("invalid_request");
        _store.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(Lists, "blobs_disabled")]
    [InlineData("keyring", "blobs_disabled")]
    [InlineData("notes", "unknown_collection")]
    public async Task A_collection_without_blobs_answers_404(string collectionId, string code)
    {
        var reply = await _client.GetAsync(BlobPath(collectionId, Items.NewId(), Items.NewId()));

        reply.Status.Should().Be(HttpStatusCode.NotFound);
        reply.Code.Should().Be(code);
    }

    [Theory]
    [InlineData("3C9D2E7A-1B4F-4A6E-8C5D-9F0E1A2B3C4D", "8e7f6a5b-4c3d-4e2f-9a1b-0c9d8e7f6a5b")]
    [InlineData("3c9d2e7a-1b4f-4a6e-8c5d-9f0e1a2b3c4d", "photo.jpg")]
    [InlineData("3c9d2e7a-1b4f-7a6e-8c5d-9f0e1a2b3c4d", "8e7f6a5b-4c3d-4e2f-9a1b-0c9d8e7f6a5b")]
    public async Task Ids_that_arent_lowercase_version_4_UUIDs_answer_400(string recordId, string blobId)
    {
        var reply = await _client.DeleteAsync(BlobPath(Notes, recordId, blobId));

        reply.Status.Should().Be(HttpStatusCode.BadRequest);
        reply.Code.Should().Be("invalid_request");
        _store.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Every_blob_route_needs_a_signed_in_user()
    {
        var reply = await _host.Client(userId: null).GetAsync(BlobPath(Notes, Items.NewId(), Items.NewId()));

        reply.Status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_tombstone_deletes_the_record_s_blobs_and_a_write_doesnt()
    {
        var id = Items.NewId();
        var created = await _client.PostAsync($"/sync/{Notes}", Items.Push(Items.Write(id, null)));
        var version = created.Body!["results"]![0]!["version"]!.GetValue<string>();

        await _client.PostAsync($"/sync/{Notes}", Items.Push(Items.Delete(id, version)));

        _store.Calls.Should().Equal($"delete record user-1/{Notes}/{id}");
    }

    [Fact]
    public async Task A_tombstone_in_a_collection_without_blobs_touches_no_storage()
    {
        var id = Items.NewId();
        var created = await _client.PostAsync($"/sync/{Lists}", Items.Push(Items.Write(id, null)));

        await _client.PostAsync($"/sync/{Lists}", Items.Push(Items.Delete(id, created.Body!["results"]![0]!["version"]!.GetValue<string>())));

        _store.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_tombstone_stands_when_its_blobs_can_t_be_deleted()
    {
        _store.DeleteRecordBlobsFails = new IOException("The storage is down.");
        var id = Items.NewId();
        var created = await _client.PostAsync($"/sync/{Notes}", Items.Push(Items.Write(id, null)));

        var deleted = await _client.PostAsync($"/sync/{Notes}", Items.Push(Items.Delete(id, created.Body!["results"]![0]!["version"]!.GetValue<string>())));

        deleted.Body!["results"]![0]!["status"]!.GetValue<int>().Should().Be(200);
        var pulled = await _client.GetAsync($"/sync/{Notes}");
        pulled.Body!["items"]![0]!["deletedAt"].Should().NotBeNull();
    }

    /// <summary>In-memory stores, and blobs for notes kept by <paramref name="store"/>, which signs nothing.</summary>
    internal static void WithRecordingBlobs(ConsealSyncBuilder sync, IConsealSyncBlobStore store)
    {
        sync.AddInMemoryStores().WithBlobs(options =>
        {
            options.BucketName = "test-blobs";
            options.Collections.Add(Notes);
        });
        sync.Services.Replace(ServiceDescriptor.Singleton(store));
    }

    private static string BlobPath(string collectionId, string recordId, string blobId) => $"/sync/{collectionId}/{recordId}/blobs/{blobId}";
}
