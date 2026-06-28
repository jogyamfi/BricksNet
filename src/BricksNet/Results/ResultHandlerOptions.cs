namespace BricksNet.Results;

/// <summary>
/// Tuning options for <see cref="ResultHandlerFactory"/> and the result providers it creates.
/// </summary>
public sealed record ResultHandlerOptions
{
    /// <summary>
    /// Maximum number of rows to surface across all chunks. A non-positive value means unlimited.
    /// When positive the provider is wrapped in a <see cref="ResultSlicer"/>.
    /// </summary>
    public long MaxRows { get; init; }

    /// <summary>Maximum number of parallel CloudFetch downloads. Defaults to 4.</summary>
    public int CloudFetchConcurrentDownloads { get; init; } = 4;

    /// <summary>Whether CloudFetch chunk payloads are LZ4 frame-compressed.</summary>
    public bool Lz4Compressed { get; init; }

    /// <summary>
    /// Optional client for CloudFetch downloads. These target pre-signed external URLs and must
    /// not carry Databricks authentication headers. When <see langword="null"/>, a shared default
    /// client is used.
    /// </summary>
    public HttpClient? DownloadClient { get; init; }
}
