using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BricksNet.EntityFrameworkCore.Metadata.Internal;

/// <summary>
/// Copies each entity type's Unity Catalog catalog onto the tables and views of the relational
/// model, where SQL generation reads it.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetAnnotationProvider : RelationalAnnotationProvider
{
    /// <summary>Initialises a new instance.</summary>
    public BricksNetAnnotationProvider(RelationalAnnotationProviderDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <inheritdoc />
    public override IEnumerable<IAnnotation> For(ITable table, bool designTime) =>
        WithCatalog(base.For(table, designTime), table.EntityTypeMappings.Select(m => m.TypeBase));

    /// <inheritdoc />
    public override IEnumerable<IAnnotation> For(IView view, bool designTime) =>
        WithCatalog(base.For(view, designTime), view.EntityTypeMappings.Select(m => m.TypeBase));

    private static IEnumerable<IAnnotation> WithCatalog(IEnumerable<IAnnotation> annotations, IEnumerable<ITypeBase> mappedTypes)
    {
        foreach (var annotation in annotations)
        {
            yield return annotation;
        }

        // Entity types sharing a table (owned types, table splitting) share the owner's catalog.
        var catalog = mappedTypes.OfType<IEntityType>().Select(e => e.GetCatalog()).FirstOrDefault(c => c is not null);
        if (catalog is not null)
        {
            yield return new Annotation(BricksNetAnnotationNames.Catalog, catalog);
        }
    }
}
