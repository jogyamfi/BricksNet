using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Storage.Internal.Mapping;

/// <summary>
/// Maps <see cref="sbyte"/> to <c>TINYINT</c>, with <c>Y</c>-suffixed literals.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetSByteTypeMapping : SByteTypeMapping
{
    /// <summary>Initialises a new instance.</summary>
    public BricksNetSByteTypeMapping()
        : base("TINYINT", System.Data.DbType.SByte)
    {
    }

    /// <summary>Initialises a new instance from mapping parameters.</summary>
    protected BricksNetSByteTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) =>
        new BricksNetSByteTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value) =>
        BricksNetLiterals.Integral(Convert.ToSByte(value, CultureInfo.InvariantCulture), "Y");
}
