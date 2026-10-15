namespace ConsealDev.Conseal.Storage;

/// <summary>A record's sealed content, decoded from base64: opaque bytes the server stores and never reads.</summary>
/// <param name="Ciphertext">The sealed plaintext, the AES-GCM tag appended.</param>
/// <param name="Iv">The 12-byte IV of <paramref name="Ciphertext"/>.</param>
/// <param name="WrappedDek">The record's DEK sealed under the user's AEK, 60 bytes.</param>
public sealed record SealedContent(byte[] Ciphertext, byte[] Iv, byte[] WrappedDek);
