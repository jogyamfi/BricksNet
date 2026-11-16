namespace BricksNet.Results;

/// <summary>A pre-signed external link for a CloudFetch result chunk.</summary>
internal sealed record ExternalLink(
    Uri Uri,
    long FileSizeBytes,
    DateTimeOffset Expiration,
    long ChunkIndex,
    long RowOffset,
    long RowCount,
    IReadOnlyDictionary<string, string>? HttpHeaders);
