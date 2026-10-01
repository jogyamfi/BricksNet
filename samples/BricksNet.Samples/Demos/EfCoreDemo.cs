using BricksNet.Contracts.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BricksNet.Samples.Demos;

/// <summary>
/// Demonstrates the BricksNet EF Core provider: creating Delta tables, SaveChanges (with a multi-row
/// insert), LINQ queries, optimistic concurrency and ExecuteUpdate/ExecuteDelete.
/// </summary>
/// <remarks>
/// Creates the schema <c>bricksnet_efcore_demo</c> in the configured catalog (<c>Databricks:Catalog</c>)
/// and drops its tables at the end.
/// </remarks>
public static class EfCoreDemo
{
    private const string DemoSchema = "bricksnet_efcore_demo";

    public static async Task RunAsync(IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var host = config["Databricks:Host"] ?? throw new InvalidOperationException("Databricks:Host is not configured.");
        var httpPath = config["Databricks:HttpPath"] ?? throw new InvalidOperationException("Databricks:HttpPath is not configured.");
        var token = config["Databricks:Token"] ?? throw new InvalidOperationException("Databricks:Token is not configured.");
        var catalog = config["Databricks:Catalog"] ?? throw new InvalidOperationException("Databricks:Catalog is not configured.");

        var options = new DbContextOptionsBuilder<StoreContext>()
            .UseBricksNet(
                new BricksNetClientOptions { Host = host, HttpPath = httpPath },
                token,
                o => o.EnableRetryOnFailure())
            .Options;

        await using var db = new StoreContext(options, catalog);

        Console.WriteLine($"[EfCore] Creating {catalog}.{DemoSchema} and its Delta tables...");
        await db.Database.EnsureCreatedAsync();

        try
        {
            // Consecutive inserts into one table become a single multi-row INSERT.
            var books = Enumerable.Range(1, 10)
                .Select(i => new Book { Id = Guid.CreateVersion7(), Title = $"Book {i}", Price = 9.99m + i, Published = new DateOnly(2020 + (i % 6), 1, 1) })
                .ToList();
            db.Books.AddRange(books);
            Console.WriteLine($"[EfCore] SaveChanges inserted {await db.SaveChangesAsync()} rows.");

            var recent = await db.Books
                .Where(b => b.Published.Year >= 2023 && b.Title.Contains("Book"))
                .OrderByDescending(b => b.Price)
                .Take(3)
                .Select(b => new { b.Title, b.Price })
                .ToListAsync();
            Console.WriteLine("[EfCore] Three most expensive books published since 2023:");
            recent.ForEach(b => Console.WriteLine($"  {b.Title}: {b.Price}"));

            var byYear = await db.Books
                .GroupBy(b => b.Published.Year)
                .Select(g => new { Year = g.Key, Count = g.Count(), Average = g.Average(b => b.Price) })
                .OrderBy(x => x.Year)
                .ToListAsync();
            Console.WriteLine("[EfCore] Books per year:");
            byYear.ForEach(y => Console.WriteLine($"  {y.Year}: {y.Count} (average {y.Average:F2})"));

            // Optimistic concurrency with a client-managed token: Databricks reports the rows affected.
            var book = await db.Books.OrderBy(b => b.Title).FirstAsync();
            book.Price += 1;
            book.Version++;
            await db.SaveChangesAsync();
            Console.WriteLine($"[EfCore] Updated '{book.Title}' to version {book.Version}.");

            var discounted = await db.Books
                .Where(b => b.Price > 15)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.Price, b => Math.Round(b.Price * 0.9m, 2)));
            Console.WriteLine($"[EfCore] ExecuteUpdate discounted {discounted} books.");

            var removed = await db.Books.Where(b => b.Published.Year < 2022).ExecuteDeleteAsync();
            Console.WriteLine($"[EfCore] ExecuteDelete removed {removed} books.");
        }
        finally
        {
            Console.WriteLine("[EfCore] Dropping the demo tables...");
            await db.Database.EnsureDeletedAsync();
        }

        Console.WriteLine("[EfCore] Done.");
    }

    private sealed class Book
    {
        public Guid Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public decimal Price { get; set; }

        public DateOnly Published { get; set; }

        public long Version { get; set; }
    }

    private sealed class StoreContext(DbContextOptions<StoreContext> options, string catalog) : DbContext(options)
    {
        public DbSet<Book> Books => Set<Book>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultCatalog(catalog).HasDefaultSchema(DemoSchema);
            modelBuilder.Entity<Book>(book =>
            {
                book.ToTable("books");
                book.Property(b => b.Price).HasPrecision(10, 2);
                book.Property(b => b.Version).IsConcurrencyToken();
            });
        }
    }
}
