namespace ConsealDev.Conseal.Hooks;

/// <summary>The hooks used when the app registers none.</summary>
internal sealed class NullConsealSyncHooks : IConsealSyncHooks
{
    public Task OnDeviceJoinedAsync(string userId, string deviceId, DeviceJoinKind kind, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task OnDeviceRevokedAsync(string userId, string deviceId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task OnRevokedDeviceContactAsync(string userId, string deviceId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
