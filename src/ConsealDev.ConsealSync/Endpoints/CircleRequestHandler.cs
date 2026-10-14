using System.Text.Json;
using ConsealDev.ConsealSync.Configuration;
using ConsealDev.ConsealSync.Hooks;
using ConsealDev.ConsealSync.Storage;
using ConsealDev.ConsealSync.Wire;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ConsealDev.ConsealSync.Endpoints;

/// <summary>
/// The circle routes of <c>circle.yaml</c>, and the ceremony's rules: the commitment written once, join requests
/// short-lived and used once by the server's clock, revoked devices kept out, and the last device kept in.
/// </summary>
internal sealed class CircleRequestHandler(
    IOptions<ConsealSyncOptions> options,
    IConsealCircleStore store,
    IConsealSyncHooks hooks,
    TimeProvider time,
    ILogger<CircleRequestHandler> logger)
{
    /// <summary>How long a join request waits for a decision, and then for the new device to finish.</summary>
    public static readonly TimeSpan JoinTtl = TimeSpan.FromMinutes(5);

    private readonly ConsealSyncOptions _options = options.Value;

    public async Task<IResult> GetCircleAsync(HttpContext context)
    {
        if (RequestUser.Find(context, _options) is not { } userId) return ApiResults.Unauthenticated();

        var commitment = await store.GetCommitmentAsync(userId, context.RequestAborted).ConfigureAwait(false);
        return commitment is null ? NoCircle() : ApiResults.Ok(new CircleWire.Circle(commitment));
    }

    public async Task<IResult> CreateCircleAsync(HttpContext context)
    {
        if (RequestUser.Find(context, _options) is not { } userId) return ApiResults.Unauthenticated();
        using var body = await ReadObjectAsync(context).ConfigureAwait(false);
        if (body is null) return InvalidBody();

        var root = body.RootElement;
        if (!CircleBody.TryGetDeviceId(root, out var deviceId)) return ApiResults.Invalid("deviceId must be a version 4 UUID in lowercase.");
        if (!CircleBody.TryGetCommitment(root, out var commitment)) return ApiResults.Invalid("aekCommitment must be base64 of 32 bytes.");
        if (!CircleBody.TryGetWrappedAek(root, "wrappedAek", out var wrappedAek)) return InvalidWrappedAek();
        if (!CircleBody.TryGetDeviceMeta(root, out var meta)) return InvalidDeviceMeta();

        var founder = new DeviceSave(deviceId, wrappedAek, meta.Name, meta.Platform, time.GetUtcNow());
        if (!await store.CreateCircleAsync(userId, commitment, founder, context.RequestAborted).ConfigureAwait(false))
        {
            return Conflict(ErrorCodes.CircleExists, "The account has a circle already, and its commitment never changes.");
        }

        await NotifyAsync(h => h.OnDeviceJoinedAsync(userId, deviceId, DeviceJoinKind.Founding, context.RequestAborted)).ConfigureAwait(false);
        return Results.NoContent();
    }

    public async Task<IResult> ListDevicesAsync(HttpContext context)
    {
        if (RequestUser.Find(context, _options) is not { } userId) return ApiResults.Unauthenticated();

        var devices = await store.ListDevicesAsync(userId, context.RequestAborted).ConfigureAwait(false);
        return ApiResults.Ok(new CircleWire.DeviceList(devices
            .Where(device => device.RevokedAt is null)
            .OrderBy(device => device.RegisteredAt)
            .Select(device => new CircleWire.Device(device.DeviceId, device.Name, device.Platform, device.RegisteredAt))
            .ToList()));
    }

    public async Task<IResult> RevokeDeviceAsync(HttpContext context, string deviceId)
    {
        if (RequestUser.Find(context, _options) is not { } userId) return ApiResults.Unauthenticated();
        if (!Ids.IsClientUuid(deviceId)) return InvalidDeviceId();

        var result = await store.RevokeDeviceAsync(userId, deviceId, time.GetUtcNow(), context.RequestAborted).ConfigureAwait(false);
        switch (result)
        {
            case DeviceRevokeResult.NotFound:
                return UnknownDevice();
            case DeviceRevokeResult.LastDevice:
                return Conflict(ErrorCodes.LastDevice, "The last device in the circle can't be revoked.");
            case DeviceRevokeResult.Revoked:
                await NotifyAsync(h => h.OnDeviceRevokedAsync(userId, deviceId, context.RequestAborted)).ConfigureAwait(false);
                return Results.NoContent();
            default:
                return Results.NoContent();
        }
    }

    public async Task<IResult> GetWrappedAekAsync(HttpContext context, string deviceId)
    {
        if (RequestUser.Find(context, _options) is not { } userId) return ApiResults.Unauthenticated();
        if (!Ids.IsClientUuid(deviceId)) return InvalidDeviceId();

        var device = await store.GetDeviceAsync(userId, deviceId, context.RequestAborted).ConfigureAwait(false);
        if (device is null) return UnknownDevice();
        if (device.RevokedAt is not null || device.WrappedAek is null) return await RevokedAsync(context, userId, deviceId).ConfigureAwait(false);
        return ApiResults.Ok(new CircleWire.WrappedAekBody(CircleWire.Raw(device.WrappedAek)));
    }

    public async Task<IResult> SaveWrappedAekAsync(HttpContext context, string deviceId)
    {
        if (RequestUser.Find(context, _options) is not { } userId) return ApiResults.Unauthenticated();
        if (!Ids.IsClientUuid(deviceId)) return InvalidDeviceId();
        using var body = await ReadObjectAsync(context).ConfigureAwait(false);
        if (body is null) return InvalidBody();

        var root = body.RootElement;
        if (!CircleBody.TryGetWrappedAek(root, "wrappedAek", out var wrappedAek)) return InvalidWrappedAek();
        if (!CircleBody.TryGetDeviceMeta(root, out var meta)) return InvalidDeviceMeta();
        if (!CircleBody.TryGetOptionalChallengeId(root, out var challengeId)) return ApiResults.Invalid("challengeId must be a join request's id.");

        var cancellationToken = context.RequestAborted;
        var existing = await store.GetDeviceAsync(userId, deviceId, cancellationToken).ConfigureAwait(false);
        if (existing?.RevokedAt is not null) return await RevokedAsync(context, userId, deviceId).ConfigureAwait(false);

        if (challengeId is not null)
        {
            var refusal = await ConsumeJoinAsync(userId, deviceId, challengeId, cancellationToken).ConfigureAwait(false);
            if (refusal is not null) return refusal;
        }

        var save = new DeviceSave(deviceId, wrappedAek, meta.Name, meta.Platform, time.GetUtcNow());
        var result = await store.SaveDeviceAsync(userId, save, cancellationToken).ConfigureAwait(false);
        if (challengeId is not null)
        {
            // The join is used up; deleting it purges the sealed AEK it carried.
            await store.DeleteJoinAsync(userId, challengeId, cancellationToken).ConfigureAwait(false);
        }

        switch (result)
        {
            case DeviceSaveResult.NoCircle:
                return NoCircle();
            case DeviceSaveResult.Revoked:
                return await RevokedAsync(context, userId, deviceId).ConfigureAwait(false);
            case DeviceSaveResult.Added:
                var kind = challengeId is null ? DeviceJoinKind.Recovery : DeviceJoinKind.Ceremony;
                await NotifyAsync(h => h.OnDeviceJoinedAsync(userId, deviceId, kind, cancellationToken)).ConfigureAwait(false);
                return Results.NoContent();
            default:
                return Results.NoContent();
        }
    }

    public async Task<IResult> ListJoinsAsync(HttpContext context)
    {
        if (RequestUser.Find(context, _options) is not { } userId) return ApiResults.Unauthenticated();

        var now = time.GetUtcNow();
        var joins = await store.ListJoinsAsync(userId, context.RequestAborted).ConfigureAwait(false);
        return ApiResults.Ok(new CircleWire.JoinList(joins
            .Where(join => join.State == JoinState.Pending && join.ExpiresAt > now)
            .OrderBy(join => join.ReceivedAt)
            .Select(join => new CircleWire.PendingJoin(join.ChallengeId, CircleWire.Raw(join.Request), join.ReceivedAt, join.ExpiresAt))
            .ToList()));
    }

    public async Task<IResult> CreateJoinAsync(HttpContext context)
    {
        if (RequestUser.Find(context, _options) is not { } userId) return ApiResults.Unauthenticated();
        using var body = await ReadObjectAsync(context).ConfigureAwait(false);
        if (body is null) return InvalidBody();
        if (!CircleBody.IsJoinRequest(body.RootElement, out var deviceId, out var request))
        {
            return ApiResults.Invalid(
                "A join request needs deviceId, an EC P-256 public JWK as ephemeralPublicKey, createdAt, and at most 8 KB.");
        }

        var cancellationToken = context.RequestAborted;
        var commitment = await store.GetCommitmentAsync(userId, cancellationToken).ConfigureAwait(false);
        if (commitment is null) return NoCircle();

        var device = await store.GetDeviceAsync(userId, deviceId, cancellationToken).ConfigureAwait(false);
        if (device?.RevokedAt is not null) return await RevokedAsync(context, userId, deviceId).ConfigureAwait(false);
        if (device is not null) return Conflict(ErrorCodes.DeviceRegistered, "The device is in the circle already.");

        // One waiting request per device: a new one replaces it, as when the device starts over.
        foreach (var waiting in await store.ListJoinsAsync(userId, cancellationToken).ConfigureAwait(false))
        {
            if (waiting.DeviceId == deviceId && waiting.State == JoinState.Pending)
            {
                await store.DeleteJoinAsync(userId, waiting.ChallengeId, cancellationToken).ConfigureAwait(false);
            }
        }

        var now = time.GetUtcNow();
        var join = new JoinChallenge(Ids.NewRandomId(), deviceId, request, JoinState.Pending, now, now + JoinTtl, SealedAek: null, PassphraseCheck: null);
        await store.AddJoinAsync(userId, join, cancellationToken).ConfigureAwait(false);
        return ApiResults.Created(new CircleWire.JoinCreated(join.ChallengeId, commitment, join.ExpiresAt));
    }

    public async Task<IResult> GetJoinAsync(HttpContext context, string challengeId)
    {
        if (RequestUser.Find(context, _options) is not { } userId) return ApiResults.Unauthenticated();
        if (!Ids.IsChallengeId(challengeId)) return InvalidChallengeId();

        var join = await FindCurrentJoinAsync(userId, challengeId, context.RequestAborted).ConfigureAwait(false);
        if (join is null) return UnknownJoin();

        return join.State switch
        {
            JoinState.Authorized => ApiResults.Ok(new CircleWire.JoinStatus(
                "authorized",
                CircleWire.Raw(join.SealedAek!),
                CircleWire.Raw(join.PassphraseCheck!))),
            JoinState.Denied => ApiResults.Ok(new CircleWire.JoinStatus("denied", null, null)),
            JoinState.Expired => ApiResults.Ok(new CircleWire.JoinStatus("expired", null, null)),
            _ => ApiResults.Ok(new CircleWire.JoinStatus("pending", null, null)),
        };
    }

    public async Task<IResult> AuthorizeJoinAsync(HttpContext context, string challengeId)
    {
        if (RequestUser.Find(context, _options) is not { } userId) return ApiResults.Unauthenticated();
        if (!Ids.IsChallengeId(challengeId)) return InvalidChallengeId();
        using var body = await ReadObjectAsync(context).ConfigureAwait(false);
        if (body is null) return InvalidBody();

        var root = body.RootElement;
        if (!CircleBody.TryGetSealed(root, "sealedAek", out var sealedAek))
        {
            return ApiResults.Invalid("sealedAek must be a sealed payload: base64 ciphertext and iv, and a public JWK, at most 8 KB.");
        }
        if (!CircleBody.TryGetSealed(root, "passphraseCheck", out var passphraseCheck))
        {
            return ApiResults.Invalid("passphraseCheck must be a sealed payload: base64 ciphertext and iv, and a public JWK, at most 8 KB.");
        }

        var cancellationToken = context.RequestAborted;
        var join = await FindCurrentJoinAsync(userId, challengeId, cancellationToken).ConfigureAwait(false);
        if (join is null) return UnknownJoin();
        if (join.State == JoinState.Expired) return JoinExpired();
        if (join.State != JoinState.Pending) return JoinNotPending(join.State);

        // A fresh five minutes for the new device to unseal the AEK and finish.
        var authorized = join with { State = JoinState.Authorized, SealedAek = sealedAek, PassphraseCheck = passphraseCheck, ExpiresAt = time.GetUtcNow() + JoinTtl };
        if (!await store.ReplaceJoinAsync(userId, authorized, JoinState.Pending, cancellationToken).ConfigureAwait(false))
        {
            return Conflict(ErrorCodes.JoinNotPending, "The join request was decided at the same time.");
        }
        return Results.NoContent();
    }

    public async Task<IResult> DenyJoinAsync(HttpContext context, string challengeId)
    {
        if (RequestUser.Find(context, _options) is not { } userId) return ApiResults.Unauthenticated();
        if (!Ids.IsChallengeId(challengeId)) return InvalidChallengeId();

        var cancellationToken = context.RequestAborted;
        var join = await FindJoinAsync(userId, challengeId, cancellationToken).ConfigureAwait(false);
        if (join is null) return UnknownJoin();
        if (join.State != JoinState.Pending) return JoinNotPending(join.State);

        if (!await store.ReplaceJoinAsync(userId, join with { State = JoinState.Denied }, JoinState.Pending, cancellationToken).ConfigureAwait(false))
        {
            return Conflict(ErrorCodes.JoinNotPending, "The join request was decided at the same time.");
        }
        return Results.NoContent();
    }

    /// <summary>Uses up the join a device names as it saves its wrapped AEK; null once it is used, else the refusal.</summary>
    private async Task<IResult?> ConsumeJoinAsync(string userId, string deviceId, string challengeId, CancellationToken cancellationToken)
    {
        var join = await FindCurrentJoinAsync(userId, challengeId, cancellationToken).ConfigureAwait(false);
        if (join is null) return UnknownJoin();
        if (join.DeviceId != deviceId)
        {
            return ApiResults.Error(StatusCodes.Status403Forbidden, ErrorCodes.JoinMismatch, "The join request is for another device.");
        }
        if (join.State == JoinState.Expired) return JoinExpired();
        if (join.State != JoinState.Authorized)
        {
            return Conflict(ErrorCodes.JoinNotAuthorized, "The join request isn't authorized.");
        }

        var completed = join with { State = JoinState.Completed, SealedAek = null, PassphraseCheck = null };
        return await store.ReplaceJoinAsync(userId, completed, JoinState.Authorized, cancellationToken).ConfigureAwait(false)
            ? null
            : Conflict(ErrorCodes.JoinNotAuthorized, "The join request was used at the same time.");
    }

    /// <summary>A join request, unless it is used up: a completed one is as good as deleted.</summary>
    private async Task<JoinChallenge?> FindJoinAsync(string userId, string challengeId, CancellationToken cancellationToken)
    {
        var join = await store.GetJoinAsync(userId, challengeId, cancellationToken).ConfigureAwait(false);
        return join?.State == JoinState.Completed ? null : join;
    }

    /// <summary>
    /// A join request as it now stands: one whose time ran out is marked expired first, dropping any sealed AEK, so the
    /// routes answer it the same whether or not a poll found it first.
    /// </summary>
    private async Task<JoinChallenge?> FindCurrentJoinAsync(string userId, string challengeId, CancellationToken cancellationToken)
    {
        var join = await FindJoinAsync(userId, challengeId, cancellationToken).ConfigureAwait(false);
        if (join is null || join.State is not (JoinState.Pending or JoinState.Authorized) || time.GetUtcNow() <= join.ExpiresAt) return join;

        var expired = join with { State = JoinState.Expired, SealedAek = null, PassphraseCheck = null };
        if (await store.ReplaceJoinAsync(userId, expired, join.State, cancellationToken).ConfigureAwait(false)) return expired;
        return await FindJoinAsync(userId, challengeId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IResult> RevokedAsync(HttpContext context, string userId, string deviceId)
    {
        await NotifyAsync(h => h.OnRevokedDeviceContactAsync(userId, deviceId, context.RequestAborted)).ConfigureAwait(false);
        return ApiResults.Error(StatusCodes.Status403Forbidden, ErrorCodes.DeviceRevoked, "The device is revoked. Join or recover again.");
    }

    /// <summary>
    /// Runs a hook after its change is stored. A hook that fails is logged rather than failing the request: the change
    /// stands either way, and a device told otherwise would retry a step that is already done.
    /// </summary>
    private async Task NotifyAsync(Func<IConsealSyncHooks, Task> hook)
    {
        try
        {
            await hook(hooks).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "A Conseal Sync hook failed; the change it reports is stored.");
        }
    }

    private static async Task<JsonDocument?> ReadObjectAsync(HttpContext context)
    {
        var body = await RequestBody.ReadAsync(context.Request, CircleBody.MaxBodyBytes, context.RequestAborted).ConfigureAwait(false);
        return body is null ? null : CircleBody.ParseObject(body);
    }

    private static IResult Conflict(string code, string message) => ApiResults.Error(StatusCodes.Status409Conflict, code, message);

    private static IResult NoCircle() =>
        ApiResults.Error(StatusCodes.Status404NotFound, ErrorCodes.NoCircle, "The account has no circle yet.");

    private static IResult UnknownDevice() =>
        ApiResults.Error(StatusCodes.Status404NotFound, ErrorCodes.UnknownDevice, "The circle has no such device.");

    private static IResult UnknownJoin() =>
        ApiResults.Error(StatusCodes.Status404NotFound, ErrorCodes.UnknownJoin, "There is no such join request, or it was used.");

    private static IResult JoinExpired() =>
        ApiResults.Error(StatusCodes.Status410Gone, ErrorCodes.JoinExpired, "The join request expired. Start the join again.");

    private static IResult JoinNotPending(JoinState state) =>
        Conflict(ErrorCodes.JoinNotPending, $"The join request is {state.ToString().ToLowerInvariant()} already.");

    private static IResult InvalidBody() => ApiResults.Invalid($"The body must be a JSON object of at most {CircleBody.MaxBodyBytes} bytes.");

    private static IResult InvalidDeviceId() => ApiResults.Invalid("deviceId must be a version 4 UUID in lowercase.");

    private static IResult InvalidChallengeId() => ApiResults.Invalid("challengeId must be 32 lowercase hex characters.");

    private static IResult InvalidWrappedAek() =>
        ApiResults.Invalid("wrappedAek must be an object with base64 wrappedKey and salt, at most 8 KB.");

    private static IResult InvalidDeviceMeta() =>
        ApiResults.Invalid("deviceMeta must be an object with a name of at most 256 characters and a platform of at most 64.");
}
