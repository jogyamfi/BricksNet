using Microsoft.EntityFrameworkCore.Update;

namespace BricksNet.EntityFrameworkCore.Update.Internal;

/// <summary>
/// A <c>SaveChanges</c> batch that is always a single statement, as the SQL Statement Execution API
/// requires. Consecutive inserts into the same table with the same columns are combined into one
/// multi-row <c>INSERT ... VALUES (...), (...)</c>, which saves a round trip per row and commits the
/// rows atomically. Updates and deletes run one per batch.
/// </summary>
/// <remarks>
/// <para>
/// All rows of a combined insert share one <c>num_affected_rows</c> result, which EF checks against
/// the number of rows (every command but the last is mapped as <c>NotLastInResultSet</c>).
/// </para>
/// <para>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </para>
/// </remarks>
public class BricksNetModificationCommandBatch : AffectedCountModificationCommandBatch
{
    /// <summary>The most parameters a combined insert may bind.</summary>
    public const int MaxParameters = 256;

    private const ResultSetMapping LastRow = ResultSetMapping.LastInResultSet | ResultSetMapping.ResultSetWithRowsAffectedOnly;
    private const ResultSetMapping EarlierRow = ResultSetMapping.NotLastInResultSet | ResultSetMapping.ResultSetWithRowsAffectedOnly;

    /// <summary>Initialises a new instance.</summary>
    /// <param name="dependencies">Service dependencies.</param>
    /// <param name="maxBatchSize">The most rows a combined insert may contain.</param>
    public BricksNetModificationCommandBatch(ModificationCommandBatchFactoryDependencies dependencies, int maxBatchSize)
        : base(dependencies, maxBatchSize)
    {
    }

    /// <inheritdoc />
    public override bool TryAddCommand(IReadOnlyModificationCommand modificationCommand)
    {
        ArgumentNullException.ThrowIfNull(modificationCommand);

        if (ModificationCommands.Count > 0)
        {
            // Only further rows of the same INSERT join a non-empty batch, within the parameter budget.
            var parameterCount = modificationCommand.ColumnModifications.Count(o => o.UseParameter);
            if (UpdateSqlGenerator is not BricksNetUpdateSqlGenerator
                || !BricksNetUpdateSqlGenerator.CanShareInsert(ModificationCommands[0], modificationCommand)
                || ParameterValues.Count + parameterCount > MaxParameters)
            {
                return false;
            }
        }

        return base.TryAddCommand(modificationCommand);
    }

    /// <inheritdoc />
    protected override void AddCommand(IReadOnlyModificationCommand modificationCommand)
    {
        ArgumentNullException.ThrowIfNull(modificationCommand);

        if (ModificationCommands.Count == 0)
        {
            base.AddCommand(modificationCommand);
            return;
        }

        // Continue the INSERT: drop the statement's trailing line break, then append ", (values)".
        while (SqlBuilder.Length > 0 && char.IsWhiteSpace(SqlBuilder[^1]))
        {
            SqlBuilder.Length--;
        }

        SqlBuilder.Append(',').AppendLine();
        ((BricksNetUpdateSqlGenerator)UpdateSqlGenerator).AppendInsertValuesRow(SqlBuilder, modificationCommand);
        SqlBuilder.AppendLine();

        ResultSetMappings[^1] = EarlierRow;
        ResultSetMappings.Add(LastRow);
        AddParameters(modificationCommand);
    }

    /// <inheritdoc />
    protected override void RollbackLastCommand(IReadOnlyModificationCommand modificationCommand)
    {
        base.RollbackLastCommand(modificationCommand);

        // The row before the rolled-back one is the last again.
        if (ResultSetMappings.Count > 0)
        {
            ResultSetMappings[^1] = LastRow;
        }
    }
}
