// SPDX-License-Identifier: AGPL-3.0-or-later OR LicenseRef-Commercial

using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConsealDev.Conseal.Wire;

/// <summary>
/// The circle routes' response bodies, shaped as <c>circle.yaml</c> defines them. The objects devices sent travel
/// back as the JSON they arrived as, so fields the server doesn't know survive.
/// </summary>
internal static class CircleWire
{
    public sealed record Circle(string AekCommitment);

    public sealed record Device(
        string DeviceId,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Platform,
        DateTimeOffset RegisteredAt);

    public sealed record DeviceList(IReadOnlyList<Device> Devices);

    public sealed record WrappedAekBody(JsonElement WrappedAek);

    public sealed record JoinCreated(string ChallengeId, string AekCommitment, DateTimeOffset ExpiresAt);

    public sealed record PendingJoin(string ChallengeId, JsonElement Request, DateTimeOffset ReceivedAt, DateTimeOffset ExpiresAt);

    public sealed record JoinList(IReadOnlyList<PendingJoin> Joins);

    public sealed record JoinStatus(
        string Status,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? SealedAek,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? PassphraseCheck);

    /// <summary>A stored JSON object, as an element the serializer writes out unchanged.</summary>
    public static JsonElement Raw(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
