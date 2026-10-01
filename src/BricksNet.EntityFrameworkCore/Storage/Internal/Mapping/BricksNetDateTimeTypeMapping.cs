using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Storage.Internal.Mapping;

/// <summary>
/// Maps <see cref="DateTime"/> to <c>TIMESTAMP_NTZ</c>, a wall-clock value without a time zone. The
/// <see cref="DateTime.Kind"/> is not stored, and values are truncated to microseconds.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetDateTimeTypeMapping : DateTimeTypeMapping
{
    /// <summary>Initialises a new instance.</summary>
    public BricksNetDateTimeTypeMapping()
        : base("TIMESTAMP_NTZ", System.Data.DbType.DateTime2)
    {
    }

    /// <summary>Initialises a new instance from mapping parameters.</summary>
    protected BricksNetDateTimeTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) =>
        new BricksNetDateTimeTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value) =>
        BricksNetLiterals.TimestampNtz((DateTime)value);
}
