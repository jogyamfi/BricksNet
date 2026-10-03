using BricksNet.EntityFrameworkCore.Query.Internal.Translators;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Query.Internal;

/// <summary>Registers the BricksNet aggregate translators alongside EF's relational ones.</summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetAggregateMethodCallTranslatorProvider : RelationalAggregateMethodCallTranslatorProvider
{
    /// <summary>Initialises a new instance.</summary>
    public BricksNetAggregateMethodCallTranslatorProvider(RelationalAggregateMethodCallTranslatorProviderDependencies dependencies)
        : base(dependencies)
    {
        AddTranslators(
        [
            new BricksNetStringAggregateMethodTranslator(
                dependencies.SqlExpressionFactory,
                (IRelationalTypeMappingSource)dependencies.RelationalTypeMappingSource),
        ]);
    }
}
