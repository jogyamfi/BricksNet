namespace BricksNet.EntityFrameworkCore.Metadata.Internal;

/// <summary>Names of the model annotations used by the BricksNet provider.</summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public static class BricksNetAnnotationNames
{
    /// <summary>The prefix of every BricksNet annotation.</summary>
    public const string Prefix = "BricksNet:";

    /// <summary>
    /// The Unity Catalog catalog of a table: on the model it is the default for every entity type,
    /// on an entity type it overrides that default.
    /// </summary>
    public const string Catalog = Prefix + "Catalog";
}
