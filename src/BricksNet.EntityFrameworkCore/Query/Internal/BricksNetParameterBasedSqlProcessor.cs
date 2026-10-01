using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace BricksNet.EntityFrameworkCore.Query.Internal;

/// <summary>
/// Processes the query tree once parameter values are known. Inlines <c>LIMIT</c>/<c>OFFSET</c>
/// parameters (from <c>Take</c>/<c>Skip</c>) as integer literals, because Databricks requires
/// those clauses to be constant and not every runtime accepts a parameter marker there.
/// </summary>
/// <remarks>
/// Inlining makes the SQL depend on the values, so EF regenerates it for each execution of a
/// paged query instead of caching it.
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetParameterBasedSqlProcessor : RelationalParameterBasedSqlProcessor
{
    /// <summary>Initialises a new instance.</summary>
    public BricksNetParameterBasedSqlProcessor(
        RelationalParameterBasedSqlProcessorDependencies dependencies,
        RelationalParameterBasedSqlProcessorParameters parameters)
        : base(dependencies, parameters)
    {
    }

    /// <inheritdoc />
    public override Expression Process(Expression queryExpression, ParametersCacheDecorator parametersDecorator)
    {
        ArgumentNullException.ThrowIfNull(parametersDecorator);

        var processed = base.Process(queryExpression, parametersDecorator);
        return new LimitOffsetInliner(parametersDecorator).Visit(processed);
    }

    private sealed class LimitOffsetInliner(ParametersCacheDecorator parametersDecorator) : ExpressionVisitor
    {
        protected override Expression VisitExtension(Expression node)
        {
            // Inner queries first, so nested Take/Skip are inlined too.
            var visited = base.VisitExtension(node);
            if (visited is not SelectExpression select
                || (select.Limit is not SqlParameterExpression && select.Offset is not SqlParameterExpression))
            {
                return visited;
            }

            return select.Update(
                select.Tables,
                select.Predicate,
                select.GroupBy,
                select.Having,
                select.Projection,
                select.Orderings,
                Inline(select.Offset),
                Inline(select.Limit));
        }

        private SqlExpression? Inline(SqlExpression? expression)
        {
            if (expression is not SqlParameterExpression parameter)
            {
                return expression;
            }

            // Reading a value marks the SQL as value-specific, so EF does not cache it.
            var value = parametersDecorator.GetAndDisableCaching()[parameter.Name];
            return new SqlConstantExpression(value, parameter.Type, parameter.TypeMapping);
        }
    }
}
