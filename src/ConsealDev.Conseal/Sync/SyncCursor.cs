// SPDX-License-Identifier: AGPL-3.0-or-later OR LicenseRef-Commercial

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using ConsealDev.Conseal.Configuration;
using Microsoft.Extensions.Options;

namespace ConsealDev.Conseal.Sync;

/// <summary>
/// The pull cursor: <c>{userId, collectionId, seq}</c> as JSON, after an HMAC-SHA256 of it under the server's key,
/// base64url-encoded. It is signed, not encrypted: the client can read its own user id and position, but can't make
/// a cursor the server didn't issue, or use one for another user or collection.
/// </summary>
internal sealed class SyncCursor(IOptions<ConsealSyncOptions> options)
{
    /// <summary>The longest cursor the contract allows.</summary>
    public const int MaxLength = 1024;

    private const int MacBytes = 32;

    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    private readonly byte[] _key = Convert.FromBase64String(options.Value.CursorSigningKey);

    public string Encode(string userId, string collectionId, long seq)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new CursorPayload(userId, collectionId, seq), PayloadJson);
        var signed = new byte[MacBytes + payload.Length];
        HMACSHA256.HashData(_key, payload, signed.AsSpan(0, MacBytes));
        payload.CopyTo(signed.AsSpan(MacBytes));
        return Base64Url.EncodeToString(signed);
    }

    /// <summary>The position a cursor names, if this server issued it for this user and collection.</summary>
    public bool TryDecode(string cursor, string userId, string collectionId, out long seq)
    {
        seq = 0;
        if (cursor.Length > MaxLength || !Base64Url.IsValid(cursor)) return false;

        var signed = Base64Url.DecodeFromChars(cursor);
        if (signed.Length <= MacBytes) return false;

        var payload = signed.AsSpan(MacBytes);
        Span<byte> expected = stackalloc byte[MacBytes];
        HMACSHA256.HashData(_key, payload, expected);
        if (!CryptographicOperations.FixedTimeEquals(signed.AsSpan(0, MacBytes), expected)) return false;

        // The MAC matched, so this server wrote the payload; it is still checked against the request it came with.
        var decoded = JsonSerializer.Deserialize<CursorPayload>(payload, PayloadJson);
        if (decoded is null || decoded.UserId != userId || decoded.CollectionId != collectionId || decoded.Seq < 0)
        {
            return false;
        }
        seq = decoded.Seq;
        return true;
    }

    private sealed record CursorPayload(string UserId, string CollectionId, long Seq);
}
