using Microsoft.Extensions.Logging;

namespace BricksNet.Connection;

/// <summary>
/// Thin, DI-friendly wrapper over an <see cref="System.Net.Http.HttpClient"/> obtained from
/// <see cref="IHttpClientFactory"/>. The underlying socket lifetime is owned by the factory; this
/// type holds no socket state of its own.
/// </summary>
/// <remarks>
/// Cross-cutting concerns (proxy, auth-header injection, retry, TLS) are applied by the handler
/// pipeline configured in <see cref="HttpClientBuilderExtensions"/>, not here.
/// </remarks>
public sealed class BricksNetHttpClient
{
    /// <summary>Logical name of the typed/named <see cref="System.Net.Http.HttpClient"/> registration.</summary>
    public const string HttpClientName = "BricksNet";

    private readonly HttpClient _httpClient;
    private readonly ILogger<BricksNetHttpClient> _logger;

    /// <summary>
    /// Initialises the wrapper. The <paramref name="httpClient"/> is expected to be configured with
    /// the base address, default headers, and timeout via <see cref="HttpClientBuilderExtensions"/>.
    /// </summary>
    public BricksNetHttpClient(HttpClient httpClient, ILogger<BricksNetHttpClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Base address applied to relative request URIs.</summary>
    public Uri? BaseAddress => _httpClient.BaseAddress;

    /// <summary>
    /// Sends an HTTP request through the configured pipeline.
    /// </summary>
    /// <param name="request">The request to send.</param>
    /// <param name="completionOption">Controls when the returned task completes.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The HTTP response.</returns>
    public async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseHeadersRead,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogDebug("Sending {Method} {Uri}.", request.Method, request.RequestUri);
        return await _httpClient.SendAsync(request, completionOption, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Issues a POST with the supplied content to a relative or absolute URI.</summary>
    public Task<HttpResponseMessage> PostAsync(
        string requestUri,
        HttpContent content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var request = new HttpRequestMessage(HttpMethod.Post, requestUri) { Content = content };
        return SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    /// <summary>Issues a GET to a relative or absolute URI.</summary>
    public Task<HttpResponseMessage> GetAsync(
        string requestUri,
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        return SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    /// <summary>Issues a DELETE to a relative or absolute URI.</summary>
    public Task<HttpResponseMessage> DeleteAsync(
        string requestUri,
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, requestUri);
        return SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}
