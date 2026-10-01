using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace BricksNet.EntityFrameworkCore.Metadata.Conventions;

/// <summary>Builds the model-building conventions for the BricksNet provider.</summary>
/// <remarks>
/// EF's relational conventions, with integer keys no longer treated as database-generated
/// (see <see cref="BricksNetValueGenerationConvention"/>).
/// </remarks>
public class BricksNetConventionSetBuilder : RelationalConventionSetBuilder
{
    /// <summary>Initialises a new instance.</summary>
    public BricksNetConventionSetBuilder(
        ProviderConventionSetBuilderDependencies dependencies,
        RelationalConventionSetBuilderDependencies relationalDependencies)
        : base(dependencies, relationalDependencies)
    {
    }

    /// <inheritdoc />
    public override ConventionSet CreateConventionSet()
    {
        var conventionSet = base.CreateConventionSet();
        conventionSet.Replace<ValueGenerationConvention>(
            new BricksNetValueGenerationConvention(Dependencies, RelationalDependencies));
        return conventionSet;
    }
}
