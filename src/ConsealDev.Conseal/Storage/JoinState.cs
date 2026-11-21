// SPDX-License-Identifier: AGPL-3.0-or-later OR LicenseRef-Commercial

namespace ConsealDev.Conseal.Storage;

/// <summary>Where a join request stands.</summary>
public enum JoinState
{
    /// <summary>Waiting for a trusted device.</summary>
    Pending,

    /// <summary>A trusted device sealed the AEK to the request; the new device can finish.</summary>
    Authorized,

    /// <summary>A trusted device refused the request.</summary>
    Denied,

    /// <summary>Nobody decided before the request's time ran out.</summary>
    Expired,

    /// <summary>The new device saved its wrapped AEK naming the request, which used it up.</summary>
    Completed,
}
