using System.Collections.Concurrent;
using ConsealDev.Conseal.Hooks;

namespace ConsealDev.Conseal.Tests.TestSupport;

/// <summary>Hooks that remember each call, and can be told to throw.</summary>
internal sealed class RecordingHooks : IConsealSyncHooks
{
    public ConcurrentQueue<string> Calls { get; } = new();

    public bool Throw { get; set; }

    public Task OnDeviceJoinedAsync(string userId, string deviceId, DeviceJoinKind kind, CancellationToken cancellationToken) =>
        Record($"joined {userId} {deviceId} {kind}");

    public Task OnDeviceRevokedAsync(string userId, string deviceId, CancellationToken cancellationToken) =>
        Record($"revoked {userId} {deviceId}");

    public Task OnRevokedDeviceContactAsync(string userId, string deviceId, CancellationToken cancellationToken) =>
        Record($"contact {userId} {deviceId}");

    private Task Record(string call)
    {
        Calls.Enqueue(call);
        return Throw ? throw new InvalidOperationException("The test told this hook to fail.") : Task.CompletedTask;
    }
}
