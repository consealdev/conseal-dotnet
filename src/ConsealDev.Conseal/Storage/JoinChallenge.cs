namespace ConsealDev.Conseal.Storage;

/// <summary>A join request, as the server holds it while the ceremony runs.</summary>
/// <param name="ChallengeId">The request's id, 32 lowercase hex characters.</param>
/// <param name="DeviceId">The joining device's id, from the request.</param>
/// <param name="Request">The join request, as the JSON object the device sent.</param>
/// <param name="State">Where the request stands.</param>
/// <param name="ReceivedAt">When the server received the request, by its own clock.</param>
/// <param name="ExpiresAt">When the request's time runs out, by the server's clock.</param>
/// <param name="SealedAek">The AEK sealed to the request's key, as a JSON object; set once authorized.</param>
/// <param name="PassphraseCheck">The approver's sealed passphrase check, as a JSON object; set once authorized.</param>
public sealed record JoinChallenge(
    string ChallengeId,
    string DeviceId,
    string Request,
    JoinState State,
    DateTimeOffset ReceivedAt,
    DateTimeOffset ExpiresAt,
    string? SealedAek,
    string? PassphraseCheck);
