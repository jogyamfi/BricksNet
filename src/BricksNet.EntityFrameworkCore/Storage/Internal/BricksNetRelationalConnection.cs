using System.Data;
using System.Data.Common;
using Transaction = System.Transactions.Transaction;
using BricksNet.AdoNet;
using BricksNet.EntityFrameworkCore.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// EF Core's connection to Databricks SQL over a <see cref="BricksNetConnection"/>. Databricks
/// has no multi-statement transactions, so every way of starting or joining one throws
/// <see cref="NotSupportedException"/>.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetRelationalConnection : RelationalConnection
{
    internal const string TransactionsNotSupportedMessage =
        "Databricks SQL does not support multi-statement transactions, so the BricksNet provider cannot "
        + "begin, use or enlist in one. Each statement commits on its own; SaveChanges is therefore not "
        + "atomic across the statements it executes.";

    private readonly BricksNetOptionsExtension? _extension;

    /// <summary>Initialises a new instance.</summary>
    public BricksNetRelationalConnection(RelationalConnectionDependencies dependencies)
        : base(dependencies)
    {
        _extension = dependencies.ContextOptions.FindExtension<BricksNetOptionsExtension>();

        // A connection supplied by the application keeps its own setting unless one was configured explicitly.
        if (_extension is { Connection: BricksNetConnection external, KeepSessionAlive: { } keepSessionAlive })
        {
            external.KeepSessionAlive = keepSessionAlive;
        }
    }

    /// <inheritdoc />
    protected override DbConnection CreateDbConnection()
    {
        BricksNetConnection connection;
        bool connectionStringSetsKeepAlive;
        if (_extension?.ClientOptions is { } clientOptions)
        {
            connection = new BricksNetConnection(clientOptions, _extension.AccessToken);
            connectionStringSetsKeepAlive = false;
        }
        else
        {
            var connectionString = GetValidatedConnectionString();
            connection = new BricksNetConnection(connectionString);
            connectionStringSetsKeepAlive = new BricksNetConnectionStringBuilder(connectionString)
                .ContainsKey(nameof(BricksNetConnection.KeepSessionAlive));
        }

        // EF opens and closes the connection around every operation; keeping the session alive
        // avoids paying for a new Databricks session each time. An explicit setting wins.
        connection.KeepSessionAlive = _extension?.KeepSessionAlive ?? (!connectionStringSetsKeepAlive || connection.KeepSessionAlive);
        return connection;
    }

    /// <summary>Always throws: Databricks SQL has no multi-statement transactions.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override IDbContextTransaction BeginTransaction() => throw TransactionsNotSupported();

    /// <summary>Always throws: Databricks SQL has no multi-statement transactions.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override IDbContextTransaction BeginTransaction(IsolationLevel isolationLevel) => throw TransactionsNotSupported();

    /// <summary>Always throws: Databricks SQL has no multi-statement transactions.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        throw TransactionsNotSupported();

    /// <summary>Always throws: Databricks SQL has no multi-statement transactions.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override Task<IDbContextTransaction> BeginTransactionAsync(
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken = default) =>
        throw TransactionsNotSupported();

    /// <summary>Throws unless <paramref name="transaction"/> is <see langword="null"/>, which clears nothing.</summary>
    /// <exception cref="NotSupportedException">A transaction is supplied.</exception>
    public override IDbContextTransaction? UseTransaction(DbTransaction? transaction) =>
        transaction is null ? base.UseTransaction(transaction) : throw TransactionsNotSupported();

    /// <summary>Throws unless <paramref name="transaction"/> is <see langword="null"/>, which clears nothing.</summary>
    /// <exception cref="NotSupportedException">A transaction is supplied.</exception>
    public override IDbContextTransaction? UseTransaction(DbTransaction? transaction, Guid transactionId) =>
        transaction is null ? base.UseTransaction(transaction, transactionId) : throw TransactionsNotSupported();

    /// <summary>Throws unless <paramref name="transaction"/> is <see langword="null"/>, which clears nothing.</summary>
    /// <exception cref="NotSupportedException">A transaction is supplied.</exception>
    public override Task<IDbContextTransaction?> UseTransactionAsync(
        DbTransaction? transaction,
        CancellationToken cancellationToken = default) =>
        transaction is null ? base.UseTransactionAsync(transaction, cancellationToken) : throw TransactionsNotSupported();

    /// <summary>Throws unless <paramref name="transaction"/> is <see langword="null"/>, which clears nothing.</summary>
    /// <exception cref="NotSupportedException">A transaction is supplied.</exception>
    public override Task<IDbContextTransaction?> UseTransactionAsync(
        DbTransaction? transaction,
        Guid transactionId,
        CancellationToken cancellationToken = default) =>
        transaction is null
            ? base.UseTransactionAsync(transaction, transactionId, cancellationToken)
            : throw TransactionsNotSupported();

    /// <summary>Throws unless <paramref name="transaction"/> is <see langword="null"/>, which clears nothing.</summary>
    /// <exception cref="NotSupportedException">A transaction is supplied.</exception>
    public override void EnlistTransaction(Transaction? transaction)
    {
        if (transaction is not null)
        {
            throw TransactionsNotSupported();
        }

        base.EnlistTransaction(transaction);
    }

    private static NotSupportedException TransactionsNotSupported() => new(TransactionsNotSupportedMessage);
}
