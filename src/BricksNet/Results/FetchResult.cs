namespace BricksNet.Results;

/// <summary>
/// Raw result payload returned by a backend fetch.
/// <para>
/// For <see cref="ResultFormat.Arrow"/> and <see cref="ResultFormat.JsonColumnar"/>
/// the data is in <see cref="RawPayload"/>.
/// For <see cref="ResultFormat.CloudFetch"/> the data is referenced via
/// <see cref="ExternalLinks"/>.
/// </para>
/// <para>
/// <see cref="HasMoreRows"/> is <see langword="true"/> when the backend holds further result
/// pages; callers invoke <see cref="Contracts.IOperationBackend.FetchResultsAsync"/> again to
/// retrieve the next one.
/// </para>
/// <para>
/// <see cref="Lz4Compressed"/> is <see langword="true"/> when each CloudFetch chunk is LZ4
/// frame-compressed. Inline payloads are always delivered uncompressed.
/// </para>
/// </summary>
public sealed record FetchResult(
    ResultFormat Format,
    ReadOnlyMemory<byte> RawPayload,
    IReadOnlyList<ExternalLink>? ExternalLinks,
    IReadOnlyList<ColumnInfo> Schema,
    bool HasMoreRows = false,
    bool Lz4Compressed = false);
