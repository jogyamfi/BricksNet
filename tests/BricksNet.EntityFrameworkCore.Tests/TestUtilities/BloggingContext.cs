using Microsoft.EntityFrameworkCore;

namespace BricksNet.EntityFrameworkCore.TestUtilities;

internal sealed class Blog
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}

/// <summary>A small model for exercising the provider against <see cref="FakeDatabricks"/>.</summary>
internal sealed class BloggingContext(DbContextOptions<BloggingContext> options) : DbContext(options)
{
    public DbSet<Blog> Blogs => Set<Blog>();

    /// <summary>A context whose connection is served by <paramref name="databricks"/>.</summary>
    public static BloggingContext Create(FakeDatabricks databricks) =>
        new(new DbContextOptionsBuilder<BloggingContext>()
            .UseBricksNet(databricks.CreateConnection(), contextOwnsConnection: true)
            .Options);

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Blog>().ToTable("blogs");
}
