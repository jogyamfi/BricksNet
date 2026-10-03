using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Storage.Internal.Mapping;

/// <summary>
/// Maps <see cref="DateTimeOffset"/> to <c>TIMESTAMP</c>, an instant. The offset is not stored: values
/// are read back in UTC, and are truncated to microseconds.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetDateTimeOffsetTypeMapping : DateTimeOffsetTypeMapping
{
    /// <summary>Initialises a new instance.</summary>
    public BricksNetDateTimeOffsetTypeMapping()
        : base("TIMESTAMP", System.Data.DbType.DateTimeOffset)
    {
    }

    /// <summary>Initialises a new instance from mapping parameters.</summary>
    protected BricksNetDateTimeOffsetTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) =>
        new BricksNetDateTimeOffsetTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value) =>
        BricksNetLiterals.Timestamp((DateTimeOffset)value);
}
