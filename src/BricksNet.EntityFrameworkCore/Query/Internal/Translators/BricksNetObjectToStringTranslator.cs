using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Query.Internal.Translators;

/// <summary>
/// Translates <see cref="object.ToString"/> on numbers, strings, <see cref="Guid"/>, <see cref="char"/>
/// and <see cref="bool"/> to <c>CAST(x AS STRING)</c>. Booleans render as <c>True</c>/<c>False</c> and
/// a <c>NULL</c> nullable value as an empty string, matching .NET.
/// </summary>
/// <remarks>
/// Dates are not translated: Databricks' string form differs from .NET's culture-specific one.
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetObjectToStringTranslator : IMethodCallTranslator
{
    private static readonly HashSet<Type> SupportedTypes =
    [
        typeof(bool), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint),
        typeof(long), typeof(ulong), typeof(decimal), typeof(double), typeof(float), typeof(char), typeof(Guid),
        typeof(string),
    ];

    private readonly ISqlExpressionFactory _sql;
    private readonly RelationalTypeMapping _stringMapping;

    /// <summary>Initialises a new instance.</summary>
    public BricksNetObjectToStringTranslator(ISqlExpressionFactory sqlExpressionFactory, IRelationalTypeMappingSource typeMappingSource)
    {
        _sql = sqlExpressionFactory;
        _stringMapping = typeMappingSource.FindMapping(typeof(string))!;
    }

    /// <inheritdoc />
    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (instance is null || method.Name != nameof(ToString) || arguments.Count != 0)
        {
            return null;
        }

        var underlying = Nullable.GetUnderlyingType(instance.Type) ?? instance.Type;
        if (!SupportedTypes.Contains(underlying))
        {
            return null;
        }

        SqlExpression text = underlying == typeof(bool)
            ? _sql.Case(
                [new CaseWhenClause(instance, _sql.Constant("True", _stringMapping))],
                _sql.Constant("False", _stringMapping))
            : underlying == typeof(string)
                ? instance
                : _sql.Convert(instance, typeof(string), _stringMapping);

        // A null Nullable<T>.ToString() is "", not null. For bool, CASE already yields 'False' on NULL,
        // so it is guarded explicitly. EF's SQL tree drops Nullable<T>, so nullability comes from the column.
        if (underlying != typeof(string) && (instance.Type != underlying || instance is ColumnExpression { IsNullable: true }))
        {
            text = underlying == typeof(bool)
                ? _sql.Case([new CaseWhenClause(_sql.IsNull(instance), _sql.Constant(string.Empty, _stringMapping))], text)
                : _sql.Coalesce(text, _sql.Constant(string.Empty, _stringMapping), _stringMapping);
        }

        return text;
    }
}
