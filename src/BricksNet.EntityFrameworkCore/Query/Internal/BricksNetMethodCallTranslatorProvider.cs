using BricksNet.EntityFrameworkCore.Query.Internal.Translators;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Query.Internal;

/// <summary>Registers the BricksNet method-call translators alongside EF's relational ones.</summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetMethodCallTranslatorProvider : RelationalMethodCallTranslatorProvider
{
    /// <summary>Initialises a new instance.</summary>
    public BricksNetMethodCallTranslatorProvider(RelationalMethodCallTranslatorProviderDependencies dependencies)
        : base(dependencies)
    {
        var sql = dependencies.SqlExpressionFactory;
        var typeMappingSource = (IRelationalTypeMappingSource)dependencies.RelationalTypeMappingSource;

        AddTranslators(
        [
            new BricksNetStringMethodTranslator(sql, typeMappingSource),
            new BricksNetMathTranslator(sql, typeMappingSource),
            new BricksNetDateTimeMethodTranslator(sql, typeMappingSource),
            new BricksNetDbFunctionsTranslator(sql, typeMappingSource),
            new BricksNetConvertTranslator(sql, typeMappingSource),
            new BricksNetObjectToStringTranslator(sql, typeMappingSource),
            new BricksNetMiscMethodTranslator(sql, typeMappingSource),
        ]);
    }
}
