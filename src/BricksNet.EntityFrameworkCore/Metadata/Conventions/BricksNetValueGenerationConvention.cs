using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace BricksNet.EntityFrameworkCore.Metadata.Conventions;

/// <summary>
/// Stops EF from treating integer keys as database-generated. Databricks cannot return generated
/// values (there is no <c>RETURNING</c> or <c>OUTPUT</c>), so keys must be set in .NET. Key types EF
/// generates on the client — <see cref="Guid"/>, <see cref="string"/> and <c>byte[]</c> — keep
/// value generation.
/// </summary>
public class BricksNetValueGenerationConvention : RelationalValueGenerationConvention
{
    /// <summary>Initialises a new instance.</summary>
    public BricksNetValueGenerationConvention(
        ProviderConventionSetBuilderDependencies dependencies,
        RelationalConventionSetBuilderDependencies relationalDependencies)
        : base(dependencies, relationalDependencies)
    {
    }

    /// <summary>The CLR types EF generates key values for in .NET rather than in the database.</summary>
    internal static bool IsClientGenerated(Type clrType)
    {
        var type = Nullable.GetUnderlyingType(clrType) ?? clrType;
        return type == typeof(Guid) || type == typeof(string) || type == typeof(byte[]);
    }

    /// <inheritdoc />
    protected override ValueGenerated? GetValueGenerated(IConventionProperty property)
    {
        var valueGenerated = base.GetValueGenerated(property);

        // Keep explicit store defaults and computed columns, so model validation can report them.
        return valueGenerated == ValueGenerated.OnAdd
            && !IsClientGenerated(property.ClrType)
            && property.GetDefaultValueSql() is null
            && property.GetComputedColumnSql() is null
            && !property.TryGetDefaultValue(out _)
                ? null
                : valueGenerated;
    }
}
