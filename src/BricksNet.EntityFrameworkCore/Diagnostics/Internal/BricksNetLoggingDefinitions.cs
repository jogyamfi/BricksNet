using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BricksNet.EntityFrameworkCore.Diagnostics.Internal;

/// <summary>Logging definitions for the BricksNet provider's diagnostic events.</summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetLoggingDefinitions : RelationalLoggingDefinitions
{
    /// <summary>The definition of <see cref="BricksNetEventId.UniqueIndexNotEnforced"/>.</summary>
    public EventDefinitionBase? LogUniqueIndexNotEnforced;

    /// <summary>The definition of <see cref="BricksNetEventId.NonAtomicSaveChanges"/>.</summary>
    public EventDefinitionBase? LogNonAtomicSaveChanges;
}
