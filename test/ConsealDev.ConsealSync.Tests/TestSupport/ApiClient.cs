using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ConsealDev.ConsealSync.Tests.TestSupport;

/// <summary>Sends JSON to the routes and reads their answers.</summary>
internal sealed class ApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<Reply> GetAsync(string path) => SendAsync(HttpMethod.Get, path, body: null);

    public Task<Reply> PostAsync(string path, object? body = null) => SendAsync(HttpMethod.Post, path, body);

    public Task<Reply> PutAsync(string path, object body) => SendAsync(HttpMethod.Put, path, body);

    public Task<Reply> DeleteAsync(string path) => SendAsync(HttpMethod.Delete, path, body: null);

    public Task<Reply> PostRawAsync(string path, string body) => SendRawAsync(HttpMethod.Post, path, body);

    private Task<Reply> SendAsync(HttpMethod method, string path, object? body) =>
        SendRawAsync(method, path, body is null ? null : JsonSerializer.Serialize(body, Json));

    private async Task<Reply> SendRawAsync(HttpMethod method, string path, string? body)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return new Reply(response.StatusCode, text.Length == 0 ? null : JsonNode.Parse(text), response.Headers);
    }
}
