using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Storage.Internal.Mapping;

/// <summary>Maps <see cref="bool"/> to Databricks' native <c>BOOLEAN</c>, with <c>true</c>/<c>false</c> literals.</summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetBoolTypeMapping : BoolTypeMapping
{
    /// <summary>Initialises a new instance.</summary>
    public BricksNetBoolTypeMapping()
        : base("BOOLEAN", System.Data.DbType.Boolean)
    {
    }

    /// <summary>Initialises a new instance from mapping parameters.</summary>
    protected BricksNetBoolTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) =>
        new BricksNetBoolTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value) => (bool)value ? "true" : "false";
}
