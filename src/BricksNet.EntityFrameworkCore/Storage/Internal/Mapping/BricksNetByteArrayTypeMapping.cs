using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Storage.Internal.Mapping;

/// <summary>
/// Maps <c>byte[]</c> to <c>BINARY</c>, with <c>X'0A0B'</c> literals.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetByteArrayTypeMapping : ByteArrayTypeMapping
{
    /// <summary>Initialises a new instance.</summary>
    public BricksNetByteArrayTypeMapping()
        : base("BINARY", System.Data.DbType.Binary)
    {
    }

    /// <summary>Initialises a new instance from mapping parameters.</summary>
    protected BricksNetByteArrayTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) =>
        new BricksNetByteArrayTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value) =>
        BricksNetLiterals.Binary((byte[])value);
}
