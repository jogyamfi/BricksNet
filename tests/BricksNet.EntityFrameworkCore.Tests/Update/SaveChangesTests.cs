using BricksNet.AdoNet;
using BricksNet.EntityFrameworkCore.Diagnostics;
using BricksNet.EntityFrameworkCore.Query;
using BricksNet.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BricksNet.EntityFrameworkCore.Update;

/// <summary>INSERT/UPDATE/DELETE through SaveChanges, rows-affected checks, non-atomic saves and retries.</summary>
public sealed class SaveChangesTests : QueryTestBase
{
    private sealed class Account
    {
        public int Id { get; set; }

        public string Owner { get; set; } = string.Empty;

        public string? Note { get; set; }

        public long Version { get; set; }
    }

    private sealed class Document
    {
        public Guid Id { get; set; }

        public string Title { get; set; } = string.Empty;
    }

    private static void AccountModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Account>(account =>
        {
            account.ToTable("accounts");
            account.Property(a => a.Version).IsConcurrencyToken();
        });

    private ConfigurableContext CreateAccountContext(
        ILoggerFactory? loggerFactory = null,
        Action<BricksNet.EntityFrameworkCore.Infrastructure.BricksNetDbContextOptionsBuilder>? bricksNetOptions = null,
        Action<DbContextOptionsBuilder>? options = null,
        Action<ModelBuilder>? model = null) =>
        ConfigurableContext.Create(Databricks, model ?? AccountModel, loggerFactory, bricksNetOptions, options);

    private static Account Tracked(DbContext context, int id = 1, long version = 3)
    {
        var account = new Account { Id = id, Owner = "ann", Version = version };
        context.Attach(account);
        return account;
    }

    // ---- generated DML ----

    [Fact]
    public async Task Insert_SendsAllColumnsAsTypedParameters()
    {
        Databricks.Responder = _ => FakeResult.AffectedRows(1);
        await using var context = BloggingContext.Create(Databricks);
        var blog = new Blog { Id = 1, Name = "alpha", IsActive = true };
        context.Blogs.Add(blog);

        Assert.Equal(1, await context.SaveChangesAsync());

        AssertSql(
            """
            INSERT INTO `blogs` (`Id`, `IsActive`, `Name`)
            VALUES (:p0, :p1, :p2)
            """);
        var parameters = Databricks.Statements[0].Parameters;
        Assert.Equal(["p0", "p1", "p2"], parameters.Select(p => p.Name));
        Assert.Equal([1, true, "alpha"], parameters.Select(p => p.Value));
        Assert.Equal(["INT", "BOOLEAN", "STRING"], parameters.Select(p => p.TypeHint));
        Assert.Equal(EntityState.Unchanged, context.Entry(blog).State);
    }

    [Fact]
    public async Task Update_SetsModifiedColumns_AndChecksConcurrencyToken()
    {
        Databricks.Responder = _ => FakeResult.AffectedRows(1);
        await using var context = CreateAccountContext();
        var account = Tracked(context);
        account.Note = "vip";
        account.Version = 4;

        await context.SaveChangesAsync();

        AssertSql(
            """
            UPDATE `accounts` SET `Note` = :p0, `Version` = :p1
            WHERE `Id` = :p2 AND `Version` = :p3
            """);
        var values = Databricks.Statements[0].Parameters.ToDictionary(p => p.Name, p => p.Value);
        Assert.Equal("vip", values["p0"]);
        Assert.Equal(4L, values["p1"]);
        Assert.Equal(1, values["p2"]);
        Assert.Equal(3L, values["p3"]);
    }

    [Fact]
    public async Task Delete_FiltersOnKeyAndConcurrencyToken()
    {
        Databricks.Responder = _ => FakeResult.AffectedRows(1);
        await using var context = CreateAccountContext();
        context.Remove(Tracked(context));

        await context.SaveChangesAsync();

        AssertSql(
            """
            DELETE FROM `accounts`
            WHERE `Id` = :p0 AND `Version` = :p1
            """);
    }

    [Fact]
    public async Task OwnedType_IsSavedInTheOwnersRow()
    {
        Databricks.Responder = _ => FakeResult.AffectedRows(1);
        await using var context = CreateContext();
        context.Customers.Add(new Customer { Id = 7, Name = "Ada", Address = new Address { Street = "Main St" } });

        await context.SaveChangesAsync();

        AssertSql(
            """
            INSERT INTO `customers` (`Id`, `City`, `IsActive`, `Name`, `Address_PostCode`, `Address_Street`)
            VALUES (:p0, :p1, :p2, :p3, :p4, :p5)
            """);
    }

    [Fact]
    public async Task Catalog_QualifiesDml()
    {
        Databricks.Responder = _ => FakeResult.AffectedRows(1);
        await using var context = CreateAccountContext(model: m =>
        {
            m.HasDefaultCatalog("main").HasDefaultSchema("ops");
            AccountModel(m);
        });
        context.Add(new Account { Id = 1, Owner = "ann" });

        await context.SaveChangesAsync();

        Assert.StartsWith("INSERT INTO `main`.`ops`.`accounts` (", Databricks.Statements[0].Sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GuidKey_IsGeneratedInDotNet_AndSentAsString()
    {
        Databricks.Responder = _ => FakeResult.AffectedRows(1);
        await using var context = CreateAccountContext(model: m => m.Entity<Document>().ToTable("documents"));
        var document = new Document { Title = "spec" };
        context.Add(document);

        await context.SaveChangesAsync();

        var key = Databricks.Statements[0].Parameters[0];
        Assert.Equal(document.Id, key.Value);
        Assert.Equal("STRING", key.TypeHint);
        Assert.Equal(7, document.Id.Version);
    }

    // ---- rows affected ----

    [Fact]
    public async Task NoRowsAffected_IsAConcurrencyConflict()
    {
        Databricks.Responder = _ => FakeResult.AffectedRows(0);
        await using var context = CreateAccountContext();
        var account = Tracked(context);
        account.Note = "stale";

        var ex = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync());

        Assert.Same(account, Assert.Single(ex.Entries).Entity);
    }

    // ---- non-atomic saves ----

    [Fact]
    public async Task MultipleChanges_RunOneStatementEach_AndWarnOnce()
    {
        Databricks.Responder = _ => FakeResult.AffectedRows(1);
        using var provider = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(provider));
        await using var context = CreateAccountContext(loggerFactory);
        context.AddRange(new Account { Id = 1, Owner = "a" }, new Account { Id = 2, Owner = "b" }, new Account { Id = 3, Owner = "c" });

        Assert.Equal(3, await context.SaveChangesAsync());

        Assert.Equal(3, Databricks.Statements.Count);
        Assert.Single(provider.Messages, m => m.Level == LogLevel.Warning && m.Message.Contains("more than one statement", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NonAtomicWarning_CanBeIgnored()
    {
        Databricks.Responder = _ => FakeResult.AffectedRows(1);
        using var provider = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(provider));
        await using var context = CreateAccountContext(
            loggerFactory,
            options: o => o.ConfigureWarnings(w => w.Ignore(BricksNetEventId.NonAtomicSaveChanges)));
        context.AddRange(new Account { Id = 1, Owner = "a" }, new Account { Id = 2, Owner = "b" });

        await context.SaveChangesAsync();

        Assert.DoesNotContain(provider.Messages, m => m.Message.Contains("more than one statement", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailedStatement_LeavesEarlierOnesCommitted_AndSaved()
    {
        var fail = true;
        Databricks.Responder = s => fail && s.Parameters.Any(p => Equals(p.Value, "b"))
            ? throw new BricksNetException("[DIVIDE_BY_ZERO] boom", null, 0, null)
            : FakeResult.AffectedRows(1);
        await using var context = CreateAccountContext();
        var first = new Account { Id = 1, Owner = "a" };
        var second = new Account { Id = 2, Owner = "b" };
        context.AddRange(first, second);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        // The first INSERT committed, so its entity is saved; the second is still pending.
        Assert.Equal(EntityState.Unchanged, context.Entry(first).State);
        Assert.Equal(EntityState.Added, context.Entry(second).State);

        fail = false;
        await context.SaveChangesAsync();

        Assert.Equal(3, Databricks.Statements.Count);
        Assert.Contains(Databricks.Statements[2].Parameters, p => Equals(p.Value, "b"));
    }

    // ---- retries ----

    [Fact]
    public async Task TransientFailure_RetriesOnlyTheFailedStatement()
    {
        var attempts = 0;
        Databricks.Responder = s => s.Parameters.Any(p => Equals(p.Value, "b")) && ++attempts == 1
            ? throw new BricksNetException("[DELTA_CONCURRENT_APPEND] conflict", null, 0, null)
            : FakeResult.AffectedRows(1);
        await using var context = CreateAccountContext(bricksNetOptions: o => o.EnableRetryOnFailure(3, TimeSpan.Zero));
        context.AddRange(new Account { Id = 1, Owner = "a" }, new Account { Id = 2, Owner = "b" });

        Assert.Equal(2, await context.SaveChangesAsync());

        // a, b (failed), b (retried): the committed first statement is not repeated.
        Assert.Equal(["a", "b", "b"], Databricks.Statements.Select(s => s.Parameters.Single(p => p.Value is "a" or "b").Value));
    }

    [Fact]
    public async Task TransientFailure_GivesUpAfterMaxRetries()
    {
        Databricks.Responder = _ => throw new BricksNetException("unavailable", null, 503, null);
        await using var context = CreateAccountContext(bricksNetOptions: o => o.EnableRetryOnFailure(2, TimeSpan.Zero));
        context.Add(new Account { Id = 1, Owner = "a" });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Equal(3, Databricks.Statements.Count);
    }

    [Fact]
    public async Task WithoutRetryOnFailure_TransientFailuresAreNotRetried()
    {
        Databricks.Responder = _ => throw new BricksNetException("unavailable", null, 503, null);
        await using var context = CreateAccountContext();
        context.Add(new Account { Id = 1, Owner = "a" });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Single(Databricks.Statements);
    }

    [Fact]
    public async Task ConcurrencyConflicts_AreNotRetried()
    {
        Databricks.Responder = _ => FakeResult.AffectedRows(0);
        await using var context = CreateAccountContext(bricksNetOptions: o => o.EnableRetryOnFailure(3, TimeSpan.Zero));
        Tracked(context).Note = "x";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync());

        Assert.Single(Databricks.Statements);
    }

    [Fact]
    public async Task Queries_AndWrappedRawSql_AreRetriedByTheExecutionStrategy()
    {
        var failures = 2;
        Databricks.Responder = s => failures-- > 0
            ? throw new BricksNetException("throttled", null, 429, null)
            : s.Sql.StartsWith("SELECT", StringComparison.Ordinal)
                ? FakeResult.Table([("Id", "INT"), ("Note", "STRING"), ("Owner", "STRING"), ("Version", "BIGINT")], [1, null, "ann", 3L])
                : FakeResult.AffectedRows(5);
        await using var context = CreateAccountContext(bricksNetOptions: o => o.EnableRetryOnFailure(3, TimeSpan.Zero));

        var accounts = await context.Set<Account>().ToListAsync();
        failures = 1;
        // EF does not route ExecuteSql through the execution strategy, so it is wrapped explicitly.
        var affected = await context.Database.CreateExecutionStrategy().ExecuteAsync(
            () => context.Database.ExecuteSqlAsync($"DELETE FROM accounts WHERE Version < {10}"));

        Assert.Equal("ann", Assert.Single(accounts).Owner);
        Assert.Equal(5, affected);
        Assert.Equal(5, Databricks.Statements.Count);
    }

    [Fact]
    public void BeginTransaction_StillThrows_WithSaveChangesSupported()
    {
        using var context = CreateAccountContext();

        Assert.Throws<NotSupportedException>(() => context.Database.BeginTransaction());
    }
}
