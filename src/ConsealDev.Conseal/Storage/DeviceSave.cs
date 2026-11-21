// SPDX-License-Identifier: AGPL-3.0-or-later OR LicenseRef-Commercial

namespace ConsealDev.Conseal.Storage;

/// <summary>A device's wrapped AEK to store, adding the device when the circle doesn't have it yet.</summary>
/// <param name="DeviceId">The device's id.</param>
/// <param name="WrappedAek">The wrapped AEK, as the JSON object the device sent.</param>
/// <param name="Name">The device's name; null keeps the one stored.</param>
/// <param name="Platform">The device's platform; null keeps the one stored.</param>
/// <param name="At">Now, by the server's clock: the device's registration time if it is new.</param>
public sealed record DeviceSave(string DeviceId, string WrappedAek, string? Name, string? Platform, DateTimeOffset At);
