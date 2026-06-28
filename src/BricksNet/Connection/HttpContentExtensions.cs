using System.Text.Json;

namespace BricksNet.Connection;

/// <summary>
/// Helpers for reading <see cref="HttpContent"/> bodies in the formats the Databricks backends
/// return: raw Arrow stream bytes and columnar JSON.
/// </summary>
public static class HttpContentExtensions
{
    /// <summary>
    /// Buffers the full response body into memory and returns it as a <see cref="ReadOnlyMemory{T}"/>.
    /// Used for Arrow stream payloads, which must be fully materialised before parsing.
    /// </summary>
    public static async Task<ReadOnlyMemory<byte>> ReadAsArrowAsync(
        this HttpContent content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        byte[] bytes = await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return bytes;
    }

    /// <summary>
    /// Reads the response body and parses it as a columnar JSON document.
    /// The caller owns the returned <see cref="JsonDocument"/> and must dispose it.
    /// </summary>
    public static async Task<JsonDocument> ReadAsJsonColumnarAsync(
        this HttpContent content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        Stream stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            return await JsonDocument.ParseAsync(stream, default, cancellationToken).ConfigureAwait(false);
        }
    }
}
