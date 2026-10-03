using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Query.Internal.Translators;

/// <summary>Translates single-argument <see cref="Convert"/> methods to <c>CAST</c>.</summary>
/// <remarks>
/// <c>CAST</c> truncates when converting fractional values to integers, while
/// <see cref="Convert.ToInt32(double)"/> rounds; Databricks raises an error for strings that do
/// not parse, as .NET does.
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetConvertTranslator : IMethodCallTranslator
{
    private static readonly HashSet<string> ConvertMethods = new(StringComparer.Ordinal)
    {
        nameof(Convert.ToBoolean),
        nameof(Convert.ToByte),
        nameof(Convert.ToSByte),
        nameof(Convert.ToInt16),
        nameof(Convert.ToInt32),
        nameof(Convert.ToInt64),
        nameof(Convert.ToDecimal),
        nameof(Convert.ToDouble),
        nameof(Convert.ToSingle),
        nameof(Convert.ToString),
    };

    private static readonly HashSet<Type> SourceTypes =
    [
        typeof(bool), typeof(byte), typeof(sbyte), typeof(short), typeof(int), typeof(long),
        typeof(decimal), typeof(double), typeof(float), typeof(string), typeof(object),
    ];

    private readonly ISqlExpressionFactory _sql;
    private readonly IRelationalTypeMappingSource _typeMappingSource;

    /// <summary>Initialises a new instance.</summary>
    public BricksNetConvertTranslator(ISqlExpressionFactory sqlExpressionFactory, IRelationalTypeMappingSource typeMappingSource)
    {
        _sql = sqlExpressionFactory;
        _typeMappingSource = typeMappingSource;
    }

    /// <inheritdoc />
    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (method.DeclaringType != typeof(Convert)
            || !ConvertMethods.Contains(method.Name)
            || method.GetParameters() is not [var parameter]
            || !SourceTypes.Contains(parameter.ParameterType))
        {
            return null;
        }

        return _sql.Convert(arguments[0], method.ReturnType, _typeMappingSource.FindMapping(method.ReturnType));
    }
}
