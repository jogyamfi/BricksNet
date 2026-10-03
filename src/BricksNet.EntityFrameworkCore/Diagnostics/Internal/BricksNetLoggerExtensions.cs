using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace BricksNet.EntityFrameworkCore.Diagnostics.Internal;

/// <summary>Logs the BricksNet provider's diagnostic events, honouring <c>ConfigureWarnings</c>.</summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public static class BricksNetLoggerExtensions
{
    /// <summary>Logs <see cref="BricksNetEventId.UniqueIndexNotEnforced"/>.</summary>
    public static void UniqueIndexNotEnforced(
        this IDiagnosticsLogger<DbLoggerCategory.Model.Validation> diagnostics,
        string entityType,
        string properties)
    {
        var definition = Definition(
            diagnostics,
            static d => ref d.LogUniqueIndexNotEnforced,
            static logger => new EventDefinition<string, string>(
                logger.Options,
                BricksNetEventId.UniqueIndexNotEnforced,
                LogLevel.Warning,
                "BricksNet.UniqueIndexNotEnforced",
                level => LoggerMessage.Define<string, string>(
                    level,
                    BricksNetEventId.UniqueIndexNotEnforced,
                    "The unique index on {EntityType} ({Properties}) is not enforced by Databricks; duplicate values can be inserted.")));

        if (diagnostics.ShouldLog(definition))
        {
            definition.Log(diagnostics, entityType, properties);
        }

        if (diagnostics.NeedsEventData(definition, out var diagnosticSourceEnabled, out var simpleLogEnabled))
        {
            var eventData = new EventData(
                definition,
                (d, _) => ((EventDefinition<string, string>)d).GenerateMessage(entityType, properties));
            diagnostics.DispatchEventData(definition, eventData, diagnosticSourceEnabled, simpleLogEnabled);
        }
    }

    /// <summary>Logs <see cref="BricksNetEventId.NonAtomicSaveChanges"/>.</summary>
    public static void NonAtomicSaveChanges(this IDiagnosticsLogger<DbLoggerCategory.Update> diagnostics)
    {
        var definition = Definition(
            diagnostics,
            static d => ref d.LogNonAtomicSaveChanges,
            static logger => new EventDefinition(
                logger.Options,
                BricksNetEventId.NonAtomicSaveChanges,
                LogLevel.Warning,
                "BricksNet.NonAtomicSaveChanges",
                level => LoggerMessage.Define(
                    level,
                    BricksNetEventId.NonAtomicSaveChanges,
                    "SaveChanges is executing more than one statement. Databricks commits each statement on its own, "
                    + "so if a later statement fails, earlier ones stay committed; their entities are marked as saved.")));

        if (diagnostics.ShouldLog(definition))
        {
            definition.Log(diagnostics);
        }

        if (diagnostics.NeedsEventData(definition, out var diagnosticSourceEnabled, out var simpleLogEnabled))
        {
            var eventData = new EventData(definition, (d, _) => ((EventDefinition)d).GenerateMessage());
            diagnostics.DispatchEventData(definition, eventData, diagnosticSourceEnabled, simpleLogEnabled);
        }
    }

    private delegate ref EventDefinitionBase? DefinitionField(BricksNetLoggingDefinitions definitions);

    // Definitions are created once per logging-definitions instance, as EF's own providers do.
    private static TDefinition Definition<TDefinition>(
        IDiagnosticsLogger logger,
        DefinitionField field,
        Func<IDiagnosticsLogger, TDefinition> create)
        where TDefinition : EventDefinitionBase
    {
        var definitions = (BricksNetLoggingDefinitions)logger.Definitions;
        ref var slot = ref field(definitions);
        if (slot is null)
        {
            // A racing thread may set it first; either definition is equivalent.
            Interlocked.CompareExchange(ref slot, create(logger), null);
        }

        return (TDefinition)slot!;
    }
}
