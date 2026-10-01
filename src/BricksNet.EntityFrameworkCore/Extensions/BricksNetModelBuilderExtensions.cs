using BricksNet.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

// Extension methods live in EF's namespace so they are discoverable next to the built-in model API.
namespace Microsoft.EntityFrameworkCore;

/// <summary>BricksNet-specific model configuration: Unity Catalog catalogs.</summary>
public static class BricksNetModelBuilderExtensions
{
    /// <summary>
    /// Sets the Unity Catalog catalog for every table in the model, so tables are referenced as
    /// <c>catalog.schema.table</c>. Without one, tables resolve against the connection's current catalog.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <param name="catalog">The catalog name, or <see langword="null"/> to use the connection's catalog.</param>
    /// <returns>The same builder so that calls can be chained.</returns>
    /// <remarks>Tables in a catalog must also have a schema (<c>ToTable(name, schema)</c> or <c>HasDefaultSchema</c>).</remarks>
    public static ModelBuilder HasDefaultCatalog(this ModelBuilder modelBuilder, string? catalog)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Model.SetAnnotation(BricksNetAnnotationNames.Catalog, NullIfEmpty(catalog));
        return modelBuilder;
    }

    /// <summary>
    /// Sets the Unity Catalog catalog of this entity type's table, overriding the model default.
    /// </summary>
    /// <param name="entityTypeBuilder">The entity type builder.</param>
    /// <param name="catalog">The catalog name, or <see langword="null"/> to use the model default.</param>
    /// <returns>The same builder so that calls can be chained.</returns>
    public static EntityTypeBuilder HasCatalog(this EntityTypeBuilder entityTypeBuilder, string? catalog)
    {
        ArgumentNullException.ThrowIfNull(entityTypeBuilder);

        entityTypeBuilder.Metadata.SetAnnotation(BricksNetAnnotationNames.Catalog, NullIfEmpty(catalog));
        return entityTypeBuilder;
    }

    /// <inheritdoc cref="HasCatalog(EntityTypeBuilder, string?)"/>
    public static EntityTypeBuilder<TEntity> HasCatalog<TEntity>(this EntityTypeBuilder<TEntity> entityTypeBuilder, string? catalog)
        where TEntity : class =>
        (EntityTypeBuilder<TEntity>)HasCatalog((EntityTypeBuilder)entityTypeBuilder, catalog);

    /// <summary>The model's default Unity Catalog catalog, if one is set.</summary>
    /// <param name="model">The model.</param>
    /// <returns>The catalog name, or <see langword="null"/>.</returns>
    public static string? GetDefaultCatalog(this IReadOnlyModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return (string?)model[BricksNetAnnotationNames.Catalog];
    }

    /// <summary>The Unity Catalog catalog of the entity type's table: its own, or the model default.</summary>
    /// <param name="entityType">The entity type.</param>
    /// <returns>The catalog name, or <see langword="null"/> when tables resolve against the connection's catalog.</returns>
    public static string? GetCatalog(this IReadOnlyEntityType entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        return entityType.FindAnnotation(BricksNetAnnotationNames.Catalog) is { } annotation
            ? (string?)annotation.Value
            : entityType.Model.GetDefaultCatalog();
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
