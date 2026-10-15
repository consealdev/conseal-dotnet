using System.Net;
using System.Text.Json.Nodes;
using ConsealDev.Conseal.Storage;
using ConsealDev.Conseal.Storage.Dynamo;
using ConsealDev.Conseal.Tests.TestSupport;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ConsealDev.Conseal.Tests.Endpoints;

public sealed class SyncRoutesTests : IAsyncLifetime
{
    private const string Notes = TestHost.Notes;

    private TestHost _host = null!;
    private ApiClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await TestHost.StartAsync();
        _client = _host.Client();
    }

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task Config_lists_the_allowlisted_collections_with_the_storage_limit()
    {
        var reply = await _client.GetAsync("/sync");

        reply.Status.Should().Be(HttpStatusCode.OK);
        var collections = reply.Body!["collections"]!.AsArray();
        collections.Select(c => c!["id"]!.GetValue<string>()).Should().Equal("keyring", TestHost.Notes, TestHost.Lists);
        collections[0]!["maxRecordBytes"]!.GetValue<int>().Should().Be(DynamoRecordStore.RecordLimitBytes);
        collections[0]!.AsObject().ContainsKey("maxBlobBytes").Should().BeTrue();
        collections[0]!["maxBlobBytes"].Should().BeNull();
        reply.Body["maxBatchItems"]!.GetValue<int>().Should().Be(100);
        reply.Body["maxPageItems"]!.GetValue<int>().Should().Be(1000);
    }

    [Theory]
    [InlineData("GET", "/sync")]
    [InlineData("GET", "/sync/7f3a9c21")]
    [InlineData("POST", "/sync/7f3a9c21")]
    public async Task Every_route_needs_a_signed_in_user(string method, string path)
    {
        var anonymous = _host.Client(userId: null);

        var reply = method == "GET" ? await anonymous.GetAsync(path) : await anonymous.PostAsync(path, Items.Push(Items.Write(Items.NewId(), null)));

        reply.Status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_collection_that_isnt_allowlisted_answers_404()
    {
        var pull = await _client.GetAsync("/sync/notes");
        var push = await _client.PostAsync("/sync/notes", Items.Push(Items.Write(Items.NewId(), null)));

        pull.Status.Should().Be(HttpStatusCode.NotFound);
        pull.Code.Should().Be("unknown_collection");
        push.Status.Should().Be(HttpStatusCode.NotFound);
        push.Code.Should().Be("unknown_collection");
    }

    [Fact]
    public async Task The_keyring_is_accepted_without_being_configured()
    {
        const string keyringRecord = "00000000-0000-4000-8000-000000000000";

        var push = await _client.PostAsync("/sync/keyring", Items.Push(Items.Write(keyringRecord, null)));
        var again = await _client.PostAsync("/sync/keyring", Items.Push(Items.Write(keyringRecord, null)));
        var pull = await _client.GetAsync("/sync/keyring");

        push.Body!["results"]![0]!["status"]!.GetValue<int>().Should().Be(200);
        again.Body!["results"]![0]!["status"]!.GetValue<int>().Should().Be(409);
        IdsOf(pull.Body!).Should().Equal(keyringRecord);
    }

    [Fact]
    public async Task A_write_with_a_null_base_version_creates_the_record_as_sent()
    {
        var id = Items.NewId();
        var item = Items.Write(id, null);

        var result = (await PushAsync(item))[0]!;
        var page = await PullAsync();

        result["status"]!.GetValue<int>().Should().Be(200);
        result["version"]!.GetValue<string>().Should().MatchRegex("^[0-9a-f]{32}$");
        var record = page["items"]!.AsArray().Single()!;
        record["id"]!.GetValue<string>().Should().Be(id);
        record["ciphertext"]!.GetValue<string>().Should().Be(Field(item, "ciphertext"));
        record["iv"]!.GetValue<string>().Should().Be(Field(item, "iv"));
        record["wrappedDek"]!.GetValue<string>().Should().Be(Field(item, "wrappedDek"));
        record["version"]!.GetValue<string>().Should().Be(result["version"]!.GetValue<string>());
        record["seq"]!.GetValue<long>().Should().Be(result["seq"]!.GetValue<long>());
        record["deletedAt"].Should().BeNull();
    }

    [Fact]
    public async Task A_write_naming_the_current_version_replaces_the_record_with_a_new_version_and_seq()
    {
        var id = Items.NewId();
        var created = (await PushAsync(Items.Write(id, null)))[0]!;

        var replaced = (await PushAsync(Items.Write(id, Version(created))))[0]!;

        replaced["status"]!.GetValue<int>().Should().Be(200);
        Version(replaced).Should().NotBe(Version(created));
        replaced["seq"]!.GetValue<long>().Should().BeGreaterThan(created["seq"]!.GetValue<long>());
        (await PullAsync())["items"]!.AsArray().Should().ContainSingle();
    }

    [Fact]
    public async Task A_write_naming_a_stale_version_is_a_conflict_carrying_the_current_record()
    {
        var id = Items.NewId();
        var created = (await PushAsync(Items.Write(id, null)))[0]!;
        var current = (await PushAsync(Items.Write(id, Version(created))))[0]!;

        var stale = (await PushAsync(Items.Write(id, Version(created))))[0]!;

        stale["status"]!.GetValue<int>().Should().Be(409);
        stale["current"]!["id"]!.GetValue<string>().Should().Be(id);
        stale["current"]!["version"]!.GetValue<string>().Should().Be(Version(current));
        stale["current"]!["ciphertext"]!.GetValue<string>().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Creating_a_record_that_exists_is_a_conflict()
    {
        var id = Items.NewId();
        await PushAsync(Items.Write(id, null));

        var again = (await PushAsync(Items.Write(id, null)))[0]!;

        again["status"]!.GetValue<int>().Should().Be(409);
        again["current"]!["id"]!.GetValue<string>().Should().Be(id);
    }

    [Fact]
    public async Task Naming_a_version_of_a_record_the_server_doesnt_have_is_a_conflict_with_a_null_current()
    {
        var result = (await PushAsync(Items.Write(Items.NewId(), "9f1c2b7d4e6a48a1b0c3d5e7f9a1b2c3")))[0]!;

        result["status"]!.GetValue<int>().Should().Be(409);
        result.AsObject().ContainsKey("current").Should().BeTrue();
        result["current"].Should().BeNull();
    }

    [Fact]
    public async Task A_delete_leaves_a_tombstone_that_pulls_carry()
    {
        var id = Items.NewId();
        var created = (await PushAsync(Items.Write(id, null)))[0]!;

        var deleted = (await PushAsync(Items.Delete(id, Version(created))))[0]!;
        var tombstone = (await PullAsync())["items"]!.AsArray().Single()!;

        deleted["status"]!.GetValue<int>().Should().Be(200);
        tombstone["version"]!.GetValue<string>().Should().Be(Version(deleted));
        tombstone["ciphertext"].Should().BeNull();
        tombstone["iv"].Should().BeNull();
        tombstone["wrappedDek"].Should().BeNull();
        tombstone["deletedAt"]!.GetValue<DateTimeOffset>().Should().Be(_host.Time.Now);
    }

    [Fact]
    public async Task Writing_over_a_tombstone_by_its_version_brings_the_record_back()
    {
        var id = Items.NewId();
        var created = (await PushAsync(Items.Write(id, null)))[0]!;
        var deleted = (await PushAsync(Items.Delete(id, Version(created))))[0]!;

        var restored = (await PushAsync(Items.Write(id, Version(deleted))))[0]!;
        var record = (await PullAsync())["items"]!.AsArray().Single()!;

        restored["status"]!.GetValue<int>().Should().Be(200);
        record["deletedAt"].Should().BeNull();
        record["ciphertext"]!.GetValue<string>().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Items_are_answered_on_their_own_and_in_order()
    {
        var created = Items.NewId();
        var conflicted = Items.NewId();
        await PushAsync(Items.Write(conflicted, null));

        var results = await PushAsync(
            Items.Write(created, null),
            Items.Write("NOT-A-UUID", null),
            Items.Write(conflicted, null));

        results.Select(r => r!["status"]!.GetValue<int>()).Should().Equal(200, 400, 409);
        results.Select(r => r!["id"]!.GetValue<string>()).Should().Equal(created, "NOT-A-UUID", conflicted);
        results[1]!["error"]!["code"]!.GetValue<string>().Should().Be("invalid_record");
    }

    public static TheoryData<string, string> InvalidItems => new()
    {
        { "an uppercase id", """{"id":"3B2F6C1E-8D4A-4F0B-9C7E-5A1D2E3F4B6C","baseVersion":null,"deleted":false,"ciphertext":"AAAAAAAAAAAAAAAAAAAAAA==","iv":"AAAAAAAAAAAAAAAA","wrappedDek":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""" },
        { "a version 7 id", """{"id":"0192f0c4-3c2a-7b1e-8f00-5a1d2e3f4b6c","baseVersion":null,"deleted":false,"ciphertext":"AAAAAAAAAAAAAAAAAAAAAA==","iv":"AAAAAAAAAAAAAAAA","wrappedDek":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""" },
        { "no deleted flag", """{"id":"3b2f6c1e-8d4a-4f0b-9c7e-5a1d2e3f4b6c","baseVersion":null,"ciphertext":"AAAAAAAAAAAAAAAAAAAAAA==","iv":"AAAAAAAAAAAAAAAA","wrappedDek":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""" },
        { "no baseVersion", """{"id":"3b2f6c1e-8d4a-4f0b-9c7e-5a1d2e3f4b6c","deleted":false,"ciphertext":"AAAAAAAAAAAAAAAAAAAAAA==","iv":"AAAAAAAAAAAAAAAA","wrappedDek":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""" },
        { "a delete with a null baseVersion", """{"id":"3b2f6c1e-8d4a-4f0b-9c7e-5a1d2e3f4b6c","baseVersion":null,"deleted":true}""" },
        { "an 11-byte iv", """{"id":"3b2f6c1e-8d4a-4f0b-9c7e-5a1d2e3f4b6c","baseVersion":null,"deleted":false,"ciphertext":"AAAAAAAAAAAAAAAAAAAAAA==","iv":"AAAAAAAAAAAAAAA=","wrappedDek":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""" },
        { "a 59-byte wrapped DEK", """{"id":"3b2f6c1e-8d4a-4f0b-9c7e-5a1d2e3f4b6c","baseVersion":null,"deleted":false,"ciphertext":"AAAAAAAAAAAAAAAAAAAAAA==","iv":"AAAAAAAAAAAAAAAA","wrappedDek":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="}""" },
        { "base64url ciphertext", """{"id":"3b2f6c1e-8d4a-4f0b-9c7e-5a1d2e3f4b6c","baseVersion":null,"deleted":false,"ciphertext":"_-_-_-_-_-_-_-_-_-_-_-_-","iv":"AAAAAAAAAAAAAAAA","wrappedDek":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""" },
        { "a ciphertext shorter than its tag", """{"id":"3b2f6c1e-8d4a-4f0b-9c7e-5a1d2e3f4b6c","baseVersion":null,"deleted":false,"ciphertext":"AAAAAAAAAAAAAAAAAAAA","iv":"AAAAAAAAAAAAAAAA","wrappedDek":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""" },
    };

    [Theory]
    [MemberData(nameof(InvalidItems))]
    public async Task An_invalid_item_gets_400_invalid_record(string _, string item)
    {
        var reply = await _client.PostRawAsync($"/sync/{Notes}", $$"""{"items":[{{item}}]}""");

        reply.Status.Should().Be(HttpStatusCode.OK);
        var result = reply.Body!["results"]![0]!;
        result["status"]!.GetValue<int>().Should().Be(400);
        result["error"]!["code"]!.GetValue<string>().Should().Be("invalid_record");
    }

    [Fact]
    public async Task A_ciphertext_over_the_storage_limit_gets_413_record_too_large()
    {
        var result = (await PushAsync(Items.Write(Items.NewId(), null, DynamoRecordStore.RecordLimitBytes + 1)))[0]!;

        result["status"]!.GetValue<int>().Should().Be(413);
        result["error"]!["code"]!.GetValue<string>().Should().Be("record_too_large");
    }

    [Fact]
    public async Task A_ciphertext_at_the_storage_limit_is_written()
    {
        var result = (await PushAsync(Items.Write(Items.NewId(), null, DynamoRecordStore.RecordLimitBytes)))[0]!;

        result["status"]!.GetValue<int>().Should().Be(200);
    }

    [Fact]
    public async Task A_record_the_storage_refuses_as_too_large_gets_413_rather_than_an_error()
    {
        await using var host = await TestHost.StartAsync(stores: sync =>
        {
            sync.AddInMemoryStores();
            sync.Services.Replace(ServiceDescriptor.Singleton<IConsealSyncRecordStore>(new TooLargeRecordStore()));
        });

        var reply = await host.Client().PostAsync($"/sync/{Notes}", Items.Push(Items.Write(Items.NewId(), null)));

        var result = reply.Body!["results"]![0]!;
        result["status"]!.GetValue<int>().Should().Be(413);
        result["error"]!["code"]!.GetValue<string>().Should().Be("record_too_large");
    }

    [Theory]
    [InlineData("not json", 400, "invalid_request")]
    [InlineData("""{"items":[]}""", 400, "invalid_request")]
    [InlineData("""{"items":{}}""", 400, "invalid_request")]
    [InlineData("""{"items":[{"baseVersion":null}]}""", 400, "invalid_request")]
    [InlineData("""{"items":[{"id":"3b2f6c1e-8d4a-4f0b-9c7e-5a1d2e3f4b6c"},{"id":"3b2f6c1e-8d4a-4f0b-9c7e-5a1d2e3f4b6c"}]}""", 400, "duplicate_id")]
    public async Task A_body_that_isnt_a_push_fails_as_a_whole(string body, int status, string code)
    {
        var reply = await _client.PostRawAsync($"/sync/{Notes}", body);

        ((int)reply.Status).Should().Be(status);
        reply.Code.Should().Be(code);
    }

    [Fact]
    public async Task More_items_than_maxBatchItems_answers_413_too_many_items()
    {
        var items = Enumerable.Range(0, 101).Select(_ => Items.Write(Items.NewId(), null, 16)).ToArray();

        var reply = await _client.PostAsync($"/sync/{Notes}", Items.Push(items));

        reply.Status.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        reply.Code.Should().Be("too_many_items");
    }

    [Fact]
    public async Task A_body_over_maxRequestBytes_answers_413_request_too_large()
    {
        await using var host = await TestHost.StartAsync(config: new Dictionary<string, string?> { ["ConsealSync:MaxRequestBytes"] = "1048576" });
        var items = Enumerable.Range(0, 4).Select(_ => Items.Write(Items.NewId(), null, 300_000)).ToArray();

        var reply = await host.Client().PostAsync($"/sync/{Notes}", Items.Push(items));

        reply.Status.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        reply.Code.Should().Be("request_too_large");
    }

    [Fact]
    public async Task A_pull_pages_through_the_records_in_seq_order()
    {
        var ids = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            ids.Add(Items.NewId());
            await PushAsync(Items.Write(ids[^1], null));
        }

        var first = await PullAsync(limit: 2);
        var second = await PullAsync(Cursor(first), limit: 2);
        var third = await PullAsync(Cursor(second), limit: 2);
        var after = await PullAsync(Cursor(third));

        IdsOf(first).Concat(IdsOf(second)).Concat(IdsOf(third)).Should().Equal(ids);
        first["hasMore"]!.GetValue<bool>().Should().BeTrue();
        second["hasMore"]!.GetValue<bool>().Should().BeTrue();
        third["hasMore"]!.GetValue<bool>().Should().BeFalse();
        IdsOf(after).Should().BeEmpty();
        after["hasMore"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task A_record_written_again_comes_once_in_its_latest_state_after_the_cursor()
    {
        var id = Items.NewId();
        var created = (await PushAsync(Items.Write(id, null)))[0]!;
        var cursor = Cursor(await PullAsync());
        await PushAsync(Items.Write(Items.NewId(), null));
        var replaced = (await PushAsync(Items.Write(id, Version(created))))[0]!;

        var page = await PullAsync(cursor);

        var items = page["items"]!.AsArray();
        items.Should().HaveCount(2);
        items[1]!["id"]!.GetValue<string>().Should().Be(id);
        items[1]!["version"]!.GetValue<string>().Should().Be(Version(replaced));
    }

    [Fact]
    public async Task A_pull_never_shows_another_users_records()
    {
        await PushAsync(Items.Write(Items.NewId(), null));

        var page = await _host.Client("user-2").GetAsync($"/sync/{Notes}");

        page.Body!["items"]!.AsArray().Should().BeEmpty();
    }

    [Fact]
    public async Task A_cursor_works_only_for_the_user_and_collection_it_was_issued_for()
    {
        await PushAsync(Items.Write(Items.NewId(), null));
        var cursor = Cursor(await PullAsync());

        var otherUser = await _host.Client("user-2").GetAsync($"/sync/{Notes}?cursor={cursor}");
        var otherCollection = await _client.GetAsync($"/sync/{TestHost.Lists}?cursor={cursor}");

        otherUser.Status.Should().Be(HttpStatusCode.BadRequest);
        otherUser.Code.Should().Be("invalid_cursor");
        otherCollection.Code.Should().Be("invalid_cursor");
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("eyJ1c2VySWQiOiJ1c2VyLTEiLCJjb2xsZWN0aW9uSWQiOiI3ZjNhOWMyMSIsInNlcSI6MH0")]
    public async Task A_cursor_the_server_didnt_issue_answers_invalid_cursor(string cursor)
    {
        var reply = await _client.GetAsync($"/sync/{Notes}?cursor={cursor}");

        reply.Status.Should().Be(HttpStatusCode.BadRequest);
        reply.Code.Should().Be("invalid_cursor");
    }

    [Fact]
    public async Task A_cursor_with_one_byte_changed_answers_invalid_cursor()
    {
        await PushAsync(Items.Write(Items.NewId(), null));
        var cursor = Cursor(await PullAsync());
        var tampered = cursor[..^1] + (cursor[^1] == 'A' ? 'B' : 'A');

        var reply = await _client.GetAsync($"/sync/{Notes}?cursor={tampered}");

        reply.Code.Should().Be("invalid_cursor");
    }

    [Fact]
    public async Task A_cursor_past_every_write_answers_invalid_cursor()
    {
        await PushAsync(Items.Write(Items.NewId(), null));
        var cursor = Cursor(await PullAsync());
        // A fresh store with the same signing key: the data the cursor was issued for is gone.
        await using var wiped = await TestHost.StartAsync();

        var reply = await wiped.Client().GetAsync($"/sync/{Notes}?cursor={cursor}");

        reply.Status.Should().Be(HttpStatusCode.BadRequest);
        reply.Code.Should().Be("invalid_cursor");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("ten")]
    [InlineData("1.5")]
    public async Task A_limit_that_isnt_a_whole_number_of_at_least_1_answers_invalid_request(string limit)
    {
        var reply = await _client.GetAsync($"/sync/{Notes}?limit={limit}");

        reply.Status.Should().Be(HttpStatusCode.BadRequest);
        reply.Code.Should().Be("invalid_request");
    }

    [Fact]
    public async Task A_limit_over_maxPageItems_is_capped()
    {
        await using var host = await TestHost.StartAsync(config: new Dictionary<string, string?> { ["ConsealSync:MaxPageItems"] = "2" });
        var client = host.Client();
        for (var i = 0; i < 3; i++) await client.PostAsync($"/sync/{Notes}", Items.Push(Items.Write(Items.NewId(), null)));

        var page = await client.GetAsync($"/sync/{Notes}?limit=50");

        page.Body!["items"]!.AsArray().Should().HaveCount(2);
        page.Body["hasMore"]!.GetValue<bool>().Should().BeTrue();
    }

    private async Task<JsonArray> PushAsync(params object[] items)
    {
        var reply = await _client.PostAsync($"/sync/{Notes}", Items.Push(items));
        reply.Status.Should().Be(HttpStatusCode.OK);
        return reply.Body!["results"]!.AsArray();
    }

    private async Task<JsonNode> PullAsync(string? cursor = null, int? limit = null)
    {
        var query = new List<string>();
        if (cursor is not null) query.Add($"cursor={cursor}");
        if (limit is not null) query.Add($"limit={limit}");
        var reply = await _client.GetAsync($"/sync/{Notes}" + (query.Count > 0 ? "?" + string.Join("&", query) : ""));
        reply.Status.Should().Be(HttpStatusCode.OK);
        return reply.Body!;
    }

    private static string Version(JsonNode result) => result["version"]!.GetValue<string>();

    private static string Cursor(JsonNode page) => page["cursor"]!.GetValue<string>();

    private static IEnumerable<string> IdsOf(JsonNode page) => page["items"]!.AsArray().Select(item => item!["id"]!.GetValue<string>());

    private static string Field(object item, string name) => (string)item.GetType().GetProperty(name)!.GetValue(item)!;
}
