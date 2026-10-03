using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Storage.Internal.Mapping;

/// <summary>
/// Maps <see cref="ulong"/> to <c>DECIMAL(20,0)</c> (Databricks has no unsigned types), with <c>BD</c>-suffixed literals.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetULongTypeMapping : ULongTypeMapping
{
    /// <summary>Initialises a new instance.</summary>
    public BricksNetULongTypeMapping()
        : base("DECIMAL(20,0)", System.Data.DbType.UInt64)
    {
    }

    /// <summary>Initialises a new instance from mapping parameters.</summary>
    protected BricksNetULongTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) =>
        new BricksNetULongTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value) =>
        BricksNetLiterals.Integral(Convert.ToUInt64(value, CultureInfo.InvariantCulture), "BD");
}
