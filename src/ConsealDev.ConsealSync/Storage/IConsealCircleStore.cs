namespace ConsealDev.ConsealSync.Storage;

/// <summary>
/// Where the circle routes keep each user's circle: the AEK commitment, the devices with their wrapped AEKs, and the
/// join requests in flight. Everything in it is opaque to the server. The routes hold the ceremony's rules; a store
/// makes each step atomic.
/// </summary>
public interface IConsealCircleStore
{
    /// <summary>The user's AEK commitment, base64; null when the user has no circle.</summary>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<string?> GetCommitmentAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Stores the commitment and the founding device together, unless the user has a circle already.</summary>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="aekCommitment">The AEK commitment, base64. It is never changed afterwards.</param>
    /// <param name="founder">The first device.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>False when the user has a circle already, and nothing was written.</returns>
    Task<bool> CreateCircleAsync(string userId, string aekCommitment, DeviceSave founder, CancellationToken cancellationToken);

    /// <summary>One device, revoked or not; null when the circle has no such device.</summary>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="deviceId">The device.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<CircleDevice?> GetDeviceAsync(string userId, string deviceId, CancellationToken cancellationToken);

    /// <summary>Every device the circle has had, revoked ones included.</summary>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<CircleDevice>> ListDevicesAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Stores a device's wrapped AEK, adding the device if it is new. A revoked device stays revoked.</summary>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="device">The device and its wrapped AEK.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task<DeviceSaveResult> SaveDeviceAsync(string userId, DeviceSave device, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes a device and deletes its wrapped AEK, unless it is the circle's last device. The check and the revoke
    /// are one atomic step, so two revokes at once can't take out the last two devices.
    /// </summary>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="deviceId">The device.</param>
    /// <param name="revokedAt">Now, by the server's clock.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task<DeviceRevokeResult> RevokeDeviceAsync(string userId, string deviceId, DateTimeOffset revokedAt, CancellationToken cancellationToken);

    /// <summary>Stores a new join request.</summary>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="join">The request, pending.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task AddJoinAsync(string userId, JoinChallenge join, CancellationToken cancellationToken);

    /// <summary>One join request; null when there is no such request, or the store has dropped it.</summary>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="challengeId">The request's id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<JoinChallenge?> GetJoinAsync(string userId, string challengeId, CancellationToken cancellationToken);

    /// <summary>The user's join requests in any state, oldest first.</summary>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<JoinChallenge>> ListJoinsAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Replaces a join request, only if it is still in <paramref name="expected"/>.</summary>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="join">The request in its new state.</param>
    /// <param name="expected">The state the stored request must be in.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>False when the request is gone or in another state, and nothing was written.</returns>
    Task<bool> ReplaceJoinAsync(string userId, JoinChallenge join, JoinState expected, CancellationToken cancellationToken);

    /// <summary>Deletes a join request, and the sealed AEK it holds. Deleting one that isn't there succeeds.</summary>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="challengeId">The request's id.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task DeleteJoinAsync(string userId, string challengeId, CancellationToken cancellationToken);
}
