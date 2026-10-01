using BricksNet.EntityFrameworkCore.Query.Internal.Translators;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Query.Internal;

/// <summary>Registers the BricksNet member translators alongside EF's relational ones.</summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetMemberTranslatorProvider : RelationalMemberTranslatorProvider
{
    /// <summary>Initialises a new instance.</summary>
    public BricksNetMemberTranslatorProvider(
        RelationalMemberTranslatorProviderDependencies dependencies,
        IRelationalTypeMappingSource typeMappingSource)
        : base(dependencies)
    {
        var sql = dependencies.SqlExpressionFactory;

        AddTranslators(
        [
            new BricksNetStringMemberTranslator(sql, typeMappingSource),
            new BricksNetDateTimeMemberTranslator(sql, typeMappingSource),
        ]);
    }
}
