using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace BricksNet.EntityFrameworkCore.Query.Internal;

/// <summary>
/// Translates scalar expressions for Databricks. EF routes <see cref="Math.Max(int, int)"/>,
/// <see cref="Math.Min(int, int)"/> and <c>EF.Functions.Greatest</c>/<c>Least</c> here, as
/// Databricks <c>greatest</c>/<c>least</c>, which skip <c>NULL</c> arguments. Floating-point
/// ordering comparisons get NaN guards to match .NET semantics.
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

    /// <summary>
    /// Guards floating-point ordering comparisons against NaN. Databricks orders NaN above every
    /// other value (so <c>NaN &gt; 6</c> is true), while every .NET ordering comparison with NaN is
    /// false; <c>NOT isnan(x)</c> is added for each non-constant <c>double</c>/<c>float</c> operand.
    /// </summary>
    protected override Expression VisitBinary(BinaryExpression binaryExpression)
    {
        var result = base.VisitBinary(binaryExpression);
        if (result is not SqlBinaryExpression
            {
                OperatorType: ExpressionType.LessThan or ExpressionType.LessThanOrEqual
                    or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual,
            } comparison)
        {
            return result;
        }

        var sql = Dependencies.SqlExpressionFactory;
        SqlExpression guarded = comparison;
        foreach (var operand in new[] { comparison.Left, comparison.Right })
        {
            if (operand is not SqlConstantExpression && IsFloatingPoint(operand.Type))
            {
                var isNan = sql.Function("isnan", [operand], nullable: false, [false], typeof(bool));
                guarded = sql.AndAlso(guarded, sql.Not(isNan));
            }
        }

        return guarded;
    }

    private static bool IsFloatingPoint(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(double) || type == typeof(float);
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
