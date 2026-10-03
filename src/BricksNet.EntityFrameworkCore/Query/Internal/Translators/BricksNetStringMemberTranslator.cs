using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Query.Internal.Translators;

/// <summary>Translates <see cref="string.Length"/> to <c>char_length</c>.</summary>
/// <remarks>
/// Databricks counts characters (code points) where .NET counts UTF-16 code units, so strings with
/// characters outside the Basic Multilingual Plane (e.g. emoji) report a shorter length.
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetStringMemberTranslator : IMemberTranslator
{
    private static readonly PropertyInfo Length = typeof(string).GetRuntimeProperty(nameof(string.Length))!;

    private readonly ISqlExpressionFactory _sql;
    private readonly RelationalTypeMapping _intMapping;

    /// <summary>Initialises a new instance.</summary>
    public BricksNetStringMemberTranslator(ISqlExpressionFactory sqlExpressionFactory, IRelationalTypeMappingSource typeMappingSource)
    {
        _sql = sqlExpressionFactory;
        _intMapping = typeMappingSource.FindMapping(typeof(int))!;
    }

    /// <inheritdoc />
    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MemberInfo member,
        Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger) =>
        instance is not null && member == Length
            ? _sql.Function("char_length", [instance], typeof(int), _intMapping)
            : null;
}
