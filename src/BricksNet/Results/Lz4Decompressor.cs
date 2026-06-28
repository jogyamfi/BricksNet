using K4os.Compression.LZ4.Streams;

namespace BricksNet.Results;

/// <summary>
/// Wraps a stream of LZ4 frame-compressed bytes (the format used by Databricks CloudFetch chunks)
/// in a decompressing read stream.
/// </summary>
internal static class Lz4Decompressor
{
    /// <summary>
    /// Returns a stream that transparently decompresses <paramref name="input"/>. Disposing the
    /// returned stream disposes <paramref name="input"/>.
    /// </summary>
    public static Stream Decode(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return LZ4Stream.Decode(input, settings: null, leaveOpen: false);
    }
}
