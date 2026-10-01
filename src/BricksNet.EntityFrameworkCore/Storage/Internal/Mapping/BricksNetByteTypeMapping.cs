using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Storage.Internal.Mapping;

/// <summary>
/// Maps <see cref="byte"/> to <c>SMALLINT</c> (Databricks <c>TINYINT</c> is signed), with <c>S</c>-suffixed literals.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetByteTypeMapping : ByteTypeMapping
{
    /// <summary>Initialises a new instance.</summary>
    public BricksNetByteTypeMapping()
        : base("SMALLINT", System.Data.DbType.Byte)
    {
    }

    /// <summary>Initialises a new instance from mapping parameters.</summary>
    protected BricksNetByteTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) =>
        new BricksNetByteTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value) =>
        BricksNetLiterals.Integral(Convert.ToByte(value, CultureInfo.InvariantCulture), "S");
}
