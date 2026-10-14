using Microsoft.AspNetCore.Http;

namespace ConsealDev.ConsealSync.Endpoints;

/// <summary>Reads a request body up to a size, so a large one is refused before it is all in memory.</summary>
internal static class RequestBody
{
    private const int ChunkBytes = 81920;

    /// <summary>The body's bytes; null when it is longer than <paramref name="maxBytes"/>.</summary>
    public static async Task<byte[]?> ReadAsync(HttpRequest request, int maxBytes, CancellationToken cancellationToken)
    {
        if (request.ContentLength > maxBytes) return null;

        using var body = new MemoryStream();
        var chunk = new byte[ChunkBytes];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (body.Length + read > maxBytes) return null;
            body.Write(chunk, 0, read);
        }
        return body.ToArray();
    }
}
