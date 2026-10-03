using Microsoft.EntityFrameworkCore.Query;

namespace BricksNet.EntityFrameworkCore.Query.Internal;

/// <summary>Creates <see cref="BricksNetSqlTranslatingExpressionVisitor"/> instances.</summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetSqlTranslatingExpressionVisitorFactory : IRelationalSqlTranslatingExpressionVisitorFactory
{
    private readonly RelationalSqlTranslatingExpressionVisitorDependencies _dependencies;

    /// <summary>Initialises a new instance.</summary>
    public BricksNetSqlTranslatingExpressionVisitorFactory(RelationalSqlTranslatingExpressionVisitorDependencies dependencies)
    {
        _dependencies = dependencies;
    }

    /// <inheritdoc />
    public virtual RelationalSqlTranslatingExpressionVisitor Create(
        QueryCompilationContext queryCompilationContext,
        QueryableMethodTranslatingExpressionVisitor queryableMethodTranslatingExpressionVisitor) =>
        new BricksNetSqlTranslatingExpressionVisitor(_dependencies, queryCompilationContext, queryableMethodTranslatingExpressionVisitor);
}
