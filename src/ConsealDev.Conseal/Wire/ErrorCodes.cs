namespace ConsealDev.Conseal.Wire;

/// <summary>
/// The error codes the contract names, in <c>openapi.yaml</c> and <c>circle.yaml</c> of conseal's spec. They are
/// constants because clients act on them, so a typo here would be a code no client knows.
/// </summary>
internal static class ErrorCodes
{
    public const string InvalidRequest = "invalid_request";
    public const string Unauthenticated = "unauthenticated";

    public const string InvalidCursor = "invalid_cursor";
    public const string DuplicateId = "duplicate_id";
    public const string UnknownCollection = "unknown_collection";
    public const string TooManyItems = "too_many_items";
    public const string RequestTooLarge = "request_too_large";
    public const string InvalidRecord = "invalid_record";
    public const string RecordTooLarge = "record_too_large";
    public const string BlobsDisabled = "blobs_disabled";
    public const string BlobTooLarge = "blob_too_large";

    public const string NoCircle = "no_circle";
    public const string CircleExists = "circle_exists";
    public const string UnknownDevice = "unknown_device";
    public const string DeviceRegistered = "device_registered";
    public const string DeviceRevoked = "device_revoked";
    public const string LastDevice = "last_device";
    public const string UnknownJoin = "unknown_join";
    public const string JoinNotPending = "join_not_pending";
    public const string JoinNotAuthorized = "join_not_authorized";
    public const string JoinMismatch = "join_mismatch";
    public const string JoinExpired = "join_expired";
}
