using Microsoft.EntityFrameworkCore.Query;

namespace BricksNet.EntityFrameworkCore.Query.Internal;

/// <summary>Creates <see cref="BricksNetQuerySqlGenerator"/> instances.</summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetQuerySqlGeneratorFactory : IQuerySqlGeneratorFactory
{
    private readonly QuerySqlGeneratorDependencies _dependencies;

    /// <summary>Initialises a new instance.</summary>
    public BricksNetQuerySqlGeneratorFactory(QuerySqlGeneratorDependencies dependencies)
    {
        _dependencies = dependencies;
    }

    /// <inheritdoc />
    public virtual QuerySqlGenerator Create() => new BricksNetQuerySqlGenerator(_dependencies);
}
