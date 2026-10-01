using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace BricksNet.EntityFrameworkCore.Query.Internal;

/// <summary>
/// Translates scalar expressions for Databricks. EF routes <see cref="Math.Max(int, int)"/>,
/// <see cref="Math.Min(int, int)"/> and <c>EF.Functions.Greatest</c>/<c>Least</c> here, as
/// Databricks <c>greatest</c>/<c>least</c>, which skip <c>NULL</c> arguments.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetSqlTranslatingExpressionVisitor : RelationalSqlTranslatingExpressionVisitor
{
    /// <summary>Initialises a new instance.</summary>
    public BricksNetSqlTranslatingExpressionVisitor(
        RelationalSqlTranslatingExpressionVisitorDependencies dependencies,
        QueryCompilationContext queryCompilationContext,
        QueryableMethodTranslatingExpressionVisitor queryableMethodTranslatingExpressionVisitor)
        : base(dependencies, queryCompilationContext, queryableMethodTranslatingExpressionVisitor)
    {
    }

    /// <summary>Translates <c>byte[].Length</c> to <c>length</c>, which counts bytes for <c>BINARY</c>.</summary>
    protected override Expression VisitUnary(UnaryExpression unaryExpression)
    {
        ArgumentNullException.ThrowIfNull(unaryExpression);

        if (unaryExpression.NodeType != ExpressionType.ArrayLength || unaryExpression.Operand.Type != typeof(byte[]))
        {
            return base.VisitUnary(unaryExpression);
        }

        return Visit(unaryExpression.Operand) is SqlExpression bytes
            ? Dependencies.SqlExpressionFactory.Function("length", [bytes], nullable: true, [true], typeof(int))
            : QueryCompilationContext.NotTranslatedExpression;
    }

    /// <inheritdoc />
    public override SqlExpression? GenerateGreatest(IReadOnlyList<SqlExpression> expressions, Type resultType) =>
        GenerateExtremum("greatest", expressions, resultType);

    /// <inheritdoc />
    public override SqlExpression? GenerateLeast(IReadOnlyList<SqlExpression> expressions, Type resultType) =>
        GenerateExtremum("least", expressions, resultType);

    private SqlExpression GenerateExtremum(string function, IReadOnlyList<SqlExpression> expressions, Type resultType)
    {
        var sql = Dependencies.SqlExpressionFactory;
        var typeMapping = ExpressionExtensions.InferTypeMapping([.. expressions]);
        return sql.Function(
            function,
            expressions.Select(e => sql.ApplyTypeMapping(e, typeMapping)),
            nullable: true,
            expressions.Select(_ => false),
            resultType,
            typeMapping);
    }
}
