using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;

namespace BricksNet.EntityFrameworkCore.Storage.Internal.Mapping;

/// <summary>
/// Maps <see cref="decimal"/> to <c>DECIMAL(p,s)</c>, defaulting to <c>DECIMAL(18,2)</c> (a bare
/// Databricks <c>DECIMAL</c> means <c>DECIMAL(10,0)</c>), with <c>BD</c>-suffixed literals.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetDecimalTypeMapping : DecimalTypeMapping
{
    /// <summary>The precision used when none is configured.</summary>
    public const int DefaultPrecision = 18;

    /// <summary>The scale used when none is configured.</summary>
    public const int DefaultScale = 2;

    /// <summary>The largest precision Databricks supports.</summary>
    public const int MaxPrecision = 38;

    /// <summary>Initialises a new instance.</summary>
    public BricksNetDecimalTypeMapping(int? precision = DefaultPrecision, int? scale = DefaultScale)
        : this(new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(typeof(decimal), jsonValueReaderWriter: JsonDecimalReaderWriter.Instance),
            "DECIMAL",
            StoreTypePostfix.PrecisionAndScale,
            System.Data.DbType.Decimal,
            precision: precision,
            scale: scale))
    {
    }

    /// <summary>Initialises a new instance from mapping parameters.</summary>
    protected BricksNetDecimalTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) =>
        new BricksNetDecimalTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value) =>
        Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + "BD";

    /// <summary>
    /// Leaves the parameter's precision and scale unset, so a value is sent with its own exact
    /// precision instead of being rounded to the column's scale (which would change comparisons
    /// such as <c>price &gt; 1.555</c>).
    /// </summary>
    protected override void ConfigureParameter(DbParameter parameter)
    {
        base.ConfigureParameter(parameter);
        parameter.Precision = 0;
        parameter.Scale = 0;
    }
}
