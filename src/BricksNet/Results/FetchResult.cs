namespace BricksNet.Results;

/// <summary>
/// Raw result payload returned by a backend fetch.
/// <para>
/// For <see cref="ResultFormat.Arrow"/> and <see cref="ResultFormat.JsonColumnar"/>
/// the data is in <see cref="RawPayload"/>.
/// For <see cref="ResultFormat.CloudFetch"/> the data is referenced via
/// <see cref="ExternalLinks"/>.
/// </para>
/// </summary>
public sealed record FetchResult(
    ResultFormat Format,
    ReadOnlyMemory<byte> RawPayload,
    IReadOnlyList<ExternalLink>? ExternalLinks,
    IReadOnlyList<ColumnInfo> Schema);
