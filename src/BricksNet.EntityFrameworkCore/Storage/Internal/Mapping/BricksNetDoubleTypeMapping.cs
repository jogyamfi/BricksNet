using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Storage.Internal.Mapping;

/// <summary>
/// Maps <see cref="double"/> to <c>DOUBLE</c>, with <c>D</c>-suffixed literals.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetDoubleTypeMapping : DoubleTypeMapping
{
    /// <summary>Initialises a new instance.</summary>
    public BricksNetDoubleTypeMapping()
        : base("DOUBLE", System.Data.DbType.Double)
    {
    }

    /// <summary>Initialises a new instance from mapping parameters.</summary>
    protected BricksNetDoubleTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) =>
        new BricksNetDoubleTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value) =>
        BricksNetLiterals.FloatingPoint(Convert.ToDouble(value, CultureInfo.InvariantCulture), "D", "DOUBLE");
}
