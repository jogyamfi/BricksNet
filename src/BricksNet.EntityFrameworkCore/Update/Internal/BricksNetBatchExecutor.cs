using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;

namespace BricksNet.EntityFrameworkCore.Update.Internal;

/// <summary>
/// Executes <c>SaveChanges</c> command batches one after another without a transaction.
/// EF's default executor wraps multi-batch saves in one, which Databricks SQL cannot provide, so
/// each statement commits on its own and a failure leaves earlier statements committed.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetBatchExecutor : IBatchExecutor
{
    /// <inheritdoc />
    public virtual int Execute(IEnumerable<ModificationCommandBatch> commandBatches, IRelationalConnection connection)
    {
        ArgumentNullException.ThrowIfNull(commandBatches);
        ArgumentNullException.ThrowIfNull(connection);

        var rowsAffected = 0;
        connection.Open();
        try
        {
            foreach (var batch in commandBatches)
            {
                batch.Execute(connection);
                rowsAffected += batch.ModificationCommands.Count;
            }
        }
        finally
        {
            connection.Close();
        }

        return rowsAffected;
    }

    /// <inheritdoc />
    public virtual async Task<int> ExecuteAsync(
        IEnumerable<ModificationCommandBatch> commandBatches,
        IRelationalConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandBatches);
        ArgumentNullException.ThrowIfNull(connection);

        var rowsAffected = 0;
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var batch in commandBatches)
            {
                await batch.ExecuteAsync(connection, cancellationToken).ConfigureAwait(false);
                rowsAffected += batch.ModificationCommands.Count;
            }
        }
        finally
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }

        return rowsAffected;
    }
}
