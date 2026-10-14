using ConsealDev.ConsealSync.Wire;
using Microsoft.Extensions.Options;

namespace ConsealDev.ConsealSync.Configuration;

/// <summary>Refuses to start with options the routes can't run on, so a typo is a startup error.</summary>
internal sealed class ConsealSyncOptionsValidator : IValidateOptions<ConsealSyncOptions>
{
    private const int MinCursorKeyBytes = 32;
    private const int MinRequestBytes = 1024 * 1024;

    public ValidateOptionsResult Validate(string? name, ConsealSyncOptions options)
    {
        var errors = new List<string>();

        if (options.Collections.Count == 0)
        {
            errors.Add("ConsealSync has no collections. Add their ids with WithCollections or ConsealSync:Collections.");
        }
        foreach (var id in options.Collections.Where(id => !Ids.IsCollectionId(id)))
        {
            errors.Add($"ConsealSync collection id '{id}' doesn't match ^[A-Za-z0-9_-]{{1,64}}$.");
        }
        foreach (var id in options.Collections.GroupBy(id => id).Where(group => group.Count() > 1).Select(group => group.Key))
        {
            errors.Add($"ConsealSync collection id '{id}' is listed more than once.");
        }
        if (options.Collections.Contains(ConsealSyncOptions.KeyringCollection))
        {
            errors.Add(
                $"ConsealSync collection id '{ConsealSyncOptions.KeyringCollection}' is reserved for the user's identity, " +
                "and the routes accept it without being configured with it.");
        }

        if (!TryDecodeKey(options.CursorSigningKey, out var key) || key.Length < MinCursorKeyBytes)
        {
            errors.Add(
                "ConsealSync:CursorSigningKey must be base64 of at least 32 random bytes. " +
                "Generate one with: openssl rand -base64 32");
        }

        if (string.IsNullOrWhiteSpace(options.UserIdClaim))
        {
            errors.Add("ConsealSync:UserIdClaim must name the claim that carries the user's id.");
        }
        if (options.MaxBatchItems < 1)
        {
            errors.Add("ConsealSync:MaxBatchItems must be at least 1.");
        }
        if (options.MaxPageItems < 1)
        {
            errors.Add("ConsealSync:MaxPageItems must be at least 1.");
        }
        if (options.MaxRequestBytes < MinRequestBytes)
        {
            errors.Add("ConsealSync:MaxRequestBytes must be at least 1 MB, room for one record of the largest size.");
        }

        return errors.Count > 0 ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
    }

    private static bool TryDecodeKey(string value, out byte[] key)
    {
        key = new byte[value.Length];
        if (!Convert.TryFromBase64String(value, key, out var written))
        {
            key = [];
            return false;
        }
        key = key[..written];
        return true;
    }
}
