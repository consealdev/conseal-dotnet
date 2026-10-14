namespace ConsealDev.ConsealSync.Configuration;

/// <summary>
/// Settings for the sync and circle routes, bound from the <c>ConsealSync</c> configuration section. Collection ids
/// can also come from code, through <see cref="Extensions.ConsealSyncBuilder.WithCollections"/>.
/// </summary>
public sealed class ConsealSyncOptions
{
    /// <summary>The configuration section the options bind from.</summary>
    public const string SectionName = "ConsealSync";

    /// <summary>
    /// The collection ids the server accepts; any other answers <c>404 unknown_collection</c>. Each is an identifier
    /// the app assigns, such as <c>7f3a9c21</c>, never the collection's name, matching <c>^[A-Za-z0-9_-]{1,64}$</c>.
    /// </summary>
    public List<string> Collections { get; set; } = [];

    /// <summary>
    /// The key that signs pull cursors, in base64, at least 32 bytes (<c>openssl rand -base64 32</c>). Changing it
    /// invalidates every cursor issued so far, and clients pull again from the start.
    /// </summary>
    public string CursorSigningKey { get; set; } = string.Empty;

    /// <summary>
    /// The claim that carries the signed-in user's id. With the default <c>sub</c>, the
    /// <see cref="System.Security.Claims.ClaimTypes.NameIdentifier"/> claim is read when <c>sub</c> is absent, since
    /// ASP.NET's JWT handler renames <c>sub</c> to it unless inbound claim mapping is off.
    /// </summary>
    public string UserIdClaim { get; set; } = "sub";

    /// <summary>The most items one push may carry. More answers <c>413 too_many_items</c>.</summary>
    public int MaxBatchItems { get; set; } = 100;

    /// <summary>The most items one pull returns, whatever <c>limit</c> the client asks for.</summary>
    public int MaxPageItems { get; set; } = 1000;

    /// <summary>
    /// The largest push body, in bytes. Larger answers <c>413 request_too_large</c>, and the client sends fewer
    /// items. The default stays under AWS Lambda's 6 MB payload limit; it must leave room for one record of the
    /// largest size the store accepts, which takes about 270 KB as JSON with DynamoDB.
    /// </summary>
    public int MaxRequestBytes { get; set; } = 5 * 1024 * 1024;
}
