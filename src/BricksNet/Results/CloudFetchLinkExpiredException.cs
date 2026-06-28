using BricksNet.AdoNet;

namespace BricksNet.Results;

/// <summary>
/// Thrown when a CloudFetch external result link has passed its expiry time and can no longer be
/// downloaded. The caller should re-fetch the result links from the server and retry.
/// </summary>
public sealed class CloudFetchLinkExpiredException : BricksNetException
{
    /// <summary>Initialises a new instance for the given expired link.</summary>
    public CloudFetchLinkExpiredException(Uri linkUri, DateTimeOffset expiration)
        : base($"The CloudFetch result link '{linkUri}' expired at {expiration:O} and must be re-fetched.")
    {
        LinkUri = linkUri;
        Expiration = expiration;
    }

    /// <summary>Initialises a new instance with a message.</summary>
    public CloudFetchLinkExpiredException(string message)
        : base(message)
    {
    }

    /// <summary>Initialises a new instance with a message and inner exception.</summary>
    public CloudFetchLinkExpiredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The URI of the link that expired, when known.</summary>
    public Uri? LinkUri { get; }

    /// <summary>The instant at which the link expired.</summary>
    public DateTimeOffset Expiration { get; }
}
