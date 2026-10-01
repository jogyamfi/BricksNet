using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Storage.Internal.Mapping;

/// <summary>
/// Maps <see cref="long"/> to <c>BIGINT</c>, with <c>L</c>-suffixed literals.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetLongTypeMapping : LongTypeMapping
{
    /// <summary>Initialises a new instance.</summary>
    public BricksNetLongTypeMapping()
        : base("BIGINT", System.Data.DbType.Int64)
    {
    }

    /// <summary>Initialises a new instance from mapping parameters.</summary>
    protected BricksNetLongTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) =>
        new BricksNetLongTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value) =>
        BricksNetLiterals.Integral(Convert.ToInt64(value, CultureInfo.InvariantCulture), "L");
}
