using Microsoft.EntityFrameworkCore.Query;

namespace BricksNet.EntityFrameworkCore.Query.Internal;

/// <summary>Creates <see cref="BricksNetParameterBasedSqlProcessor"/> instances.</summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetParameterBasedSqlProcessorFactory : IRelationalParameterBasedSqlProcessorFactory
{
    private readonly RelationalParameterBasedSqlProcessorDependencies _dependencies;

    /// <summary>Initialises a new instance.</summary>
    public BricksNetParameterBasedSqlProcessorFactory(RelationalParameterBasedSqlProcessorDependencies dependencies)
    {
        _dependencies = dependencies;
    }

    /// <inheritdoc />
    public virtual RelationalParameterBasedSqlProcessor Create(RelationalParameterBasedSqlProcessorParameters parameters) =>
        new BricksNetParameterBasedSqlProcessor(_dependencies, parameters);
}
