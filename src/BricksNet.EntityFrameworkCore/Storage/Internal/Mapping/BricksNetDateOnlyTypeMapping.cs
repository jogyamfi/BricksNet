using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Storage.Internal.Mapping;

/// <summary>
/// Maps <see cref="DateOnly"/> to <c>DATE</c>, with <c>DATE'yyyy-MM-dd'</c> literals.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetDateOnlyTypeMapping : DateOnlyTypeMapping
{
    /// <summary>Initialises a new instance.</summary>
    public BricksNetDateOnlyTypeMapping()
        : base("DATE", System.Data.DbType.Date)
    {
    }

    /// <summary>Initialises a new instance from mapping parameters.</summary>
    protected BricksNetDateOnlyTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) =>
        new BricksNetDateOnlyTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value) =>
        BricksNetLiterals.Date((DateOnly)value);
}
