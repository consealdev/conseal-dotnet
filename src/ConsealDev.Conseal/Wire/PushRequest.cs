// SPDX-License-Identifier: AGPL-3.0-or-later OR LicenseRef-Commercial

using System.Text.Json;
using ConsealDev.Conseal.Storage;

namespace ConsealDev.Conseal.Wire;

/// <summary>
/// Reads a push body. A body that isn't a push at all fails as a whole; each item is then checked on its own, so one
/// bad item gets its own <c>400</c> or <c>413</c> and doesn't fail the others.
/// </summary>
internal static class PushRequest
{
    private const int IvBytes = 12;
    private const int WrappedDekBytes = 60;
    private const int TagBytes = 16;
    private const int MaxVersionLength = 64;

    public static PushParse Parse(byte[] body, int maxBatchItems, int maxRecordBytes)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return Fail(400, ErrorCodes.InvalidRequest, "The body isn't JSON.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                return Fail(400, ErrorCodes.InvalidRequest, "The body must be an object with an items array.");
            }

            var count = items.GetArrayLength();
            if (count == 0) return Fail(400, ErrorCodes.InvalidRequest, "items is empty.");
            if (count > maxBatchItems)
            {
                return Fail(413, ErrorCodes.TooManyItems, $"A push carries at most {maxBatchItems} items.");
            }

            var ids = new List<string>(count);
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("id", out var id)
                    || id.ValueKind != JsonValueKind.String)
                {
                    return Fail(400, ErrorCodes.InvalidRequest, "Every item must be an object with a string id.");
                }
                ids.Add(id.GetString()!);
            }
            if (ids.Distinct(StringComparer.Ordinal).Count() != count)
            {
                return Fail(400, ErrorCodes.DuplicateId, "An id appears more than once.");
            }

            return new PushParse.Items(items.EnumerateArray().Zip(ids, (item, id) => ParseItem(item, id, maxRecordBytes)).ToList());
        }
    }

    private static PushItem ParseItem(JsonElement item, string id, int maxRecordBytes)
    {
        if (!Ids.IsClientUuid(id)) return Invalid(id, "id must be a version 4 UUID in lowercase.");

        if (!item.TryGetProperty("deleted", out var deleted) || deleted.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return Invalid(id, "deleted must be true or false.");
        }
        if (!item.TryGetProperty("baseVersion", out var baseElement)
            || baseElement.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
        {
            return Invalid(id, "baseVersion must be a version, or null to create the record.");
        }
        var baseVersion = baseElement.GetString();
        if (baseVersion is { Length: 0 or > MaxVersionLength })
        {
            return Invalid(id, $"baseVersion must be 1 to {MaxVersionLength} characters.");
        }

        if (deleted.GetBoolean())
        {
            return baseVersion is null
                ? Invalid(id, "A delete names the version it tombstones in baseVersion.")
                : new PushItem.Valid(new RecordWrite(id, baseVersion, null));
        }

        if (!TryGetBytes(item, "ciphertext", out var ciphertext) || ciphertext.Length < TagBytes)
        {
            return Invalid(id, "ciphertext must be base64 of at least the 16-byte tag.");
        }
        if (!TryGetBytes(item, "iv", out var iv) || iv.Length != IvBytes)
        {
            return Invalid(id, "iv must be base64 of 12 bytes.");
        }
        if (!TryGetBytes(item, "wrappedDek", out var wrappedDek) || wrappedDek.Length != WrappedDekBytes)
        {
            return Invalid(id, "wrappedDek must be base64 of 60 bytes.");
        }
        if (ciphertext.Length > maxRecordBytes)
        {
            return new PushItem.Rejected(id, 413, ErrorCodes.RecordTooLarge, $"ciphertext is over maxRecordBytes ({maxRecordBytes}).");
        }

        return new PushItem.Valid(new RecordWrite(id, baseVersion, new SealedContent(ciphertext, iv, wrappedDek)));
    }

    private static bool TryGetBytes(JsonElement item, string name, out byte[] bytes)
    {
        bytes = [];
        return item.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            && Base64Bytes.TryDecode(value.GetString()!, out bytes);
    }

    private static PushParse Fail(int status, string code, string message) => new PushParse.Failed(status, code, message);

    private static PushItem Invalid(string id, string message) =>
        new PushItem.Rejected(id, 400, ErrorCodes.InvalidRecord, message);
}
