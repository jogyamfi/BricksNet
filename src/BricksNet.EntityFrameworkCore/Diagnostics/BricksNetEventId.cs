using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace BricksNet.EntityFrameworkCore.Diagnostics;

/// <summary>
/// Event ids for warnings the BricksNet provider logs. Use them with
/// <c>ConfigureWarnings</c>, for example to ignore or throw on an event.
/// </summary>
public static class BricksNetEventId
{
    // EF reserves ids from CoreEventId.ProviderBaseId upwards for database providers.
    private enum Id
    {
        UniqueIndexNotEnforced = CoreEventId.ProviderBaseId,
        NonAtomicSaveChanges = CoreEventId.ProviderBaseId + 100,
    }

    /// <summary>
    /// A unique index is configured, but Databricks does not enforce uniqueness, so duplicates can be inserted.
    /// </summary>
    public static readonly EventId UniqueIndexNotEnforced =
        new((int)Id.UniqueIndexNotEnforced, DbLoggerCategory.Model.Validation.Name + "." + nameof(Id.UniqueIndexNotEnforced));

    /// <summary>
    /// <c>SaveChanges</c> is executing more than one statement. Databricks has no multi-statement
    /// transactions, so each statement commits on its own: if a later statement fails, earlier ones
    /// stay committed.
    /// </summary>
    public static readonly EventId NonAtomicSaveChanges =
        new((int)Id.NonAtomicSaveChanges, DbLoggerCategory.Update.Name + "." + nameof(Id.NonAtomicSaveChanges));
}
