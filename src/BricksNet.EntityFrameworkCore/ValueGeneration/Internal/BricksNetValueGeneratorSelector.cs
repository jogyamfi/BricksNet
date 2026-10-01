using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace BricksNet.EntityFrameworkCore.ValueGeneration.Internal;

/// <summary>
/// Selects value generators for client-generated keys. <see cref="Guid"/> keys get time-ordered
/// version 7 GUIDs, so rows inserted together cluster together in Delta files.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetValueGeneratorSelector : RelationalValueGeneratorSelector
{
    /// <summary>Initialises a new instance.</summary>
    public BricksNetValueGeneratorSelector(ValueGeneratorSelectorDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <inheritdoc />
    protected override ValueGenerator? FindForType(IProperty property, ITypeBase typeBase, Type clrType) =>
        clrType == typeof(Guid)
            ? new GuidV7ValueGenerator()
            : base.FindForType(property, typeBase, clrType);
}
