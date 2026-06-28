using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BricksNet.Auth;
using RichardSzalay.MockHttp;
using Xunit;

namespace BricksNet.Auth;

public class OAuthPkceAuthProviderTests
{
    private const string TokenEndpoint = "https://auth.example.com/token";

    private static OAuthOptions CreateOptions() => new()
    {
        ClientId = "client-1",
        AuthorizationEndpoint = new Uri("https://auth.example.com/authorize"),
        TokenEndpoint = new Uri(TokenEndpoint),
        Scopes = new[] { "sql", "offline_access" },
        RedirectPort = 8200,
        RedirectPortScanLimit = 8260,
    };

    [Fact]
    public async Task GetTokenAsync_CompletesPkceFlow_AndReturnsAccessToken()
    {
        // Arrange
        const string tokenJson =
            "{\"access_token\":\"access-123\",\"token_type\":\"Bearer\",\"expires_in\":3600}";
        string? capturedBody = null;
        var mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Post, TokenEndpoint)
            .Respond(async req =>
            {
                capturedBody = await req.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(tokenJson, Encoding.UTF8, "application/json"),
                };
            });
        using HttpClient httpClient = mockHttp.ToHttpClient();

        string? capturedChallenge = null;
        string? capturedMethod = null;
        Task? callbackTask = null;
        void OpenBrowser(Uri authUri)
        {
            IReadOnlyDictionary<string, string> query = ParseQuery(authUri.Query);
            capturedChallenge = query["code_challenge"];
            capturedMethod = query["code_challenge_method"];
            callbackTask = SendCallbackAsync(query["redirect_uri"], "auth-code", query["state"]);
        }

        var provider = new OAuthPkceAuthProvider(
            CreateOptions(), httpClient, tokenCache: null, logger: null, openBrowser: OpenBrowser);

        // Act
        string token = await provider.GetTokenAsync();
        if (callbackTask is not null)
        {
            await callbackTask;
        }

        // Assert
        Assert.Equal("access-123", token);
        Assert.Equal("S256", capturedMethod);

        IReadOnlyDictionary<string, string> form = ParseQuery(capturedBody!);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("auth-code", form["code"]);
        Assert.Equal("client-1", form["client_id"]);

        // The code_challenge must be Base64Url(SHA256(code_verifier)) of the exchanged verifier.
        string expectedChallenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"])));
        Assert.Equal(expectedChallenge, capturedChallenge);
    }

    [Fact]
    public async Task GetTokenAsync_Throws_OnStateMismatch()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        using HttpClient httpClient = mockHttp.ToHttpClient();

        Task? callbackTask = null;
        void OpenBrowser(Uri authUri)
        {
            IReadOnlyDictionary<string, string> query = ParseQuery(authUri.Query);
            callbackTask = SendCallbackAsync(query["redirect_uri"], "auth-code", "tampered-state");
        }

        var provider = new OAuthPkceAuthProvider(
            CreateOptions(), httpClient, tokenCache: null, logger: null, openBrowser: OpenBrowser);

        // Act / Assert
        InvalidOperationException ex =
            await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetTokenAsync());
        Assert.Contains("state mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);

        if (callbackTask is not null)
        {
            await callbackTask;
        }
    }

    [Fact]
    public async Task GetTokenAsync_Throws_WhenCallbackReportsError()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        using HttpClient httpClient = mockHttp.ToHttpClient();

        Task? callbackTask = null;
        void OpenBrowser(Uri authUri)
        {
            IReadOnlyDictionary<string, string> query = ParseQuery(authUri.Query);
            string callbackUrl = $"{query["redirect_uri"]}?error=access_denied&state={query["state"]}";
            callbackTask = SendCallbackRawAsync(callbackUrl);
        }

        var provider = new OAuthPkceAuthProvider(
            CreateOptions(), httpClient, tokenCache: null, logger: null, openBrowser: OpenBrowser);

        // Act / Assert
        InvalidOperationException ex =
            await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetTokenAsync());
        Assert.Contains("access_denied", ex.Message, StringComparison.OrdinalIgnoreCase);

        if (callbackTask is not null)
        {
            await callbackTask;
        }
    }

    [Fact]
    public async Task GetTokenAsync_UsesCachedToken_WithoutLaunchingBrowser()
    {
        // Arrange
        var cache = new OAuthTokenCache();
        await cache.StoreTokenAsync("client-1@auth.example.com", "cached-token", DateTimeOffset.UtcNow.AddHours(1));

        var mockHttp = new MockHttpMessageHandler();
        using HttpClient httpClient = mockHttp.ToHttpClient();

        var provider = new OAuthPkceAuthProvider(
            CreateOptions(),
            httpClient,
            tokenCache: cache,
            logger: null,
            openBrowser: _ => throw new InvalidOperationException("Browser must not be launched when a token is cached."));

        // Act
        string token = await provider.GetTokenAsync();

        // Assert
        Assert.Equal("cached-token", token);
    }

    [Fact]
    public void Constructor_Throws_WhenAuthorizationEndpointNotHttps()
    {
        // Arrange
        OAuthOptions options = CreateOptions() with
        {
            AuthorizationEndpoint = new Uri("http://auth.example.com/authorize"),
        };
        using var httpClient = new HttpClient();

        // Act / Assert
        Assert.Throws<ArgumentException>(() => new OAuthPkceAuthProvider(options, httpClient));
    }

    [Fact]
    public void Constructor_Throws_WhenTokenEndpointNotHttps()
    {
        // Arrange
        OAuthOptions options = CreateOptions() with
        {
            TokenEndpoint = new Uri("http://auth.example.com/token"),
        };
        using var httpClient = new HttpClient();

        // Act / Assert
        Assert.Throws<ArgumentException>(() => new OAuthPkceAuthProvider(options, httpClient));
    }

    private static async Task SendCallbackAsync(string redirectUri, string code, string state)
    {
        string callbackUrl = $"{redirectUri}?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}";
        await SendCallbackRawAsync(callbackUrl);
    }

    private static async Task SendCallbackRawAsync(string callbackUrl)
    {
        using var client = new HttpClient();
        using HttpResponseMessage response = await client.GetAsync(callbackUrl);
    }

    private static IReadOnlyDictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
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
}
