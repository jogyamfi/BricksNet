using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Storage.Internal.Mapping;

/// <summary>
/// Maps <see cref="short"/> to <c>SMALLINT</c>, with <c>S</c>-suffixed literals.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetShortTypeMapping : ShortTypeMapping
{
    /// <summary>Initialises a new instance.</summary>
    public BricksNetShortTypeMapping()
        : base("SMALLINT", System.Data.DbType.Int16)
    {
    }

    /// <summary>Initialises a new instance from mapping parameters.</summary>
    protected BricksNetShortTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) =>
        new BricksNetShortTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value) =>
        BricksNetLiterals.Integral(Convert.ToInt16(value, CultureInfo.InvariantCulture), "S");
}
