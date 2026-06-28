namespace BricksNet.Backends.Thrift;

/// <summary>Helpers for deriving stable, opaque identifiers from Thrift handle GUIDs.</summary>
internal static class ThriftHandle
{
    /// <summary>
    /// Produces a stable, opaque identifier from the raw handle GUID bytes. Returns an
    /// empty string when no GUID is present.
    /// </summary>
    public static string ToId(byte[]? guidBytes)
        => guidBytes is { Length: > 0 } ? Convert.ToHexString(guidBytes).ToLowerInvariant() : string.Empty;
}
