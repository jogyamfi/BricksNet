using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace BricksNet.IntegrationTests.FakeService;

/// <summary>
/// A single recorded request/response pair, persisted as
/// <c>fixtures/{service_type}/{test_name}/step-{n}.json</c>.
/// </summary>
internal sealed class StoredExchange
{
    public StoredRequest Request { get; set; } = new();

    public StoredResponse Response { get; set; } = new();
}

/// <summary>The salient parts of a recorded request used to match a replayed call.</summary>
internal sealed class StoredRequest
{
    /// <summary>HTTP method (for example <c>GET</c> or <c>POST</c>).</summary>
    public string Method { get; set; } = "GET";

    /// <summary>Absolute request path without the query string.</summary>
    public string Path { get; set; } = "/";

    /// <summary>
    /// Optional normalized request body. Captured for diagnostics; matching is keyed on
    /// <see cref="Method"/> + <see cref="Path"/> because statement identifiers are deterministic
    /// within a fixture set.
    /// </summary>
    public string? Body { get; set; }
}

/// <summary>The stored response returned for a matched request.</summary>
internal sealed class StoredResponse
{
    /// <summary>HTTP status code.</summary>
    public int Status { get; set; } = 200;

    /// <summary>Response <c>Content-Type</c>.</summary>
    public string ContentType { get; set; } = "application/json";

    /// <summary>A JSON response body. Mutually exclusive with <see cref="Base64"/>.</summary>
    [JsonPropertyName("json")]
    public JsonNode? Json { get; set; }

    /// <summary>A base64-encoded binary response body (for example an Arrow IPC stream).</summary>
    [JsonPropertyName("base64")]
    public string? Base64 { get; set; }

    /// <summary>Optional extra response headers.</summary>
    public Dictionary<string, string>? Headers { get; set; }
}
