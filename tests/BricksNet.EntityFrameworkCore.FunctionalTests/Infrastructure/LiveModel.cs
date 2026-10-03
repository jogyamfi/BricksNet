using Microsoft.EntityFrameworkCore;

namespace BricksNet.EntityFrameworkCore.FunctionalTests.Infrastructure;

public sealed class Customer
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? City { get; set; }

    public bool IsActive { get; set; }

    public Address Address { get; set; } = new();

    public List<Order> Orders { get; set; } = [];
}

public sealed class Address
{
    public string Street { get; set; } = string.Empty;

    public string? PostCode { get; set; }
}

public sealed class Order
{
    public int Id { get; set; }

    public int CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public decimal Amount { get; set; }

    public int Quantity { get; set; }

    public DateOnly OrderDate { get; set; }

    public string Status { get; set; } = string.Empty;
}

/// <summary>Rows with crafted values for comparing server-side translations with .NET.</summary>
public sealed class Sample
{
    public int Id { get; set; }

    public string Text { get; set; } = string.Empty;

    public string? Note { get; set; }

    public int Number { get; set; }

    public double Value { get; set; }

    public decimal Amount { get; set; }

    public bool Flag { get; set; }

    public DateTime At { get; set; }

    public DateOnly Day { get; set; }

    public DateTimeOffset Stamp { get; set; }
}

/// <summary>A property of every mapped CLR type, for round trips.</summary>
public sealed class TypeRow
{
    public int Id { get; set; }

    public bool Bool { get; set; }

    public sbyte SByte { get; set; }

    public byte Byte { get; set; }

    public short Short { get; set; }

    public ushort UShort { get; set; }

    public uint UInt { get; set; }

    public long Long { get; set; }

    public ulong ULong { get; set; }

    public float Float { get; set; }

    public double Double { get; set; }

    public decimal Decimal { get; set; }

    public string Text { get; set; } = string.Empty;

    public char Char { get; set; }

    public DateOnly Date { get; set; }

    public DateTime DateTime { get; set; }

    public DateTimeOffset DateTimeOffset { get; set; }

    public TimeOnly TimeOnly { get; set; }

    public TimeSpan TimeSpan { get; set; }

    public byte[] Bytes { get; set; } = [];

    public Guid Guid { get; set; }

    public int? NullableInt { get; set; }

    public string? NullableText { get; set; }
}

/// <summary>Mutable rows for SaveChanges, concurrency and retry tests.</summary>
public sealed class Account
{
    public int Id { get; set; }

    public string Owner { get; set; } = string.Empty;

    public string? Note { get; set; }

    public long Version { get; set; }
}

/// <summary>Rows for multi-row inserts and ExecuteUpdate/ExecuteDelete.</summary>
public sealed class BulkItem
{
    public int Id { get; set; }

    public int GroupId { get; set; }

    public BulkGroup? Group { get; set; }

    public int Value { get; set; }

    public string Status { get; set; } = string.Empty;
}

public sealed class BulkGroup
{
    public int Id { get; set; }

    public string Region { get; set; } = string.Empty;
}

public sealed class LiveContext(DbContextOptions<LiveContext> options, string catalog, string schema) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<Sample> Samples => Set<Sample>();

    public DbSet<TypeRow> TypeRows => Set<TypeRow>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<BulkItem> BulkItems => Set<BulkItem>();

    public DbSet<BulkGroup> BulkGroups => Set<BulkGroup>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // One schema per test run (see LiveDatabaseFixture); EF caches this model for the process.
        modelBuilder.HasDefaultCatalog(catalog).HasDefaultSchema(schema);

        modelBuilder.Entity<Customer>(customer =>
        {
            customer.ToTable("customers");
            customer.OwnsOne(c => c.Address);
            customer.HasMany(c => c.Orders).WithOne(o => o.Customer).HasForeignKey(o => o.CustomerId);
        });
        modelBuilder.Entity<Order>().ToTable("orders");
        modelBuilder.Entity<Sample>().ToTable("samples");
        modelBuilder.Entity<TypeRow>(row =>
        {
            row.ToTable("type_rows");
            row.Property(r => r.Decimal).HasPrecision(18, 4);
        });
        modelBuilder.Entity<Account>(account =>
        {
            account.ToTable("accounts");
            account.Property(a => a.Version).IsConcurrencyToken();
        });
        modelBuilder.Entity<BulkItem>().ToTable("bulk_items");
        modelBuilder.Entity<BulkGroup>().ToTable("bulk_groups");
    }
}
