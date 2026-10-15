namespace ConsealDev.Conseal.Hooks;

/// <summary>
/// Implement this in the app to hear about changes to a user's circle, such as to email the user when a device joins
/// or a revoked one tries to reach the server. Register it with <c>WithHooks&lt;T&gt;()</c>.
/// </summary>
/// <remarks>
/// Each hook runs after the change is stored. One that throws is logged and doesn't fail the request, so a broken
/// notification can't leave a device halfway through the ceremony.
/// </remarks>
public interface IConsealSyncHooks
{
    /// <summary>Called when a device joins the user's circle.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="deviceId">The new device.</param>
    /// <param name="kind">How it joined.</param>
    /// <param name="cancellationToken">Cancelled when the request is aborted.</param>
    Task OnDeviceJoinedAsync(string userId, string deviceId, DeviceJoinKind kind, CancellationToken cancellationToken);

    /// <summary>Called when a device is revoked.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="deviceId">The revoked device.</param>
    /// <param name="cancellationToken">Cancelled when the request is aborted.</param>
    Task OnDeviceRevokedAsync(string userId, string deviceId, CancellationToken cancellationToken);

    /// <summary>
    /// Called when a request names a revoked device: it asked for its wrapped AEK, saved one, or asked to join again.
    /// The server refuses it; the device still holds the AEK it had, so the user should know.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="deviceId">The revoked device.</param>
    /// <param name="cancellationToken">Cancelled when the request is aborted.</param>
    Task OnRevokedDeviceContactAsync(string userId, string deviceId, CancellationToken cancellationToken);
}
