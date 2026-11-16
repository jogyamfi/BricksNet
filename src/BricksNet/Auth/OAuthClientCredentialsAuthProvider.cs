using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BricksNet.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BricksNet.Auth;

/// <summary>
/// Gets access tokens for a Databricks service principal with the OAuth 2.0 client-credentials
/// grant (machine-to-machine, M2M). Wrap it in <see cref="CachedTokenProvider"/>, which reuses each
/// token until shortly before it expires.
/// </summary>
/// <remarks>
/// Each request is <c>POST {tokenEndpoint}</c> with HTTP Basic client authentication and the form
/// body <c>grant_type=client_credentials&amp;scope=all-apis</c>, as Databricks documents for
/// workspace-level M2M tokens. The client secret and access tokens are never logged.
/// </remarks>
internal sealed class OAuthClientCredentialsAuthProvider : IAuthProvider, ISupportsTokenExpiry
{
    /// <summary>The scope Databricks requires for workspace-level M2M tokens.</summary>
    internal const string DefaultScope = "all-apis";

    private readonly Uri _tokenEndpoint;
    private readonly AuthenticationHeaderValue _clientAuthentication;
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;

    /// <param name="tokenEndpoint">The workspace token endpoint, <c>https://&lt;host&gt;/oidc/v1/token</c>.</param>
    /// <param name="clientId">The service principal's OAuth client ID (application ID).</param>
    /// <param name="clientSecret">The service principal's OAuth secret.</param>
    /// <param name="httpClient">
    /// Client for the token request. Must not be the auth-injecting BricksNet pipeline client.
    /// </param>
    /// <param name="logger">Optional logger.</param>
    public OAuthClientCredentialsAuthProvider(
        Uri tokenEndpoint,
        string clientId,
        string clientSecret,
        HttpClient httpClient,
        ILogger<OAuthClientCredentialsAuthProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(tokenEndpoint);
        ArgumentException.ThrowIfNullOrEmpty(clientId);
        ArgumentException.ThrowIfNullOrEmpty(clientSecret);
        ArgumentNullException.ThrowIfNull(httpClient);

        if (tokenEndpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("The OAuth token endpoint must use HTTPS.", nameof(tokenEndpoint));
        }

        _tokenEndpoint = tokenEndpoint;
        _httpClient = httpClient;
        _logger = logger ?? (ILogger)NullLogger.Instance;

        // Sent as-is, matching the Databricks documentation (curl --user "$CLIENT_ID:$CLIENT_SECRET").
        _clientAuthentication = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}")));
    }

    /// <summary>Builds the workspace-level token endpoint for <paramref name="host"/>.</summary>
    internal static Uri TokenEndpointFor(string host, int port) =>
        new UriBuilder(Uri.UriSchemeHttps, host, port, "/oidc/v1/token").Uri;

    /// <inheritdoc />
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        AccessToken token = await GetTokenWithExpiryAsync(cancellationToken).ConfigureAwait(false);
        return token.Value;
    }

    /// <inheritdoc />
    public async Task<AccessToken> GetTokenWithExpiryAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _tokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["scope"] = DefaultScope,
            }),
        };
        request.Headers.Authorization = _clientAuthentication;

        using HttpResponseMessage response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            string detail = await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"OAuth client-credentials token request failed with status {(int)response.StatusCode}{detail}. "
                + "Check the client ID and secret, and that the service principal has access to the workspace.");
        }

        TokenResponse? token = await response.Content
            .ReadFromJsonAsync<TokenResponse>(cancellationToken)
            .ConfigureAwait(false);

        if (token is null || string.IsNullOrEmpty(token.AccessToken))
        {
            throw new InvalidOperationException("OAuth token endpoint returned no access token.");
        }

        DateTimeOffset expiresAt = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(Math.Max(0, token.ExpiresInSeconds));
        _logger.LogInformation("Acquired OAuth client-credentials access token (expires {ExpiresAt:o}).", expiresAt);

        return new AccessToken(token.AccessToken, expiresAt);
    }

    /// <summary>
    /// Reads only the standard OAuth <c>error</c> and <c>error_description</c> fields (RFC 6749
    /// section 5.2). They describe the failure and hold no credentials; anything else is ignored.
    /// </summary>
    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            ErrorResponse? error = await response.Content
                .ReadFromJsonAsync<ErrorResponse>(cancellationToken)
                .ConfigureAwait(false);
            if (error?.Error is { Length: > 0 } code)
            {
                return error.Description is { Length: > 0 } description
                    ? $" ({code}: {description})"
                    : $" ({code})";
            }
        }
        catch (JsonException)
        {
        }
        catch (NotSupportedException)
        {
            // Not a JSON body.
        }

        return string.Empty;
    }

    private sealed record TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresInSeconds { get; init; }
    }

    private sealed record ErrorResponse
    {
        [JsonPropertyName("error")]
        public string? Error { get; init; }

        [JsonPropertyName("error_description")]
        public string? Description { get; init; }
    }
}
