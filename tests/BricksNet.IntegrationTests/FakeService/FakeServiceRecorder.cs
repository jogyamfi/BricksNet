using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;

namespace BricksNet.IntegrationTests.FakeService;

/// <summary>
/// Forwards each request to a real Databricks workspace and, in <see cref="FakeServiceMode.Record"/>
/// mode, persists the request/response pair as a fixture. Requires live credentials and is only
/// used to (re)capture fixtures — it never runs in the offline CI suite.
/// </summary>
internal sealed class FakeServiceRecorder : IFakeResponder
{
    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly HttpClient _upstream;
    private readonly Uri _targetBaseAddress;
    private readonly string? _fixtureDirectory;
    private int _step;

    /// <param name="upstream">Client used to forward requests to the live workspace.</param>
    /// <param name="targetBaseAddress">Base address of the live workspace.</param>
    /// <param name="fixtureDirectory">
    /// Directory to persist fixtures into; <see langword="null"/> for pure passthrough.
    /// </param>
    public FakeServiceRecorder(HttpClient upstream, Uri targetBaseAddress, string? fixtureDirectory)
    {
        _upstream = upstream ?? throw new ArgumentNullException(nameof(upstream));
        _targetBaseAddress = targetBaseAddress ?? throw new ArgumentNullException(nameof(targetBaseAddress));
        _fixtureDirectory = fixtureDirectory;

        if (_fixtureDirectory is not null)
        {
            Directory.CreateDirectory(_fixtureDirectory);
        }
    }

    public async Task<bool> TryRespondAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var ct = context.RequestAborted;

        var path = context.Request.Path.Value ?? "/";
        var query = context.Request.QueryString.Value ?? string.Empty;
        var target = new Uri(_targetBaseAddress, path + query);

        using var forward = new HttpRequestMessage(new HttpMethod(context.Request.Method), target);

        string? requestBody = null;
        if (context.Request.ContentLength is > 0)
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            requestBody = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            forward.Content = new StringContent(requestBody, Encoding.UTF8);
            if (context.Request.ContentType is { } contentType)
            {
                forward.Content.Headers.ContentType =
                    System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
            }
        }

        foreach (var header in context.Request.Headers)
        {
            if (!header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
            {
                forward.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        using var response = await _upstream.SendAsync(forward, ct).ConfigureAwait(false);
        var payload = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);

        if (_fixtureDirectory is not null)
        {
            await PersistAsync(context, path, requestBody, response, payload, ct).ConfigureAwait(false);
        }

        context.Response.StatusCode = (int)response.StatusCode;
        context.Response.ContentType =
            response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
        await context.Response.Body.WriteAsync(payload, ct).ConfigureAwait(false);
        return true;
    }

    private async Task PersistAsync(
        HttpContext context,
        string path,
        string? requestBody,
        HttpResponseMessage response,
        byte[] payload,
        CancellationToken ct)
    {
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
        var isJson = contentType.Contains("json", StringComparison.OrdinalIgnoreCase);

        var stored = new StoredExchange
        {
            Request = new StoredRequest
            {
                Method = context.Request.Method,
                Path = path,
                Body = requestBody,
            },
            Response = new StoredResponse
            {
                Status = (int)response.StatusCode,
                ContentType = contentType,
                Json = isJson && payload.Length > 0
                    ? JsonNode.Parse(Encoding.UTF8.GetString(payload))
                    : null,
                Base64 = isJson ? null : Convert.ToBase64String(payload),
            },
        };

        var file = Path.Combine(_fixtureDirectory!, $"step-{_step++}.json");
        await File.WriteAllTextAsync(
            file, JsonSerializer.Serialize(stored, WriteOptions), ct).ConfigureAwait(false);
    }
}
