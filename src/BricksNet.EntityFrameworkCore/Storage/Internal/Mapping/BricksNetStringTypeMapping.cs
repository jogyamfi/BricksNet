using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Storage.Internal.Mapping;

/// <summary>
/// Maps <see cref="string"/> to Databricks <c>STRING</c>, generating literals with backslash
/// escaping (see <see cref="BricksNetSqlGenerationHelper.GenerateStringLiteral"/>).
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetStringTypeMapping : StringTypeMapping
{
    /// <summary>Initialises a new instance.</summary>
    public BricksNetStringTypeMapping(string storeType = "STRING", int? size = null)
        : base(storeType, System.Data.DbType.String, unicode: true, size)
    {
    }

    /// <summary>Initialises a new instance from mapping parameters.</summary>
    protected BricksNetStringTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) =>
        new BricksNetStringTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value) =>
        BricksNetSqlGenerationHelper.GenerateStringLiteral((string)value);
}
