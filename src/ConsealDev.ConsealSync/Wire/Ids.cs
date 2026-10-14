using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace ConsealDev.ConsealSync.Wire;

/// <summary>
/// The id formats the contract fixes, so every route checks them the same way. Each pattern ends in <c>\z</c>, since
/// .NET's <c>$</c> also matches before a trailing newline.
/// </summary>
internal static partial class Ids
{
    /// <summary>A collection id the app assigns: never a name.</summary>
    public static bool IsCollectionId(string? value) => value is not null && CollectionId().IsMatch(value);

    /// <summary>A record, blob or device id: a random version 4 UUID in lowercase, minted by the client.</summary>
    public static bool IsClientUuid(string? value) => value is not null && ClientUuid().IsMatch(value);

    /// <summary>A join request's id: 128 random bits in lowercase hex, minted by the server.</summary>
    public static bool IsChallengeId(string? value) => value is not null && ChallengeId().IsMatch(value);

    /// <summary>128 random bits in lowercase hex, for versions and join request ids.</summary>
    public static string NewRandomId() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,64}\z")]
    private static partial Regex CollectionId();

    [GeneratedRegex(@"^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\z")]
    private static partial Regex ClientUuid();

    [GeneratedRegex(@"^[0-9a-f]{32}\z")]
    private static partial Regex ChallengeId();
}
