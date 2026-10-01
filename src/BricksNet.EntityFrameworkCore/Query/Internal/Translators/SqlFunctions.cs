using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Query.Internal.Translators;

/// <summary>Shorthand for building Databricks function calls in translators.</summary>
internal static class SqlFunctions
{
    /// <summary>
    /// A function that returns <c>NULL</c> when any argument is <c>NULL</c> — true of almost every
    /// Databricks scalar function.
    /// </summary>
    public static SqlExpression Function(
        this ISqlExpressionFactory sql,
        string name,
        IReadOnlyList<SqlExpression> arguments,
        Type returnType,
        RelationalTypeMapping? typeMapping = null) =>
        sql.Function(name, arguments, nullable: true, arguments.Select(_ => true), returnType, typeMapping);

    /// <summary>A Databricks datetime unit keyword (<c>DAY</c>, <c>MONTH</c>, ...) for <c>timestampadd</c>/<c>timestampdiff</c>.</summary>
    public static SqlExpression Unit(this ISqlExpressionFactory sql, string unit) => sql.Fragment(unit);
}
