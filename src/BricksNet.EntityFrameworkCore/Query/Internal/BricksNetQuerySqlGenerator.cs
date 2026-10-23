using System.Linq.Expressions;
using BricksNet.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace BricksNet.EntityFrameworkCore.Query.Internal;

/// <summary>
/// Generates Databricks SQL from EF's relational query tree. Differences from EF's default SQL:
/// <c>LIMIT</c>/<c>OFFSET</c> paging, <c>DIV</c> for integer division (Databricks <c>/</c> always
/// returns a fractional result), <c>||</c> for string concatenation (<c>+</c> is arithmetic only),
/// <c>LATERAL</c> joins in place of <c>CROSS APPLY</c>/<c>OUTER APPLY</c>, and
/// <c>any_value(...)</c> around scalar subqueries projected by a grouped <c>SELECT</c>.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetQuerySqlGenerator : QuerySqlGenerator
{
    private readonly Stack<SelectExpression> _selects = new();
    private bool _inGroupedProjection;

    /// <summary>Initialises a new instance.</summary>
    public BricksNetQuerySqlGenerator(QuerySqlGeneratorDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <inheritdoc />
    protected override Expression VisitSelect(SelectExpression selectExpression)
    {
        ArgumentNullException.ThrowIfNull(selectExpression);

        var inGroupedProjection = _inGroupedProjection;
        _inGroupedProjection = false;
        _selects.Push(selectExpression);
        try
        {
            return base.VisitSelect(selectExpression);
        }
        finally
        {
            _selects.Pop();
            _inGroupedProjection = inGroupedProjection;
        }
    }

    /// <inheritdoc />
    protected override Expression VisitProjection(ProjectionExpression projectionExpression)
    {
        ArgumentNullException.ThrowIfNull(projectionExpression);

        _inGroupedProjection = _selects.TryPeek(out var select) && select.GroupBy.Count > 0;
        try
        {
            return base.VisitProjection(projectionExpression);
        }
        finally
        {
            _inGroupedProjection = false;
        }
    }

    /// <summary>
    /// Databricks rejects a correlated scalar subquery in the <c>SELECT</c> list of a grouped query
    /// unless it is in an aggregate. EF only correlates such subqueries on the grouping key, so the
    /// value is the same for the whole group and <c>any_value</c> returns it unchanged.
    /// </summary>
    protected override Expression VisitScalarSubquery(ScalarSubqueryExpression scalarSubqueryExpression)
    {
        ArgumentNullException.ThrowIfNull(scalarSubqueryExpression);

        if (!_inGroupedProjection)
        {
            return base.VisitScalarSubquery(scalarSubqueryExpression);
        }

        Sql.Append("any_value(");
        base.VisitScalarSubquery(scalarSubqueryExpression);
        Sql.Append(")");
        return scalarSubqueryExpression;
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

    /// <summary>
    /// Generates <c>catalog.schema.table</c> for tables with a Unity Catalog catalog
    /// (<c>HasDefaultCatalog</c>/<c>HasCatalog</c>), and EF's default name otherwise.
    /// </summary>
    protected override Expression VisitTable(TableExpression tableExpression)
    {
        ArgumentNullException.ThrowIfNull(tableExpression);

        if (tableExpression.Table.FindAnnotation(BricksNetAnnotationNames.Catalog)?.Value is not string catalog)
        {
            return base.VisitTable(tableExpression);
        }

        var helper = Dependencies.SqlGenerationHelper;
        Sql.Append(helper.DelimitIdentifier(catalog))
            .Append(".")
            .Append(helper.DelimitIdentifier(tableExpression.Name, tableExpression.Schema))
            .Append(AliasSeparator)
            .Append(helper.DelimitIdentifier(tableExpression.Alias));
        return tableExpression;
    }

    /// <summary>
    /// Generates <c>ExecuteUpdate</c>. Databricks has no <c>UPDATE ... FROM</c>, so when EF needs other
    /// tables (a navigation filter, <c>Take</c>, ...) they move into a correlated
    /// <c>WHERE EXISTS (SELECT 1 FROM ... WHERE ...)</c>. Values taken from those other tables cannot
    /// be expressed that way and are rejected.
    /// </summary>
    protected override Expression VisitUpdate(UpdateExpression updateExpression)
    {
        ArgumentNullException.ThrowIfNull(updateExpression);

        var select = updateExpression.SelectExpression;
        var targetAlias = updateExpression.Table.Alias;
        var otherTables = select.Tables.Where(t => t.Alias != targetAlias).ToList();
        if (otherTables.Count == 0)
        {
            return base.VisitUpdate(updateExpression);
        }

        var aliases = new TableAliasCollector();
        foreach (var setter in updateExpression.ColumnValueSetters)
        {
            aliases.Visit(setter.Value);
        }

        if (aliases.Aliases.Any(alias => alias != targetAlias))
        {
            throw new InvalidOperationException(
                "This ExecuteUpdate sets values taken from another table, which Databricks cannot express: it has no "
                + "UPDATE ... FROM. Use a MERGE INTO statement through ExecuteSql instead.");
        }

        Sql.Append("UPDATE ");
        Visit(updateExpression.Table);
        Sql.AppendLine().Append("SET ");
        for (var i = 0; i < updateExpression.ColumnValueSetters.Count; i++)
        {
            if (i > 0)
            {
                Sql.Append(",").AppendLine().Append("    ");
            }

            var setter = updateExpression.ColumnValueSetters[i];
            Sql.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(setter.Column.Name)).Append(" = ");
            Visit(setter.Value);
        }

        // Inside EXISTS, inner joins become FROM entries whose join conditions move into WHERE.
        var fromTables = new List<TableExpressionBase>();
        var conditions = new List<SqlExpression>();
        foreach (var table in otherTables)
        {
            switch (table)
            {
                case InnerJoinExpression innerJoin:
                    fromTables.Add(innerJoin.Table);
                    conditions.Add(innerJoin.JoinPredicate);
                    break;
                case CrossJoinExpression crossJoin:
                    fromTables.Add(crossJoin.Table);
                    break;
                case JoinExpressionBase:
                    throw new InvalidOperationException(
                        "This ExecuteUpdate needs an outer join, which Databricks cannot express in an UPDATE. "
                        + "Use a MERGE INTO statement through ExecuteSql instead.");
                default:
                    fromTables.Add(table);
                    break;
            }
        }

        if (select.Predicate is not null)
        {
            conditions.Add(select.Predicate);
        }

        Sql.AppendLine().Append("WHERE EXISTS (");
        Sql.IncrementIndent();
        try
        {
            Sql.AppendLine().Append("SELECT 1").AppendLine().Append("FROM ");
            for (var i = 0; i < fromTables.Count; i++)
            {
                if (i > 0)
                {
                    Sql.Append(", ");
                }

                Visit(fromTables[i]);
            }

            if (conditions.Count > 0)
            {
                Sql.AppendLine().Append("WHERE ");
                Visit(conditions.Aggregate((left, right) =>
                    new SqlBinaryExpression(ExpressionType.AndAlso, left, right, typeof(bool), left.TypeMapping)));
            }
        }
        finally
        {
            Sql.DecrementIndent();
        }

        Sql.Append(")");
        return updateExpression;
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

    /// <summary>Collects the table aliases referenced by columns in an expression.</summary>
    private sealed class TableAliasCollector : ExpressionVisitor
    {
        public HashSet<string> Aliases { get; } = new(StringComparer.Ordinal);

        protected override Expression VisitExtension(Expression node)
        {
            if (node is ColumnExpression column)
            {
                Aliases.Add(column.TableAlias);
                return node;
            }

            return base.VisitExtension(node);
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
