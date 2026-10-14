using ConsealDev.ConsealSync.Storage;
using ConsealDev.ConsealSync.Tests.TestSupport;
using FluentAssertions;

namespace ConsealDev.ConsealSync.Tests.Storage;

/// <summary>What every <see cref="IConsealCircleStore"/> does, run against each store.</summary>
public abstract class CircleStoreContractTests
{
    private const string User = "user-1";
    private const string Commitment = "c29tZSBjb21taXRtZW50IG9mIDMyIGJ5dGVzIGxvbmc=";
    private const string WrappedAek = """{"wrappedKey":"AAAA","salt":"AAAA"}""";

    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_circle_is_created_once_with_its_founding_device()
    {
        var store = await CreateStoreAsync();
        var founder = Device(Items.NewId(), name: "Laptop");

        var created = await store.CreateCircleAsync(User, Commitment, founder, default);
        var again = await store.CreateCircleAsync(User, "b3RoZXI=", Device(Items.NewId()), default);

        created.Should().BeTrue();
        again.Should().BeFalse();
        (await store.GetCommitmentAsync(User, default)).Should().Be(Commitment);
        (await store.ListDevicesAsync(User, default)).Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new CircleDevice(founder.DeviceId, WrappedAek, "Laptop", null, Now, null));
    }

    [Fact]
    public async Task A_user_without_a_circle_has_no_commitment_and_no_devices()
    {
        var store = await CreateStoreAsync();

        (await store.GetCommitmentAsync(User, default)).Should().BeNull();
        (await store.ListDevicesAsync(User, default)).Should().BeEmpty();
        (await store.SaveDeviceAsync(User, Device(Items.NewId()), default)).Should().Be(DeviceSaveResult.NoCircle);
    }

    [Fact]
    public async Task Saving_adds_a_new_device_and_updates_a_known_one_keeping_what_it_didnt_say()
    {
        var store = await CreateStoreAsync();
        await store.CreateCircleAsync(User, Commitment, Device(Items.NewId()), default);
        var id = Items.NewId();

        var added = await store.SaveDeviceAsync(User, Device(id, name: "Phone", platform: "iOS"), default);
        var updated = await store.SaveDeviceAsync(User, Device(id, wrappedAek: """{"wrappedKey":"BBBB","salt":"BBBB"}""") with { At = Now.AddDays(1) }, default);

        added.Should().Be(DeviceSaveResult.Added);
        updated.Should().Be(DeviceSaveResult.Updated);
        (await store.GetDeviceAsync(User, id, default)).Should().BeEquivalentTo(
            new CircleDevice(id, """{"wrappedKey":"BBBB","salt":"BBBB"}""", "Phone", "iOS", Now, null));
    }

    [Fact]
    public async Task Revoking_drops_the_wrapped_aek_and_keeps_the_device_out()
    {
        var store = await CreateStoreAsync();
        await store.CreateCircleAsync(User, Commitment, Device(Items.NewId()), default);
        var id = Items.NewId();
        await store.SaveDeviceAsync(User, Device(id), default);

        var revoked = await store.RevokeDeviceAsync(User, id, Now.AddHours(1), default);
        var again = await store.RevokeDeviceAsync(User, id, Now.AddHours(2), default);
        var saved = await store.SaveDeviceAsync(User, Device(id), default);

        revoked.Should().Be(DeviceRevokeResult.Revoked);
        again.Should().Be(DeviceRevokeResult.AlreadyRevoked);
        saved.Should().Be(DeviceSaveResult.Revoked);
        var device = await store.GetDeviceAsync(User, id, default);
        device!.WrappedAek.Should().BeNull();
        device.RevokedAt.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public async Task The_last_device_and_unknown_devices_cant_be_revoked()
    {
        var store = await CreateStoreAsync();
        var founder = Items.NewId();
        await store.CreateCircleAsync(User, Commitment, Device(founder), default);

        (await store.RevokeDeviceAsync(User, founder, Now, default)).Should().Be(DeviceRevokeResult.LastDevice);
        (await store.RevokeDeviceAsync(User, Items.NewId(), Now, default)).Should().Be(DeviceRevokeResult.NotFound);
        (await store.RevokeDeviceAsync("user-2", founder, Now, default)).Should().Be(DeviceRevokeResult.NotFound);
    }

    [Fact]
    public async Task Two_revokes_at_once_leave_one_device_in_the_circle()
    {
        var store = await CreateStoreAsync();
        var first = Items.NewId();
        var second = Items.NewId();
        await store.CreateCircleAsync(User, Commitment, Device(first), default);
        await store.SaveDeviceAsync(User, Device(second), default);

        var results = await Task.WhenAll(
            Task.Run(() => store.RevokeDeviceAsync(User, first, Now, default)),
            Task.Run(() => store.RevokeDeviceAsync(User, second, Now, default)));

        results.Should().BeEquivalentTo([DeviceRevokeResult.Revoked, DeviceRevokeResult.LastDevice]);
        (await store.ListDevicesAsync(User, default)).Count(device => device.RevokedAt is null).Should().Be(1);
    }

    [Fact]
    public async Task A_join_is_replaced_only_from_the_state_it_is_in()
    {
        var store = await CreateStoreAsync();
        var join = Join();
        await store.AddJoinAsync(User, join, default);

        var authorized = await store.ReplaceJoinAsync(User, join with { State = JoinState.Authorized, SealedAek = "{}" }, JoinState.Pending, default);
        var denied = await store.ReplaceJoinAsync(User, join with { State = JoinState.Denied }, JoinState.Pending, default);

        authorized.Should().BeTrue();
        denied.Should().BeFalse();
        (await store.GetJoinAsync(User, join.ChallengeId, default)).Should().BeEquivalentTo(join with { State = JoinState.Authorized, SealedAek = "{}" });
    }

    [Fact]
    public async Task Joins_are_listed_oldest_first_and_deleted_on_request()
    {
        var store = await CreateStoreAsync();
        var later = Join() with { ReceivedAt = Now.AddMinutes(1) };
        var earlier = Join();
        await store.AddJoinAsync(User, later, default);
        await store.AddJoinAsync(User, earlier, default);

        var listed = await store.ListJoinsAsync(User, default);
        await store.DeleteJoinAsync(User, later.ChallengeId, default);
        await store.DeleteJoinAsync(User, "0123456789abcdef0123456789abcdef", default);

        listed.Select(join => join.ChallengeId).Should().Equal(earlier.ChallengeId, later.ChallengeId);
        (await store.GetJoinAsync(User, later.ChallengeId, default)).Should().BeNull();
        (await store.ListJoinsAsync(User, default)).Should().ContainSingle();
    }

    [Fact]
    public async Task Replacing_a_join_that_isnt_there_writes_nothing()
    {
        var store = await CreateStoreAsync();
        var join = Join();

        var replaced = await store.ReplaceJoinAsync(User, join with { State = JoinState.Denied }, JoinState.Pending, default);

        replaced.Should().BeFalse();
        (await store.GetJoinAsync(User, join.ChallengeId, default)).Should().BeNull();
    }

    protected abstract Task<IConsealCircleStore> CreateStoreAsync();

    private static DeviceSave Device(string id, string? name = null, string? platform = null, string wrappedAek = WrappedAek) =>
        new(id, wrappedAek, name, platform, Now);

    private static JoinChallenge Join() => new(
        Guid.NewGuid().ToString("N"),
        Items.NewId(),
        """{"deviceId":"x"}""",
        JoinState.Pending,
        Now,
        Now.AddMinutes(5),
        SealedAek: null,
        PassphraseCheck: null);
}
