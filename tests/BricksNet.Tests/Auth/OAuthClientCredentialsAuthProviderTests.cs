using System.Net;
using System.Net.Http.Headers;
using System.Text;
using BricksNet.Contracts.Options;
using RichardSzalay.MockHttp;
using Xunit;

namespace BricksNet.Auth;

public sealed class OAuthClientCredentialsAuthProviderTests
{
    private const string TokenEndpoint = "https://workspace.example.net/oidc/v1/token";
    private const string ClientId = "sp-client-id";
    private const string ClientSecret = "dose-secret-value";

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static OAuthClientCredentialsAuthProvider CreateProvider(HttpClient httpClient) =>
        new(new Uri(TokenEndpoint), ClientId, ClientSecret, httpClient);

    [Fact]
    public async Task GetTokenAsync_PostsClientCredentialsGrant_WithBasicAuth()
    {
        AuthenticationHeaderValue? capturedAuth = null;
        string? capturedBody = null;
        var mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Post, TokenEndpoint)
            .Respond(async req =>
            {
                capturedAuth = req.Headers.Authorization;
                capturedBody = await req.Content!.ReadAsStringAsync();
                return Json(HttpStatusCode.OK, "{\"access_token\":\"m2m-token\",\"token_type\":\"Bearer\",\"expires_in\":3600}");
            });
        using HttpClient httpClient = mockHttp.ToHttpClient();

        string token = await CreateProvider(httpClient).GetTokenAsync();

        Assert.Equal("m2m-token", token);
        Assert.NotNull(capturedAuth);
        Assert.Equal("Basic", capturedAuth!.Scheme);
        Assert.Equal(
            $"{ClientId}:{ClientSecret}",
            Encoding.UTF8.GetString(Convert.FromBase64String(capturedAuth.Parameter!)));
        Assert.Equal("grant_type=client_credentials&scope=all-apis", capturedBody);
    }

    [Fact]
    public async Task GetTokenWithExpiryAsync_ReportsExpiryFromExpiresIn()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, TokenEndpoint)
            .Respond(_ => Json(HttpStatusCode.OK, "{\"access_token\":\"t\",\"expires_in\":3600}"));
        using HttpClient httpClient = mockHttp.ToHttpClient();

        DateTimeOffset before = DateTimeOffset.UtcNow;
        AccessToken token = await CreateProvider(httpClient).GetTokenWithExpiryAsync(CancellationToken.None);

        Assert.InRange(token.ExpiresAtUtc, before.AddSeconds(3590), DateTimeOffset.UtcNow.AddSeconds(3610));
    }

    [Fact]
    public async Task GetTokenAsync_Rejected_ThrowsWithOAuthError_ButNotTheSecret()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, TokenEndpoint)
            .Respond(_ => Json(
                HttpStatusCode.Unauthorized,
                "{\"error\":\"invalid_client\",\"error_description\":\"Client authentication failed\"}"));
        using HttpClient httpClient = mockHttp.ToHttpClient();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateProvider(httpClient).GetTokenAsync());

        Assert.Contains("401", ex.Message, StringComparison.Ordinal);
        Assert.Contains("invalid_client: Client authentication failed", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ClientSecret, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTokenAsync_RejectedWithNonJsonBody_StillThrowsClearly()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, TokenEndpoint)
            .Respond(HttpStatusCode.BadGateway, "text/html", "<html>bad gateway</html>");
        using HttpClient httpClient = mockHttp.ToHttpClient();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateProvider(httpClient).GetTokenAsync());

        Assert.Contains("502", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTokenAsync_ResponseWithoutAccessToken_Throws()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, TokenEndpoint).Respond(_ => Json(HttpStatusCode.OK, "{\"expires_in\":3600}"));
        using HttpClient httpClient = mockHttp.ToHttpClient();

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateProvider(httpClient).GetTokenAsync());
    }

    [Fact]
    public void Constructor_HttpTokenEndpoint_Throws()
    {
        using var httpClient = new HttpClient();

        Assert.Throws<ArgumentException>(() => new OAuthClientCredentialsAuthProvider(
            new Uri("http://workspace.example.net/oidc/v1/token"), ClientId, ClientSecret, httpClient));
    }

    [Fact]
    public void TokenEndpointFor_BuildsWorkspaceOidcEndpoint()
    {
        Assert.Equal(
            new Uri("https://workspace.example.net/oidc/v1/token"),
            OAuthClientCredentialsAuthProvider.TokenEndpointFor("workspace.example.net", 443));
    }

    [Fact]
    public async Task ThroughFactory_TokenIsCachedUntilNearExpiry()
    {
        int requests = 0;
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, TokenEndpoint).Respond(_ =>
        {
            requests++;
            return Json(HttpStatusCode.OK, "{\"access_token\":\"cached\",\"expires_in\":3600}");
        });
        using HttpClient httpClient = mockHttp.ToHttpClient();

        var options = new BricksNetClientOptions
        {
            Host = "workspace.example.net",
            HttpPath = "/sql/1.0/warehouses/abc",
            AuthType = AuthType.OAuthClientCredentials,
            OAuthClientId = ClientId,
        };
        var provider = AuthProviderFactory.Create(options, accessToken: ClientSecret, tokenExchangeClient: httpClient);

        Assert.IsType<CachedTokenProvider>(provider);
        Assert.Equal("cached", await provider.GetTokenAsync());
        Assert.Equal("cached", await provider.GetTokenAsync());
        Assert.Equal(1, requests);
    }

    [Fact]
    public void Factory_MissingClientId_Throws()
    {
        using var httpClient = new HttpClient();
        var options = new BricksNetClientOptions
        {
            Host = "h",
            HttpPath = "/p",
            AuthType = AuthType.OAuthClientCredentials,
        };

        var ex = Assert.Throws<ArgumentException>(() =>
            AuthProviderFactory.Create(options, accessToken: ClientSecret, tokenExchangeClient: httpClient));
        Assert.Contains("OAuthClientId", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Factory_MissingSecret_Throws()
    {
        using var httpClient = new HttpClient();
        var options = new BricksNetClientOptions
        {
            Host = "h",
            HttpPath = "/p",
            AuthType = AuthType.OAuthClientCredentials,
            OAuthClientId = ClientId,
        };

        Assert.Throws<ArgumentException>(() =>
            AuthProviderFactory.Create(options, accessToken: null, tokenExchangeClient: httpClient));
    }
}
