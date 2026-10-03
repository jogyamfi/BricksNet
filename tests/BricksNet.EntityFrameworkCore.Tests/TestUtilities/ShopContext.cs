using Microsoft.EntityFrameworkCore;

namespace BricksNet.EntityFrameworkCore.TestUtilities;

internal sealed class Customer
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? City { get; set; }

    public bool IsActive { get; set; }

    public Address Address { get; set; } = new();

    public List<Order> Orders { get; set; } = [];
}

internal sealed class Address
{
    public string Street { get; set; } = string.Empty;

    public string? PostCode { get; set; }
}

internal sealed class Order
{
    public int Id { get; set; }

    public int CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public decimal Amount { get; set; }

    public int Quantity { get; set; }

    public DateOnly OrderDate { get; set; }

    public string Status { get; set; } = string.Empty;
}

/// <summary>A small relational model (one-to-many, owned type) for query translation tests.</summary>
internal sealed class ShopContext(DbContextOptions<ShopContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Order> Orders => Set<Order>();

    public static ShopContext Create(FakeDatabricks databricks) =>
        new(new DbContextOptionsBuilder<ShopContext>()
            .UseBricksNet(databricks.CreateConnection(), contextOwnsConnection: true)
            .Options);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(customer =>
        {
            customer.ToTable("customers");
            customer.OwnsOne(c => c.Address);
            customer.HasMany(c => c.Orders).WithOne(o => o.Customer).HasForeignKey(o => o.CustomerId);
        });

        modelBuilder.Entity<Order>().ToTable("orders");
    }
}
