using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace BricksNet.EntityFrameworkCore.Metadata.Conventions;

/// <summary>Builds the model-building conventions for the BricksNet provider.</summary>
/// <remarks>
/// Uses EF Core's relational conventions as-is for now; Databricks-specific conventions
/// (keys, value generation, catalogs) are layered on here.
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
}
