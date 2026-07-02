using System.Text.Json;
using System.Text.Json.Serialization;

namespace BricksNet.Backends.Rest.Dto;

/// <summary>
/// A single result chunk, returned inline in <see cref="ExecuteStatementResponse.Result"/>
/// or from <c>GET /statements/{id}/result/chunks/{index}</c>. Carries either inline
/// <see cref="Data"/> (<c>INLINE</c> disposition) or <see cref="ExternalLinks"/> (<c>EXTERNAL_LINKS</c>).
/// </summary>
internal sealed class ResultData
{
    /// <summary>Zero-based index of this chunk.</summary>
    public long ChunkIndex { get; set; }

    /// <summary>Row offset of the first row in this chunk.</summary>
    public long RowOffset { get; set; }

    /// <summary>Number of rows in this chunk.</summary>
    public long RowCount { get; set; }

    /// <summary>Uncompressed size of this chunk in bytes.</summary>
    public long ByteCount { get; set; }

    /// <summary>Index of the next chunk, or <see langword="null"/> when this is the last chunk.</summary>
    public long? NextChunkIndex { get; set; }

    /// <summary>Relative internal link used to fetch the next chunk, when present.</summary>
    public string? NextChunkInternalLink { get; set; }

    /// <summary>
    /// Inline result payload (<c>INLINE</c> disposition). For <c>JSON_ARRAY</c> this is an
    /// array of row arrays; for <c>ARROW_STREAM</c> it is a base64-encoded byte string.
    /// The API delivers this under the <c>data_array</c> field.
    /// </summary>
    [JsonPropertyName("data_array")]
    public JsonElement? Data { get; set; }

    /// <summary>External result links (<c>EXTERNAL_LINKS</c> disposition / CloudFetch).</summary>
    public IReadOnlyList<ExternalLinkDto>? ExternalLinks { get; set; }
}

/// <summary>A presigned external link to a single result chunk (CloudFetch).</summary>
internal sealed class ExternalLinkDto
{
    /// <summary>Presigned URL of the chunk payload.</summary>
    public string? ExternalLink { get; set; }

    /// <summary>Expiry instant of the presigned URL (ISO-8601).</summary>
    public string? Expiration { get; set; }

    /// <summary>Zero-based index of this chunk.</summary>
    public long ChunkIndex { get; set; }

    /// <summary>Row offset of the first row in this chunk.</summary>
    public long RowOffset { get; set; }

    /// <summary>Number of rows in this chunk.</summary>
    public long RowCount { get; set; }

    /// <summary>Size of the chunk payload in bytes.</summary>
    public long ByteCount { get; set; }

    /// <summary>Index of the next chunk, or <see langword="null"/> when this is the last chunk.</summary>
    public long? NextChunkIndex { get; set; }

    /// <summary>Relative internal link used to fetch the next chunk, when present.</summary>
    public string? NextChunkInternalLink { get; set; }

    /// <summary>Additional headers that must be sent when downloading <see cref="ExternalLink"/>.</summary>
    public IReadOnlyDictionary<string, string>? HttpHeaders { get; set; }
}
