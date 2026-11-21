// SPDX-License-Identifier: AGPL-3.0-or-later OR LicenseRef-Commercial

namespace ConsealDev.Conseal.Storage.InMemory;

/// <summary>Circles in memory, for tests and local development. One lock makes each step atomic.</summary>
internal sealed class InMemoryCircleStore : IConsealCircleStore
{
    private readonly Dictionary<string, Circle> _circles = [];
    private readonly Dictionary<string, Dictionary<string, JoinChallenge>> _joins = [];
    private readonly Lock _lock = new();

    public Task<string?> GetCommitmentAsync(string userId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(CircleOf(userId)?.Commitment);
        }
    }

    public Task<bool> CreateCircleAsync(string userId, string aekCommitment, DeviceSave founder, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_circles.ContainsKey(userId)) return Task.FromResult(false);

            var circle = new Circle(aekCommitment);
            circle.Devices[founder.DeviceId] = new CircleDevice(founder.DeviceId, founder.WrappedAek, founder.Name, founder.Platform, founder.At, RevokedAt: null);
            _circles[userId] = circle;
            return Task.FromResult(true);
        }
    }

    public Task<CircleDevice?> GetDeviceAsync(string userId, string deviceId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            CircleDevice? device = null;
            CircleOf(userId)?.Devices.TryGetValue(deviceId, out device);
            return Task.FromResult(device);
        }
    }

    public Task<IReadOnlyList<CircleDevice>> ListDevicesAsync(string userId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            IReadOnlyList<CircleDevice> devices = CircleOf(userId)?.Devices.Values.ToList() ?? [];
            return Task.FromResult(devices);
        }
    }

    public Task<DeviceSaveResult> SaveDeviceAsync(string userId, DeviceSave device, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var circle = CircleOf(userId);
            if (circle is null) return Task.FromResult(DeviceSaveResult.NoCircle);

            if (circle.Devices.TryGetValue(device.DeviceId, out var existing))
            {
                if (existing.RevokedAt is not null) return Task.FromResult(DeviceSaveResult.Revoked);
                circle.Devices[device.DeviceId] = existing with
                {
                    WrappedAek = device.WrappedAek,
                    Name = device.Name ?? existing.Name,
                    Platform = device.Platform ?? existing.Platform,
                };
                return Task.FromResult(DeviceSaveResult.Updated);
            }

            circle.Devices[device.DeviceId] = new CircleDevice(device.DeviceId, device.WrappedAek, device.Name, device.Platform, device.At, RevokedAt: null);
            return Task.FromResult(DeviceSaveResult.Added);
        }
    }

    public Task<DeviceRevokeResult> RevokeDeviceAsync(string userId, string deviceId, DateTimeOffset revokedAt, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var circle = CircleOf(userId);
            if (circle is null || !circle.Devices.TryGetValue(deviceId, out var device)) return Task.FromResult(DeviceRevokeResult.NotFound);
            if (device.RevokedAt is not null) return Task.FromResult(DeviceRevokeResult.AlreadyRevoked);
            if (circle.Devices.Values.Count(d => d.RevokedAt is null) <= 1) return Task.FromResult(DeviceRevokeResult.LastDevice);

            circle.Devices[deviceId] = device with { WrappedAek = null, RevokedAt = revokedAt };
            return Task.FromResult(DeviceRevokeResult.Revoked);
        }
    }

    public Task AddJoinAsync(string userId, JoinChallenge join, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            JoinsOf(userId)[join.ChallengeId] = join;
            return Task.CompletedTask;
        }
    }

    public Task<JoinChallenge?> GetJoinAsync(string userId, string challengeId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(JoinsOf(userId).GetValueOrDefault(challengeId));
        }
    }

    public Task<IReadOnlyList<JoinChallenge>> ListJoinsAsync(string userId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            IReadOnlyList<JoinChallenge> joins = JoinsOf(userId).Values.OrderBy(join => join.ReceivedAt).ToList();
            return Task.FromResult(joins);
        }
    }

    public Task<bool> ReplaceJoinAsync(string userId, JoinChallenge join, JoinState expected, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var joins = JoinsOf(userId);
            if (!joins.TryGetValue(join.ChallengeId, out var stored) || stored.State != expected) return Task.FromResult(false);
            joins[join.ChallengeId] = join;
            return Task.FromResult(true);
        }
    }

    public Task DeleteJoinAsync(string userId, string challengeId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            JoinsOf(userId).Remove(challengeId);
            return Task.CompletedTask;
        }
    }

    private Circle? CircleOf(string userId) => _circles.GetValueOrDefault(userId);

    private Dictionary<string, JoinChallenge> JoinsOf(string userId)
    {
        if (!_joins.TryGetValue(userId, out var joins))
        {
            joins = [];
            _joins[userId] = joins;
        }
        return joins;
    }

    private sealed class Circle(string commitment)
    {
        public string Commitment { get; } = commitment;

        public Dictionary<string, CircleDevice> Devices { get; } = [];
    }
}
