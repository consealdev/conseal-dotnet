using System.Security.Cryptography;

namespace ConsealDev.ConsealSync.Tests.TestSupport;

/// <summary>Push items as a client sends them. The server never opens them, so random bytes stand in for ciphertext.</summary>
internal static class Items
{
    public static string NewId() => Guid.NewGuid().ToString();

    public static object Push(params object[] items) => new { items };

    public static object Write(string id, string? baseVersion, int ciphertextBytes = 48) => new
    {
        id,
        baseVersion,
        deleted = false,
        ciphertext = RandomBase64(ciphertextBytes),
        iv = RandomBase64(12),
        wrappedDek = RandomBase64(60),
    };

    public static object Delete(string id, string? baseVersion) => new { id, baseVersion, deleted = true };

    public static string RandomBase64(int bytes) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));
}
