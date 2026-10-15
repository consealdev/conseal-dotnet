using Microsoft.Extensions.Options;

namespace ConsealDev.Conseal.Configuration;

/// <summary>Refuses to start with blob options the routes can't run on, so a typo is a startup error.</summary>
internal sealed class ConsealSyncBlobOptionsValidator(IOptions<ConsealSyncOptions> syncOptions) : IValidateOptions<ConsealSyncBlobOptions>
{
    /// <summary>The smallest sealed blob: a 12-byte IV and a 16-byte tag around no content.</summary>
    public const long MinSealedBytes = 28;

    /// <summary>The most one S3 <c>PUT</c> takes.</summary>
    private const long MaxPutBytes = 5L * 1024 * 1024 * 1024;

    /// <summary>The longest a Signature Version 4 presigned URL can work.</summary>
    private static readonly TimeSpan MaxUrlLifetime = TimeSpan.FromDays(7);

    public ValidateOptionsResult Validate(string? name, ConsealSyncBlobOptions options)
    {
        var errors = new List<string>();
        var collections = syncOptions.Value.Collections;

        if (options.Collections.Count == 0)
        {
            errors.Add("ConsealSync:Blobs has no collections. Add the ids of the collections that have blobs to its Collections.");
        }
        foreach (var id in options.Collections.Where(id => !collections.Contains(id)))
        {
            errors.Add($"ConsealSync:Blobs collection '{id}' isn't one of ConsealSync's collections.");
        }
        foreach (var id in options.Collections.GroupBy(id => id).Where(group => group.Count() > 1).Select(group => group.Key))
        {
            errors.Add($"ConsealSync:Blobs collection '{id}' is listed more than once.");
        }

        if (string.IsNullOrWhiteSpace(options.BucketName))
        {
            errors.Add("ConsealSync:Blobs:BucketName must name the S3 bucket that holds the blobs.");
        }
        if (options.MaxBlobBytes is < MinSealedBytes or > MaxPutBytes)
        {
            errors.Add($"ConsealSync:Blobs:MaxBlobBytes must be from {MinSealedBytes} bytes to 5 GiB.");
        }
        if (options.UrlLifetime <= TimeSpan.Zero || options.UrlLifetime > MaxUrlLifetime)
        {
            errors.Add("ConsealSync:Blobs:UrlLifetime must be more than zero and at most 7 days.");
        }

        return errors.Count > 0 ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
    }
}
