using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace BricksNet.IntegrationTests.FakeService;

/// <summary>
/// Serves responses from previously captured fixtures (<see cref="FakeServiceMode.Replay"/>).
/// Fixtures are matched on HTTP method + path; query-parameter order is ignored and statement
/// identifiers are deterministic within a fixture set, so body matching is unnecessary. A
/// per-key consumption counter is exposed for assertions and diagnostics.
/// </summary>
internal sealed class FakeServiceReplayer : IFakeResponder
{
    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<string, StoredResponse> _responses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _hits = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    private FakeServiceReplayer()
    {
    }

    /// <summary>The number of fixtures loaded.</summary>
    public int FixtureCount => _responses.Count;

    /// <summary>Loads every <c>*.json</c> fixture under <paramref name="directory"/> (recursively).</summary>
    public static FakeServiceReplayer Load(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        var replayer = new FakeServiceReplayer();

        if (!Directory.Exists(directory))
        {
            return replayer;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories))
        {
            var json = File.ReadAllText(file);
            var exchange = JsonSerializer.Deserialize<StoredExchange>(json, ReadOptions)
                ?? throw new InvalidOperationException($"Fixture '{file}' did not deserialize to an exchange.");

            var key = Key(exchange.Request.Method, exchange.Request.Path);
            replayer._responses[key] = exchange.Response;
        }

        return replayer;
    }

    /// <summary>Returns how many times the fixture for <paramref name="method"/> + <paramref name="path"/> was served.</summary>
    public int HitsFor(string method, string path)
    {
        lock (_lock)
        {
            return _hits.TryGetValue(Key(method, path), out var count) ? count : 0;
        }
    }

    public async Task<bool> TryRespondAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Drain the body so the underlying connection is not left half-read.
        if (context.Request.ContentLength is > 0)
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            _ = await reader.ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);
        }

        var key = Key(context.Request.Method, context.Request.Path.Value ?? "/");
        StoredResponse? stored;
        lock (_lock)
        {
            if (!_responses.TryGetValue(key, out stored))
            {
                return false;
            }

            _hits[key] = (_hits.TryGetValue(key, out var existing) ? existing : 0) + 1;
        }

        await WriteAsync(context, stored).ConfigureAwait(false);
        return true;
    }

    private static async Task WriteAsync(HttpContext context, StoredResponse stored)
    {
        context.Response.StatusCode = stored.Status;
        context.Response.ContentType = stored.ContentType;

        if (stored.Headers is not null)
        {
            foreach (var header in stored.Headers)
            {
                context.Response.Headers[header.Key] = header.Value;
            }
        }

        byte[] body;
        if (stored.Base64 is not null)
        {
            body = Convert.FromBase64String(stored.Base64);
        }
        else if (stored.Json is not null)
        {
            body = Encoding.UTF8.GetBytes(stored.Json.ToJsonString());
        }
        else
        {
            body = Array.Empty<byte>();
        }

        await context.Response.Body.WriteAsync(body, context.RequestAborted).ConfigureAwait(false);
    }

    private static string Key(string method, string path) =>
        $"{method.ToUpperInvariant()} {path.TrimEnd('/')}";
}
