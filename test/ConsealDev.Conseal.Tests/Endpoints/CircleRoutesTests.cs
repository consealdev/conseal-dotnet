using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ConsealDev.Conseal.Tests.TestSupport;
using FluentAssertions;

namespace ConsealDev.Conseal.Tests.Endpoints;

public sealed class CircleRoutesTests : IAsyncLifetime
{
    private const string User = "user-1";

    private TestHost _host = null!;
    private ApiClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await TestHost.StartAsync();
        _client = _host.Client(User);
    }

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task An_account_without_a_circle_answers_404_no_circle()
    {
        var reply = await _client.GetAsync("/circle");

        reply.Status.Should().Be(HttpStatusCode.NotFound);
        reply.Code.Should().Be("no_circle");
    }

    [Fact]
    public async Task Every_route_needs_a_signed_in_user()
    {
        var reply = await _host.Client(userId: null).GetAsync("/circle/devices");

        reply.Status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Creating_the_circle_stores_the_commitment_once_and_registers_the_first_device()
    {
        var founder = Items.NewId();
        var body = NewCircle(founder);

        var created = await _client.PostAsync("/circle", body);
        var again = await _client.PostAsync("/circle", NewCircle(Items.NewId()));
        var circle = await _client.GetAsync("/circle");
        var devices = await _client.GetAsync("/circle/devices");

        created.Status.Should().Be(HttpStatusCode.NoContent);
        again.Status.Should().Be(HttpStatusCode.Conflict);
        again.Code.Should().Be("circle_exists");
        circle.Body!["aekCommitment"]!.GetValue<string>().Should().Be(Field(body, "aekCommitment"));
        var device = devices.Body!["devices"]!.AsArray().Single()!;
        device["deviceId"]!.GetValue<string>().Should().Be(founder);
        device["name"]!.GetValue<string>().Should().Be("Laptop");
        device["registeredAt"]!.GetValue<DateTimeOffset>().Should().Be(_host.Time.Now);
        _host.Hooks.Calls.Should().Equal($"joined {User} {founder} Founding");
    }

    public static TheoryData<string, object> InvalidCircles => new()
    {
        { "an uppercase device id", new { deviceId = Items.NewId().ToUpperInvariant(), aekCommitment = Base64(32), wrappedAek = WrappedAek() } },
        { "a 31-byte commitment", new { deviceId = Items.NewId(), aekCommitment = Base64(31), wrappedAek = WrappedAek() } },
        { "a wrapped AEK without salt", new { deviceId = Items.NewId(), aekCommitment = Base64(32), wrappedAek = new { wrappedKey = Base64(40) } } },
        { "a wrapped AEK over 8 KB", new { deviceId = Items.NewId(), aekCommitment = Base64(32), wrappedAek = new { wrappedKey = Base64(7000), salt = Base64(16) } } },
        { "a device name over 256 characters", new { deviceId = Items.NewId(), aekCommitment = Base64(32), wrappedAek = WrappedAek(), deviceMeta = new { name = new string('a', 257) } } },
    };

    [Theory]
    [MemberData(nameof(InvalidCircles))]
    public async Task A_circle_with_a_malformed_field_answers_invalid_request(string _, object body)
    {
        var reply = await _client.PostAsync("/circle", body);

        reply.Status.Should().Be(HttpStatusCode.BadRequest);
        reply.Code.Should().Be("invalid_request");
    }

    [Fact]
    public async Task The_join_ceremony_brings_a_new_device_in()
    {
        var commitment = await CreateCircleAsync();
        var joiner = Items.NewId();
        var request = JoinRequest(joiner);
        var sealedAek = Sealed();
        var passphraseCheck = Sealed();

        var created = await _client.PostAsync("/circle/joins", request);
        var challengeId = created.Body!["challengeId"]!.GetValue<string>();
        var waiting = await _client.GetAsync("/circle/joins");
        var pending = await _client.GetAsync($"/circle/joins/{challengeId}");
        var authorized = await _client.PostAsync($"/circle/joins/{challengeId}/authorize", new { sealedAek, passphraseCheck });
        var status = await _client.GetAsync($"/circle/joins/{challengeId}");
        var saved = await _client.PutAsync($"/circle/devices/{joiner}/wrapped-aek", new { wrappedAek = WrappedAek(), deviceMeta = new { name = "Phone" }, challengeId });

        created.Status.Should().Be(HttpStatusCode.Created);
        challengeId.Should().MatchRegex("^[0-9a-f]{32}$");
        created.Body["aekCommitment"]!.GetValue<string>().Should().Be(commitment);
        created.Body["expiresAt"]!.GetValue<DateTimeOffset>().Should().Be(_host.Time.Now.AddMinutes(5));

        var listed = waiting.Body!["joins"]!.AsArray().Single()!;
        listed["challengeId"]!.GetValue<string>().Should().Be(challengeId);
        listed["receivedAt"]!.GetValue<DateTimeOffset>().Should().Be(_host.Time.Now);
        JsonNode.DeepEquals(listed["request"], ToNode(request)).Should().BeTrue("the request comes back as it was sent, unknown fields included");

        pending.Body!["status"]!.GetValue<string>().Should().Be("pending");
        pending.Body.AsObject().ContainsKey("sealedAek").Should().BeFalse();
        authorized.Status.Should().Be(HttpStatusCode.NoContent);
        status.Body!["status"]!.GetValue<string>().Should().Be("authorized");
        JsonNode.DeepEquals(status.Body["sealedAek"], ToNode(sealedAek)).Should().BeTrue();
        JsonNode.DeepEquals(status.Body["passphraseCheck"], ToNode(passphraseCheck)).Should().BeTrue();
        saved.Status.Should().Be(HttpStatusCode.NoContent);

        (await _client.GetAsync($"/circle/joins/{challengeId}")).Code.Should().Be("unknown_join");
        (await _client.GetAsync("/circle/devices")).Body!["devices"]!.AsArray().Should().HaveCount(2);
        _host.Hooks.Calls.Should().EndWith($"joined {User} {joiner} Ceremony");
    }

    [Fact]
    public async Task A_join_is_used_once()
    {
        await CreateCircleAsync();
        var joiner = Items.NewId();
        var challengeId = await AuthorizedJoinAsync(joiner);
        await _client.PutAsync($"/circle/devices/{joiner}/wrapped-aek", new { wrappedAek = WrappedAek(), challengeId });

        var again = await _client.PutAsync($"/circle/devices/{joiner}/wrapped-aek", new { wrappedAek = WrappedAek(), challengeId });

        again.Status.Should().Be(HttpStatusCode.NotFound);
        again.Code.Should().Be("unknown_join");
    }

    [Fact]
    public async Task A_join_completes_only_for_the_device_that_asked()
    {
        await CreateCircleAsync();
        var challengeId = await AuthorizedJoinAsync(Items.NewId());

        var reply = await _client.PutAsync($"/circle/devices/{Items.NewId()}/wrapped-aek", new { wrappedAek = WrappedAek(), challengeId });

        reply.Status.Should().Be(HttpStatusCode.Forbidden);
        reply.Code.Should().Be("join_mismatch");
    }

    [Fact]
    public async Task A_join_completes_only_once_authorized()
    {
        await CreateCircleAsync();
        var joiner = Items.NewId();
        var challengeId = (await _client.PostAsync("/circle/joins", JoinRequest(joiner))).Body!["challengeId"]!.GetValue<string>();

        var reply = await _client.PutAsync($"/circle/devices/{joiner}/wrapped-aek", new { wrappedAek = WrappedAek(), challengeId });

        reply.Status.Should().Be(HttpStatusCode.Conflict);
        reply.Code.Should().Be("join_not_authorized");
    }

    [Fact]
    public async Task A_join_is_decided_once()
    {
        await CreateCircleAsync();
        var challengeId = (await _client.PostAsync("/circle/joins", JoinRequest(Items.NewId()))).Body!["challengeId"]!.GetValue<string>();

        var denied = await _client.PostAsync($"/circle/joins/{challengeId}/deny");
        var authorized = await _client.PostAsync($"/circle/joins/{challengeId}/authorize", new { sealedAek = Sealed(), passphraseCheck = Sealed() });
        var status = await _client.GetAsync($"/circle/joins/{challengeId}");

        denied.Status.Should().Be(HttpStatusCode.NoContent);
        authorized.Status.Should().Be(HttpStatusCode.Conflict);
        authorized.Code.Should().Be("join_not_pending");
        status.Body!["status"]!.GetValue<string>().Should().Be("denied");
    }

    [Fact]
    public async Task A_join_nobody_decides_within_five_minutes_expires()
    {
        await CreateCircleAsync();
        var challengeId = (await _client.PostAsync("/circle/joins", JoinRequest(Items.NewId()))).Body!["challengeId"]!.GetValue<string>();
        _host.Time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

        var listed = await _client.GetAsync("/circle/joins");
        var authorized = await _client.PostAsync($"/circle/joins/{challengeId}/authorize", new { sealedAek = Sealed(), passphraseCheck = Sealed() });
        var status = await _client.GetAsync($"/circle/joins/{challengeId}");

        listed.Body!["joins"]!.AsArray().Should().BeEmpty();
        authorized.Status.Should().Be(HttpStatusCode.Gone);
        authorized.Code.Should().Be("join_expired");
        status.Body!["status"]!.GetValue<string>().Should().Be("expired");
    }

    [Fact]
    public async Task An_authorized_join_gives_the_new_device_five_more_minutes()
    {
        await CreateCircleAsync();
        var joiner = Items.NewId();
        var challengeId = (await _client.PostAsync("/circle/joins", JoinRequest(joiner))).Body!["challengeId"]!.GetValue<string>();
        _host.Time.Advance(TimeSpan.FromMinutes(4));
        await _client.PostAsync($"/circle/joins/{challengeId}/authorize", new { sealedAek = Sealed(), passphraseCheck = Sealed() });
        _host.Time.Advance(TimeSpan.FromMinutes(4));

        var inTime = await _client.GetAsync($"/circle/joins/{challengeId}");
        _host.Time.Advance(TimeSpan.FromMinutes(2));
        var late = await _client.PutAsync($"/circle/devices/{joiner}/wrapped-aek", new { wrappedAek = WrappedAek(), challengeId });

        inTime.Body!["status"]!.GetValue<string>().Should().Be("authorized");
        late.Status.Should().Be(HttpStatusCode.Gone);
        late.Code.Should().Be("join_expired");
    }

    [Fact]
    public async Task Authorizing_a_join_a_poll_found_expired_answers_410()
    {
        await CreateCircleAsync();
        var challengeId = (await _client.PostAsync("/circle/joins", JoinRequest(Items.NewId()))).Body!["challengeId"]!.GetValue<string>();
        _host.Time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        var status = await _client.GetAsync($"/circle/joins/{challengeId}");

        var authorized = await _client.PostAsync($"/circle/joins/{challengeId}/authorize", new { sealedAek = Sealed(), passphraseCheck = Sealed() });

        status.Body!["status"]!.GetValue<string>().Should().Be("expired");
        authorized.Status.Should().Be(HttpStatusCode.Gone);
        authorized.Code.Should().Be("join_expired");
    }

    [Fact]
    public async Task Saving_with_a_join_a_poll_found_expired_answers_410()
    {
        await CreateCircleAsync();
        var joiner = Items.NewId();
        var challengeId = await AuthorizedJoinAsync(joiner);
        _host.Time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        var status = await _client.GetAsync($"/circle/joins/{challengeId}");

        var saved = await _client.PutAsync($"/circle/devices/{joiner}/wrapped-aek", new { wrappedAek = WrappedAek(), challengeId });

        status.Body!["status"]!.GetValue<string>().Should().Be("expired");
        saved.Status.Should().Be(HttpStatusCode.Gone);
        saved.Code.Should().Be("join_expired");
    }

    [Fact]
    public async Task Saving_with_a_join_nobody_decided_in_time_answers_410()
    {
        await CreateCircleAsync();
        var joiner = Items.NewId();
        var challengeId = (await _client.PostAsync("/circle/joins", JoinRequest(joiner))).Body!["challengeId"]!.GetValue<string>();
        _host.Time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

        var saved = await _client.PutAsync($"/circle/devices/{joiner}/wrapped-aek", new { wrappedAek = WrappedAek(), challengeId });

        saved.Status.Should().Be(HttpStatusCode.Gone);
        saved.Code.Should().Be("join_expired");
    }

    [Fact]
    public async Task A_new_join_from_the_same_device_replaces_the_waiting_one()
    {
        await CreateCircleAsync();
        var joiner = Items.NewId();
        var first = (await _client.PostAsync("/circle/joins", JoinRequest(joiner))).Body!["challengeId"]!.GetValue<string>();

        var second = (await _client.PostAsync("/circle/joins", JoinRequest(joiner))).Body!["challengeId"]!.GetValue<string>();

        (await _client.GetAsync($"/circle/joins/{first}")).Code.Should().Be("unknown_join");
        (await _client.GetAsync("/circle/joins")).Body!["joins"]!.AsArray().Single()!["challengeId"]!.GetValue<string>().Should().Be(second);
    }

    [Fact]
    public async Task A_join_needs_a_circle_and_a_device_that_isnt_in_it()
    {
        var withoutCircle = await _client.PostAsync("/circle/joins", JoinRequest(Items.NewId()));
        var founder = Items.NewId();
        await _client.PostAsync("/circle", NewCircle(founder));
        var member = await _client.PostAsync("/circle/joins", JoinRequest(founder));

        withoutCircle.Status.Should().Be(HttpStatusCode.NotFound);
        withoutCircle.Code.Should().Be("no_circle");
        member.Status.Should().Be(HttpStatusCode.Conflict);
        member.Code.Should().Be("device_registered");
    }

    [Fact]
    public async Task A_join_request_carrying_a_private_key_is_refused()
    {
        await CreateCircleAsync();
        var request = new { deviceId = Items.NewId(), ephemeralPublicKey = new { kty = "EC", crv = "P-256", x = "AAAA", y = "AAAA", d = "AAAA" }, createdAt = "2026-10-14T12:00:00.000Z" };

        var reply = await _client.PostAsync("/circle/joins", request);

        reply.Status.Should().Be(HttpStatusCode.BadRequest);
        reply.Code.Should().Be("invalid_request");
    }

    [Fact]
    public async Task A_device_that_saves_without_a_join_is_reported_as_a_recovery()
    {
        await CreateCircleAsync();
        var recovered = Items.NewId();

        var saved = await _client.PutAsync($"/circle/devices/{recovered}/wrapped-aek", new { wrappedAek = WrappedAek() });
        var fetched = await _client.GetAsync($"/circle/devices/{recovered}/wrapped-aek");

        saved.Status.Should().Be(HttpStatusCode.NoContent);
        fetched.Body!["wrappedAek"]!["wrappedKey"].Should().NotBeNull();
        _host.Hooks.Calls.Should().EndWith($"joined {User} {recovered} Recovery");
    }

    [Fact]
    public async Task Saving_a_wrapped_aek_needs_a_circle()
    {
        var reply = await _client.PutAsync($"/circle/devices/{Items.NewId()}/wrapped-aek", new { wrappedAek = WrappedAek() });

        reply.Status.Should().Be(HttpStatusCode.NotFound);
        reply.Code.Should().Be("no_circle");
    }

    [Fact]
    public async Task A_revoked_device_is_kept_out_and_its_attempts_are_reported()
    {
        await CreateCircleAsync();
        var device = Items.NewId();
        await _client.PutAsync($"/circle/devices/{device}/wrapped-aek", new { wrappedAek = WrappedAek() });

        var revoked = await _client.DeleteAsync($"/circle/devices/{device}");
        var fetched = await _client.GetAsync($"/circle/devices/{device}/wrapped-aek");
        var saved = await _client.PutAsync($"/circle/devices/{device}/wrapped-aek", new { wrappedAek = WrappedAek() });
        var joined = await _client.PostAsync("/circle/joins", JoinRequest(device));
        var again = await _client.DeleteAsync($"/circle/devices/{device}");

        revoked.Status.Should().Be(HttpStatusCode.NoContent);
        foreach (var reply in new[] { fetched, saved, joined })
        {
            reply.Status.Should().Be(HttpStatusCode.Forbidden);
            reply.Code.Should().Be("device_revoked");
        }
        again.Status.Should().Be(HttpStatusCode.NoContent);
        (await _client.GetAsync("/circle/devices")).Body!["devices"]!.AsArray().Should().ContainSingle();
        _host.Hooks.Calls.Where(call => call.StartsWith("revoked", StringComparison.Ordinal)).Should().Equal($"revoked {User} {device}");
        _host.Hooks.Calls.Count(call => call == $"contact {User} {device}").Should().Be(3);
    }

    [Fact]
    public async Task The_last_device_cant_be_revoked()
    {
        var founder = Items.NewId();
        await _client.PostAsync("/circle", NewCircle(founder));

        var reply = await _client.DeleteAsync($"/circle/devices/{founder}");

        reply.Status.Should().Be(HttpStatusCode.Conflict);
        reply.Code.Should().Be("last_device");
    }

    [Fact]
    public async Task An_unknown_device_answers_404_unknown_device()
    {
        await CreateCircleAsync();

        var fetched = await _client.GetAsync($"/circle/devices/{Items.NewId()}/wrapped-aek");
        var revoked = await _client.DeleteAsync($"/circle/devices/{Items.NewId()}");

        fetched.Code.Should().Be("unknown_device");
        revoked.Code.Should().Be("unknown_device");
    }

    [Theory]
    [InlineData("/circle/devices/NOT-A-UUID/wrapped-aek")]
    [InlineData("/circle/joins/not-a-challenge")]
    public async Task A_malformed_id_in_the_path_answers_invalid_request(string path)
    {
        var reply = await _client.GetAsync(path);

        reply.Status.Should().Be(HttpStatusCode.BadRequest);
        reply.Code.Should().Be("invalid_request");
    }

    [Fact]
    public async Task A_failing_hook_doesnt_fail_the_request()
    {
        _host.Hooks.Throw = true;

        var reply = await _client.PostAsync("/circle", NewCircle(Items.NewId()));

        reply.Status.Should().Be(HttpStatusCode.NoContent);
        (await _client.GetAsync("/circle")).Status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Each_user_has_a_circle_of_their_own()
    {
        await CreateCircleAsync();

        var reply = await _host.Client("user-2").GetAsync("/circle/devices");

        reply.Body!["devices"]!.AsArray().Should().BeEmpty();
    }

    private async Task<string> CreateCircleAsync()
    {
        var body = NewCircle(Items.NewId());
        (await _client.PostAsync("/circle", body)).Status.Should().Be(HttpStatusCode.NoContent);
        return Field(body, "aekCommitment");
    }

    private async Task<string> AuthorizedJoinAsync(string deviceId)
    {
        var challengeId = (await _client.PostAsync("/circle/joins", JoinRequest(deviceId))).Body!["challengeId"]!.GetValue<string>();
        (await _client.PostAsync($"/circle/joins/{challengeId}/authorize", new { sealedAek = Sealed(), passphraseCheck = Sealed() }))
            .Status.Should().Be(HttpStatusCode.NoContent);
        return challengeId;
    }

    private static object NewCircle(string deviceId) => new
    {
        deviceId,
        aekCommitment = Base64(32),
        wrappedAek = WrappedAek(),
        deviceMeta = new { name = "Laptop", platform = "macOS" },
    };

    private static object WrappedAek() => new { wrappedKey = Base64(40), salt = Base64(16) };

    private static object JoinRequest(string deviceId) => new
    {
        deviceId,
        ephemeralPublicKey = Jwk(),
        createdAt = "2026-10-14T12:00:00.000Z",
        deviceMeta = new { name = "Phone", platform = "iOS" },
        addedLater = "a field the server doesn't know",
    };

    private static object Sealed() => new { ciphertext = Base64(48), iv = Base64(12), ephemeralPublicKey = Jwk() };

    private static object Jwk() => new { kty = "EC", crv = "P-256", x = Base64(32), y = Base64(32), ext = true };

    private static string Base64(int bytes) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));

    private static JsonNode ToNode(object value) => JsonSerializer.SerializeToNode(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static string Field(object value, string name) => (string)value.GetType().GetProperty(name)!.GetValue(value)!;
}
