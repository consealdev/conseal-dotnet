// SPDX-License-Identifier: AGPL-3.0-or-later OR LicenseRef-Commercial

namespace ConsealDev.Conseal.Storage;

/// <summary>What became of a revoke.</summary>
public enum DeviceRevokeResult
{
    /// <summary>The device is revoked and its wrapped AEK deleted.</summary>
    Revoked,

    /// <summary>The device was revoked already.</summary>
    AlreadyRevoked,

    /// <summary>The circle has no such device.</summary>
    NotFound,

    /// <summary>The device is the circle's last one, so it stays.</summary>
    LastDevice,
}
