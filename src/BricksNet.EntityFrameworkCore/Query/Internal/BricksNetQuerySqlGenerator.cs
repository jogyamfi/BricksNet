using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace BricksNet.EntityFrameworkCore.Query.Internal;

/// <summary>
/// Generates Databricks SQL from EF's relational query tree. Differences from EF's default SQL:
/// <c>LIMIT</c>/<c>OFFSET</c> paging, <c>DIV</c> for integer division (Databricks <c>/</c> always
/// returns a fractional result), <c>||</c> for string concatenation (<c>+</c> is arithmetic only),
/// and <c>LATERAL</c> joins in place of <c>CROSS APPLY</c>/<c>OUTER APPLY</c>.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetQuerySqlGenerator : QuerySqlGenerator
{
    /// <summary>Initialises a new instance.</summary>
    public BricksNetQuerySqlGenerator(QuerySqlGeneratorDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <summary>Generates <c>LIMIT n</c>, <c>OFFSET m</c> or both.</summary>
    protected override void GenerateLimitOffset(SelectExpression selectExpression)
    {
        ArgumentNullException.ThrowIfNull(selectExpression);

        if (selectExpression.Limit is { } limit)
        {
            Sql.AppendLine().Append("LIMIT ");
            Visit(limit);
        }

        if (selectExpression.Offset is { } offset)
        {
            Sql.AppendLine().Append("OFFSET ");
            Visit(offset);
        }
    }

    /// <summary>Uses <c>||</c> for string concatenation, which Databricks' <c>+</c> does not perform.</summary>
    protected override string GetOperator(SqlBinaryExpression binaryExpression)
    {
        ArgumentNullException.ThrowIfNull(binaryExpression);

        return binaryExpression.OperatorType == ExpressionType.Add && binaryExpression.Type == typeof(string)
            ? " || "
            : base.GetOperator(binaryExpression);
    }

    /// <summary>
    /// Generates integer division as <c>CAST(a DIV b AS type)</c>: Databricks' <c>/</c> returns a
    /// fractional result even for integers, while <c>DIV</c> truncates toward zero as C# does, but
    /// always returns <c>BIGINT</c>.
    /// </summary>
    protected override Expression VisitSqlBinary(SqlBinaryExpression sqlBinaryExpression)
    {
        ArgumentNullException.ThrowIfNull(sqlBinaryExpression);

        if (sqlBinaryExpression.OperatorType != ExpressionType.Divide
            || !IsInteger(sqlBinaryExpression.Left.Type)
            || !IsInteger(sqlBinaryExpression.Right.Type))
        {
            return base.VisitSqlBinary(sqlBinaryExpression);
        }

        var storeType = sqlBinaryExpression.TypeMapping?.StoreType;
        var cast = storeType is not null && !string.Equals(storeType, "BIGINT", StringComparison.OrdinalIgnoreCase);
        if (cast)
        {
            Sql.Append("CAST(");
        }

        VisitDivOperand(sqlBinaryExpression.Left);
        Sql.Append(" DIV ");
        VisitDivOperand(sqlBinaryExpression.Right);

        if (cast)
        {
            Sql.Append(" AS ").Append(storeType!).Append(")");
        }

        return sqlBinaryExpression;
    }

    /// <summary>Generates <c>CROSS APPLY</c> as a <c>CROSS JOIN LATERAL</c>.</summary>
    protected override Expression VisitCrossApply(CrossApplyExpression crossApplyExpression)
    {
        ArgumentNullException.ThrowIfNull(crossApplyExpression);

        Sql.Append("CROSS JOIN LATERAL ");
        Visit(crossApplyExpression.Table);
        return crossApplyExpression;
    }

    /// <summary>Generates <c>OUTER APPLY</c> as a <c>LEFT JOIN LATERAL ... ON TRUE</c>.</summary>
    protected override Expression VisitOuterApply(OuterApplyExpression outerApplyExpression)
    {
        ArgumentNullException.ThrowIfNull(outerApplyExpression);

        Sql.Append("LEFT JOIN LATERAL ");
        Visit(outerApplyExpression.Table);
        Sql.Append(" ON TRUE");
        return outerApplyExpression;
    }

    private void VisitDivOperand(SqlExpression operand)
    {
        var parenthesize = operand is SqlBinaryExpression;
        if (parenthesize)
        {
            Sql.Append("(");
        }

        Visit(operand);

        if (parenthesize)
        {
            Sql.Append(")");
        }
    }

    private static bool IsInteger(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsEnum)
        {
            type = Enum.GetUnderlyingType(type);
        }

        return type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(sbyte)
            || type == typeof(byte) || type == typeof(uint) || type == typeof(ushort) || type == typeof(ulong);
    }
}
