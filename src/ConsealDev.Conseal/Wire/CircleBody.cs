using System.Text;
using System.Text.Json;

namespace ConsealDev.Conseal.Wire;

/// <summary>
/// Checks the objects devices send to the circle routes: their shape and size, never their content. Each check keeps
/// the object's JSON as it arrived, to store and hand back unchanged.
/// </summary>
internal static class CircleBody
{
    /// <summary>The largest body a circle route reads.</summary>
    public const int MaxBodyBytes = 64 * 1024;

    private const int MaxObjectBytes = 8 * 1024;
    private const int MaxNameLength = 256;
    private const int MaxPlatformLength = 64;
    private const int CommitmentBytes = 32;

    /// <summary>The body as a JSON object; null when it is anything else.</summary>
    public static JsonDocument? ParseObject(byte[] body)
    {
        try
        {
            var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object) return document;
            document.Dispose();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A wrapped AEK: an object with base64 <c>wrappedKey</c> and <c>salt</c>.</summary>
    public static bool TryGetWrappedAek(JsonElement body, string name, out string raw) =>
        TryGetObject(body, name, IsWrappedAek, out raw);

    /// <summary>A payload sealed to an ECDH key: base64 <c>ciphertext</c> and <c>iv</c>, and the sender's public key.</summary>
    public static bool TryGetSealed(JsonElement body, string name, out string raw) =>
        TryGetObject(body, name, IsSealed, out raw);

    /// <summary>The AEK commitment: base64 of a 32-byte SHA-256.</summary>
    public static bool TryGetCommitment(JsonElement body, out string commitment)
    {
        commitment = string.Empty;
        if (!body.TryGetProperty("aekCommitment", out var value) || value.ValueKind != JsonValueKind.String) return false;
        commitment = value.GetString()!;
        return Base64Bytes.TryDecode(commitment, out var bytes) && bytes.Length == CommitmentBytes;
    }

    /// <summary>An optional <c>deviceMeta</c>: absent, null, or an object with string <c>name</c> and <c>platform</c>.</summary>
    public static bool TryGetDeviceMeta(JsonElement body, out DeviceMetaBody meta)
    {
        meta = default;
        if (!body.TryGetProperty("deviceMeta", out var value) || value.ValueKind == JsonValueKind.Null) return true;
        if (value.ValueKind != JsonValueKind.Object) return false;
        if (!TryGetOptionalString(value, "name", MaxNameLength, out var name)) return false;
        if (!TryGetOptionalString(value, "platform", MaxPlatformLength, out var platform)) return false;
        meta = new DeviceMetaBody(name, platform);
        return true;
    }

    /// <summary>An optional <c>challengeId</c>: absent, null, or a join request's id.</summary>
    public static bool TryGetOptionalChallengeId(JsonElement body, out string? challengeId)
    {
        challengeId = null;
        if (!body.TryGetProperty("challengeId", out var value) || value.ValueKind == JsonValueKind.Null) return true;
        challengeId = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return Ids.IsChallengeId(challengeId);
    }

    /// <summary>A device id in a body.</summary>
    public static bool TryGetDeviceId(JsonElement body, out string deviceId)
    {
        deviceId = string.Empty;
        if (!body.TryGetProperty("deviceId", out var value) || value.ValueKind != JsonValueKind.String) return false;
        deviceId = value.GetString()!;
        return Ids.IsClientUuid(deviceId);
    }

    /// <summary>
    /// A join request, the whole body: the device's id, its throwaway public key and its own clock's time, with an
    /// optional <c>deviceMeta</c>.
    /// </summary>
    public static bool IsJoinRequest(JsonElement body, out string deviceId, out string raw)
    {
        raw = body.GetRawText();
        return TryGetDeviceId(body, out deviceId)
            && body.TryGetProperty("ephemeralPublicKey", out var key) && IsPublicJwk(key)
            && body.TryGetProperty("createdAt", out var createdAt) && createdAt.ValueKind == JsonValueKind.String
            && TryGetDeviceMeta(body, out _)
            && Encoding.UTF8.GetByteCount(raw) <= MaxObjectBytes;
    }

    private static bool TryGetObject(JsonElement body, string name, Func<JsonElement, bool> isValid, out string raw)
    {
        raw = string.Empty;
        if (!body.TryGetProperty(name, out var value) || !isValid(value)) return false;
        raw = value.GetRawText();
        return Encoding.UTF8.GetByteCount(raw) <= MaxObjectBytes;
    }

    private static bool IsWrappedAek(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object
        && IsBase64Property(value, "wrappedKey")
        && IsBase64Property(value, "salt");

    private static bool IsSealed(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object
        && IsBase64Property(value, "ciphertext")
        && IsBase64Property(value, "iv")
        && value.TryGetProperty("ephemeralPublicKey", out var key) && IsPublicJwk(key);

    /// <summary>An EC P-256 public key as a JWK. One that carries the private part <c>d</c> is refused.</summary>
    private static bool IsPublicJwk(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object
        && IsStringProperty(value, "kty", "EC")
        && IsStringProperty(value, "crv", "P-256")
        && IsNonEmptyStringProperty(value, "x")
        && IsNonEmptyStringProperty(value, "y")
        && !value.TryGetProperty("d", out _);

    private static bool IsBase64Property(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.String
        && Base64Bytes.TryDecode(property.GetString()!, out _);

    private static bool IsStringProperty(JsonElement value, string name, string expected) =>
        value.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.String
        && property.GetString() == expected;

    private static bool IsNonEmptyStringProperty(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.String
        && property.GetString()!.Length > 0;

    private static bool TryGetOptionalString(JsonElement value, string name, int maxLength, out string? result)
    {
        result = null;
        if (!value.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null) return true;
        if (property.ValueKind != JsonValueKind.String) return false;
        result = property.GetString()!;
        return result.Length <= maxLength;
    }
}
