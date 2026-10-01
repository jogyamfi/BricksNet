using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Storage.Internal.Mapping;

/// <summary>
/// Maps <see cref="float"/> to <c>FLOAT</c>, with <c>F</c>-suffixed literals.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetFloatTypeMapping : FloatTypeMapping
{
    /// <summary>Initialises a new instance.</summary>
    public BricksNetFloatTypeMapping()
        : base("FLOAT", System.Data.DbType.Single)
    {
    }

    /// <summary>Initialises a new instance from mapping parameters.</summary>
    protected BricksNetFloatTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) =>
        new BricksNetFloatTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value) =>
        BricksNetLiterals.FloatingPoint(Convert.ToSingle(value, CultureInfo.InvariantCulture), "F", "FLOAT");
}
