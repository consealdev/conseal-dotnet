namespace ConsealDev.ConsealSync.Wire;

/// <summary>Standard base64 with padding, as the contract carries binary values.</summary>
internal static class Base64Bytes
{
    public static bool TryDecode(string value, out byte[] bytes)
    {
        var buffer = new byte[value.Length / 4 * 3 + 3];
        if (value.Length == 0 || !Convert.TryFromBase64String(value, buffer, out var written))
        {
            bytes = [];
            return false;
        }
        bytes = buffer[..written];
        return true;
    }
}
