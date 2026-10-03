using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Storage.Internal.Mapping;

/// <summary>
/// Maps <see cref="uint"/> to <c>BIGINT</c> (Databricks has no unsigned types), with <c>L</c>-suffixed literals.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetUIntTypeMapping : UIntTypeMapping
{
    /// <summary>Initialises a new instance.</summary>
    public BricksNetUIntTypeMapping()
        : base("BIGINT", System.Data.DbType.UInt32)
    {
    }

    /// <summary>Initialises a new instance from mapping parameters.</summary>
    protected BricksNetUIntTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) =>
        new BricksNetUIntTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value) =>
        BricksNetLiterals.Integral(Convert.ToUInt32(value, CultureInfo.InvariantCulture), "L");
}
