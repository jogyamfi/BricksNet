using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using BricksNet.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BricksNet.Auth;

/// <summary>
/// <see cref="IAuthProvider"/> implementing the OAuth 2.0 Authorization Code flow with PKCE
/// (RFC 7636). On first use it opens the system browser, captures the authorization code on a
/// loopback redirect listener, and exchanges it for an access token at the token endpoint.
/// </summary>
/// <remarks>
/// Security properties (see the security-auth skill):
/// <list type="bullet">
///   <item>The PKCE <c>code_verifier</c> uses 96 bytes of cryptographic randomness; the
///   <c>code_challenge</c> is <c>Base64Url(SHA256(code_verifier))</c>.</item>
///   <item>A random <c>state</c> is generated per request and compared in constant time on the
///   callback to defend against CSRF.</item>
///   <item>The redirect listener binds only to <c>127.0.0.1</c> on an ephemeral port and is closed
///   immediately after a single callback.</item>
///   <item>Secrets (verifier, code, tokens) are never written to logs; verifier bytes are zeroed
///   after use.</item>
/// </list>
/// </remarks>
internal sealed class OAuthPkceAuthProvider : IAuthProvider, ISupportsTokenExpiry
{
    private readonly OAuthOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ITokenCache? _tokenCache;
    private readonly ILogger<OAuthPkceAuthProvider> _logger;
    private readonly Action<Uri> _openBrowser;
    private readonly string _cacheKey;

    // The refresh token is held in memory only; it lets an expired access token be renewed
    // silently instead of re-running the interactive browser flow.
    private string? _refreshToken;

    /// <summary>Creates the provider.</summary>
    /// <param name="options">OAuth endpoints, client id, scopes, and redirect configuration.</param>
    /// <param name="httpClient">
    /// HTTP client used solely for the token-endpoint exchange. This must <b>not</b> be the
    /// auth-injecting BricksNet pipeline client, to avoid a circular token dependency.
    /// </param>
    /// <param name="tokenCache">Optional cache for acquired tokens.</param>
    /// <param name="logger">Optional logger. Never receives secret values.</param>
    public OAuthPkceAuthProvider(
        OAuthOptions options,
        HttpClient httpClient,
        ITokenCache? tokenCache = null,
        ILogger<OAuthPkceAuthProvider>? logger = null)
        : this(options, httpClient, tokenCache, logger, openBrowser: null)
    {
    }

    // Internal constructor exposes a browser-launch seam for testing without a real browser.
    internal OAuthPkceAuthProvider(
        OAuthOptions options,
        HttpClient httpClient,
        ITokenCache? tokenCache,
        ILogger<OAuthPkceAuthProvider>? logger,
        Action<Uri>? openBrowser)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentException.ThrowIfNullOrEmpty(options.ClientId);

        if (!options.AuthorizationEndpoint.IsAbsoluteUri ||
            !string.Equals(options.AuthorizationEndpoint.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            throw new ArgumentException("AuthorizationEndpoint must be an absolute HTTPS URI.", nameof(options));
        }

        if (!options.TokenEndpoint.IsAbsoluteUri ||
            !string.Equals(options.TokenEndpoint.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            throw new ArgumentException("TokenEndpoint must be an absolute HTTPS URI.", nameof(options));
        }

        if (options.RedirectPort <= 0 || options.RedirectPortScanLimit < options.RedirectPort)
        {
            throw new ArgumentException("Invalid redirect port range.", nameof(options));
        }

        if (!IsLoopbackHost(options.RedirectHost))
        {
            throw new ArgumentException("RedirectHost must be 'localhost' or a loopback address.", nameof(options));
        }

        _tokenCache = tokenCache;
        _logger = logger ?? NullLogger<OAuthPkceAuthProvider>.Instance;
        _openBrowser = openBrowser ?? LaunchSystemBrowser;
        _cacheKey = $"{options.ClientId}@{options.TokenEndpoint.Host}";
    }

    /// <inheritdoc />
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        AccessToken token = await GetTokenWithExpiryAsync(cancellationToken).ConfigureAwait(false);
        return token.Value;
    }

    /// <inheritdoc />
    async Task<AccessToken> ISupportsTokenExpiry.GetTokenWithExpiryAsync(CancellationToken cancellationToken)
        => await GetTokenWithExpiryAsync(cancellationToken).ConfigureAwait(false);

    private async Task<AccessToken> GetTokenWithExpiryAsync(CancellationToken cancellationToken)
    {
        if (_tokenCache is not null)
        {
            string? cached = await _tokenCache.TryGetTokenAsync(_cacheKey, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                _logger.LogDebug("Using cached OAuth token.");
                // The cache only retains valid (unexpired) tokens; expiry is re-tracked by the caller.
                return new AccessToken(cached, DateTimeOffset.UtcNow + CachedTokenProvider.RefreshBuffer);
            }
        }

        if (_refreshToken is { } refreshToken)
        {
            AccessToken? refreshed = await TryRefreshAsync(refreshToken, cancellationToken).ConfigureAwait(false);
            if (refreshed is { } token)
            {
                return token;
            }
        }

        return await RunAuthorizationCodeFlowAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<AccessToken?> TryRefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = _options.ClientId,
        };

        try
        {
            _logger.LogDebug("Refreshing OAuth access token with the refresh token.");
            return await RequestTokenAsync(form, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Revoked or expired refresh token: discard it and fall back to interactive login.
            _logger.LogInformation("OAuth token refresh failed; falling back to interactive authorization.");
            _refreshToken = null;
            return null;
        }
    }

    private static bool IsLoopbackHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host, out IPAddress? address) && IPAddress.IsLoopback(address));

    private async Task<AccessToken> RunAuthorizationCodeFlowAsync(CancellationToken cancellationToken)
    {
        // --- PKCE parameters (RFC 7636) ---
        byte[] verifierBytes = RandomNumberGenerator.GetBytes(96);
        string codeVerifier;
        string codeChallenge;
        try
        {
            codeVerifier = Base64Url(verifierBytes);
            codeChallenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(verifierBytes);
        }

        // --- CSRF state ---
        string state = RandomNumberGenerator.GetHexString(32);

        using var listener = new HttpListener();
        int port = StartLoopbackListener(
            listener, _options.RedirectHost, _options.RedirectPort, _options.RedirectPortScanLimit);
        var redirectUri = new Uri($"http://{_options.RedirectHost}:{port}/");

        try
        {
            Uri authorizationUri = BuildAuthorizationUri(redirectUri, state, codeChallenge);
            _logger.LogInformation("Opening browser for OAuth authorization on loopback port {Port}.", port);
            _openBrowser(authorizationUri);

            (string code, string returnedState) = await AwaitCallbackAsync(listener, cancellationToken)
                .ConfigureAwait(false);

            // Constant-time comparison defends against CSRF.
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(returnedState),
                    Encoding.ASCII.GetBytes(state)))
            {
                throw new InvalidOperationException("OAuth state mismatch (possible CSRF).");
            }

            return await ExchangeCodeForTokenAsync(code, codeVerifier, redirectUri, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (listener.IsListening)
            {
                listener.Stop();
            }
        }
    }

    private static int StartLoopbackListener(HttpListener listener, string host, int startPort, int endPort)
    {
        for (int port = startPort; port <= endPort; port++)
        {
            listener.Prefixes.Clear();
            listener.Prefixes.Add($"http://{host}:{port}/");
            try
            {
                listener.Start();
                return port;
            }
            catch (HttpListenerException)
            {
                // Port busy or otherwise unavailable — try the next one.
            }
        }

        throw new InvalidOperationException("No free loopback port available for the OAuth redirect listener.");
    }

    private Uri BuildAuthorizationUri(Uri redirectUri, string state, string codeChallenge)
    {
        var query = new StringBuilder();
        AppendQueryParam(query, "response_type", "code");
        AppendQueryParam(query, "client_id", _options.ClientId);
        AppendQueryParam(query, "redirect_uri", redirectUri.AbsoluteUri);
        AppendQueryParam(query, "state", state);
        AppendQueryParam(query, "code_challenge", codeChallenge);
        AppendQueryParam(query, "code_challenge_method", "S256");
        if (_options.Scopes.Count > 0)
        {
            AppendQueryParam(query, "scope", string.Join(' ', _options.Scopes));
        }

        var builder = new UriBuilder(_options.AuthorizationEndpoint) { Query = query.ToString() };
        return builder.Uri;
    }

    private static async Task<(string Code, string State)> AwaitCallbackAsync(
        HttpListener listener,
        CancellationToken cancellationToken)
    {
        // Stopping the listener on cancellation unblocks GetContextAsync.
        using CancellationTokenRegistration registration = cancellationToken.Register(static state =>
        {
            var l = (HttpListener)state!;
            if (l.IsListening)
            {
                l.Stop();
            }
        }, listener);

        HttpListenerContext context;
        try
        {
            context = await listener.GetContextAsync().ConfigureAwait(false);
        }
        catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        IReadOnlyDictionary<string, string> queryParams = ParseQuery(context.Request.Url?.Query ?? string.Empty);

        await WriteCallbackResponseAsync(context.Response).ConfigureAwait(false);

        if (queryParams.TryGetValue("error", out string? error))
        {
            throw new InvalidOperationException($"OAuth authorization failed: {error}.");
        }

        if (!queryParams.TryGetValue("code", out string? code) || string.IsNullOrEmpty(code))
        {
            throw new InvalidOperationException("OAuth callback did not include an authorization code.");
        }

        queryParams.TryGetValue("state", out string? returnedState);
        return (code, returnedState ?? string.Empty);
    }

    private static async Task WriteCallbackResponseAsync(HttpListenerResponse response)
    {
        byte[] body = Encoding.UTF8.GetBytes(
            "<html><body>Authentication complete. You may close this window.</body></html>");
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = body.Length;
        await response.OutputStream.WriteAsync(body).ConfigureAwait(false);
        response.OutputStream.Close();
    }

    private async Task<AccessToken> ExchangeCodeForTokenAsync(
        string code,
        string codeVerifier,
        Uri redirectUri,
        CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri.AbsoluteUri,
            ["client_id"] = _options.ClientId,
            ["code_verifier"] = codeVerifier,
        };

        return await RequestTokenAsync(form, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// POSTs a token request (authorization-code or refresh-token grant), records any rotated
    /// refresh token, and stores the access token in the cache when one is configured.
    /// </summary>
    private async Task<AccessToken> RequestTokenAsync(
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(form),
        };

        using HttpResponseMessage response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // The body may contain an error description but never a usable secret; do not log it.
            throw new InvalidOperationException(
                $"OAuth token exchange failed with status {(int)response.StatusCode}.");
        }

        TokenResponse? token = await response.Content
            .ReadFromJsonAsync<TokenResponse>(cancellationToken)
            .ConfigureAwait(false);

        if (token is null || string.IsNullOrEmpty(token.AccessToken))
        {
            throw new InvalidOperationException("OAuth token endpoint returned no access token.");
        }

        // Keep the previous refresh token when the server does not rotate it.
        if (!string.IsNullOrEmpty(token.RefreshToken))
        {
            _refreshToken = token.RefreshToken;
        }

        DateTimeOffset expiresAt = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(Math.Max(0, token.ExpiresInSeconds));
        _logger.LogInformation("Acquired OAuth access token (expires {ExpiresAt:o}).", expiresAt);

        if (_tokenCache is not null)
        {
            await _tokenCache.StoreTokenAsync(_cacheKey, token.AccessToken, expiresAt, cancellationToken)
                .ConfigureAwait(false);
        }

        return new AccessToken(token.AccessToken, expiresAt);
    }

    private void LaunchSystemBrowser(Uri uri)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning("Could not launch the system browser. Open the authorization URL manually.");
        }
        catch (InvalidOperationException)
        {
            _logger.LogWarning("Could not launch the system browser. Open the authorization URL manually.");
        }
        catch (PlatformNotSupportedException)
        {
            _logger.LogWarning("Launching the system browser is not supported on this platform.");
        }
    }

    private static void AppendQueryParam(StringBuilder builder, string key, string value)
    {
        if (builder.Length > 0)
        {
            builder.Append('&');
        }

        builder.Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
    }

    private static IReadOnlyDictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        string trimmed = query.TrimStart('?');
        foreach (string pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=', StringComparison.Ordinal);
            string key = eq < 0 ? pair : pair[..eq];
            string value = eq < 0 ? string.Empty : pair[(eq + 1)..];
            result[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value);
        }

        return result;
    }

    private static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresInSeconds { get; init; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }
    }
}
