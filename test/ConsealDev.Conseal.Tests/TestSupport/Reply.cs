using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace ConsealDev.Conseal.Tests.TestSupport;

/// <summary>A route's answer: the status, the JSON body if there is one, and the headers.</summary>
internal sealed record Reply(HttpStatusCode Status, JsonNode? Body, HttpResponseHeaders Headers)
{
    /// <summary>The error code of an error body.</summary>
    public string? Code => Body?["code"]?.GetValue<string>();
}
