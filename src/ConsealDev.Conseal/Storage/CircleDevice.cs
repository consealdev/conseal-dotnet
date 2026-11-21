// SPDX-License-Identifier: AGPL-3.0-or-later OR LicenseRef-Commercial

namespace ConsealDev.Conseal.Storage;

/// <summary>A device in a user's circle, as the server holds it.</summary>
/// <param name="DeviceId">The device's id, a lowercase version 4 UUID.</param>
/// <param name="WrappedAek">The device's wrapped AEK, as the JSON object it sent; null once the device is revoked.</param>
/// <param name="Name">What the device calls itself, if it said.</param>
/// <param name="Platform">The device's platform, if it said.</param>
/// <param name="RegisteredAt">When the server added the device.</param>
/// <param name="RevokedAt">When the device was revoked; null while it is in the circle.</param>
public sealed record CircleDevice(
    string DeviceId,
    string? WrappedAek,
    string? Name,
    string? Platform,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? RevokedAt);
