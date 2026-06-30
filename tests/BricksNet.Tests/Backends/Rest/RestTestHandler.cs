using System.Net;

namespace BricksNet.Backends.Rest;

/// <summary>
/// Configurable test double for <see cref="HttpMessageHandler"/> used by the REST backend tests.
/// Records every request and returns responses produced by the supplied responder delegate.
/// </summary>
internal sealed class RestTestHandler(
    Func<HttpRequestMessage, string?, (HttpStatusCode Status, string Body)> responder)
    : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public List<string?> Bodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string? body = null;
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        Requests.Add(request);
        Bodies.Add(body);

        var (status, responseBody) = responder(request, body);
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(responseBody),
        };
    }
}
