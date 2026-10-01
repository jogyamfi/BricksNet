using BricksNet.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BricksNet.EntityFrameworkCore.Metadata;

/// <summary>Conventions and validation that keep models within what Databricks supports.</summary>
public sealed class ModelValidationTests
{
    private sealed class Item
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public long Version { get; set; }

        public byte[] RowVersion { get; set; } = [];

        public decimal Price { get; set; }

        public List<Tag> Tags { get; set; } = [];
    }

    private sealed class Tag
    {
        public string Label { get; set; } = string.Empty;
    }

    private sealed class Document
    {
        public Guid Id { get; set; }

        public string Title { get; set; } = string.Empty;
    }

    private sealed class Line
    {
        public int OrderId { get; set; }

        public int LineNumber { get; set; }
    }

    private sealed class Summary
    {
        public string Status { get; set; } = string.Empty;

        public int Count { get; set; }
    }

    private sealed class SequenceGenerator : ValueGenerator<int>
    {
        private int _next;

        public override bool GeneratesTemporaryValues => false;

        public override int Next(EntityEntry entry) => Interlocked.Increment(ref _next);
    }

    private static IModel BuildModel(Action<ModelBuilder> configure, ILoggerFactory? loggerFactory = null)
    {
        using var context = ConfigurableContext.Create(new FakeDatabricks(), configure, loggerFactory);
        return context.Model;
    }

    private static InvalidOperationException AssertRejected(Action<ModelBuilder> configure, params string[] expectedFragments)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => BuildModel(configure));
        foreach (var fragment in expectedFragments)
        {
            Assert.Contains(fragment, ex.Message, StringComparison.Ordinal);
        }

        return ex;
    }

    private static void ItemOnly(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Item>().Ignore(i => i.Tags).Ignore(i => i.RowVersion);
    }

    // ---- conventions ----

    [Fact]
    public void IntegerKeys_AreNotDatabaseGenerated()
    {
        var model = BuildModel(m =>
        {
            ItemOnly(m);
            m.Entity<Line>().HasKey(l => new { l.OrderId, l.LineNumber });
        });

        Assert.Equal(ValueGenerated.Never, model.FindEntityType(typeof(Item))!.FindProperty(nameof(Item.Id))!.ValueGenerated);
        Assert.All(model.FindEntityType(typeof(Line))!.GetProperties(), p => Assert.Equal(ValueGenerated.Never, p.ValueGenerated));
    }

    [Fact]
    public void GuidKeys_AreGeneratedInDotNet_AsVersion7()
    {
        using var context = ConfigurableContext.Create(new FakeDatabricks(), m => m.Entity<Document>());
        var document = new Document { Title = "a" };

        context.Add(document);

        Assert.Equal(ValueGenerated.OnAdd, context.Model.FindEntityType(typeof(Document))!.FindPrimaryKey()!.Properties[0].ValueGenerated);
        Assert.Equal(7, document.Id.Version);
        Assert.False(context.Entry(document).Property(d => d.Id).IsTemporary);
    }

    // ---- rejected configuration ----

    [Fact]
    public void DefaultValueSql_IsRejected() => AssertRejected(
        m => { ItemOnly(m); m.Entity<Item>().Property(i => i.Name).HasDefaultValueSql("'x'"); },
        "Item.Name", "HasDefaultValueSql", "set the value in .NET");

    [Fact]
    public void DefaultValue_IsRejected() => AssertRejected(
        m => { ItemOnly(m); m.Entity<Item>().Property(i => i.Name).HasDefaultValue("x"); },
        "Item.Name", "HasDefaultValue");

    [Fact]
    public void ComputedColumn_IsRejected() => AssertRejected(
        m => { ItemOnly(m); m.Entity<Item>().Property(i => i.Version).HasComputedColumnSql("1"); },
        "Item.Version", "HasComputedColumnSql");

    [Fact]
    public void RowVersion_IsRejected() => AssertRejected(
        m => { m.Entity<Item>().Ignore(i => i.Tags).Property(i => i.RowVersion).IsRowVersion(); },
        "Item.RowVersion", "IsRowVersion", "IsConcurrencyToken");

    [Fact]
    public void ExplicitIdentityKey_IsRejected() => AssertRejected(
        m => { ItemOnly(m); m.Entity<Item>().Property(i => i.Id).ValueGeneratedOnAdd(); },
        "Item.Id", "ValueGeneratedOnAdd");

    [Fact]
    public void OwnsMany_WithGeneratedShadowKey_IsRejected() => AssertRejected(
        m => m.Entity<Item>().Ignore(i => i.RowVersion).OwnsMany(i => i.Tags),
        "Tag", "OwnsMany");

    [Fact]
    public void Sequence_IsRejected() => AssertRejected(
        m => { ItemOnly(m); m.HasSequence<int>("item_ids"); },
        "item_ids", "no sequences");

    [Fact]
    public void DecimalPrecisionAbove38_IsRejected() => AssertRejected(
        m => { ItemOnly(m); m.Entity<Item>().Property(i => i.Price).HasPrecision(40, 2); },
        "Item.Price", "at most 38");

    [Fact]
    public void Catalog_WithoutSchema_IsRejected() => AssertRejected(
        m => { ItemOnly(m); m.HasDefaultCatalog("main"); },
        "main", "no schema");

    // ---- accepted configuration ----

    [Fact]
    public void ClientManagedConfiguration_IsAccepted()
    {
        var model = BuildModel(m =>
        {
            m.HasDefaultCatalog("main").HasDefaultSchema("sales");
            var item = m.Entity<Item>();
            item.Ignore(i => i.Tags).Ignore(i => i.RowVersion);
            item.Property(i => i.Version).IsConcurrencyToken();
            item.Property(i => i.Id).ValueGeneratedOnAdd().HasValueGenerator<SequenceGenerator>();
            m.Entity<Summary>().HasNoKey().ToView("order_summary");
        });

        Assert.True(model.FindEntityType(typeof(Item))!.FindProperty(nameof(Item.Version))!.IsConcurrencyToken);
    }

    [Fact]
    public void UniqueIndex_LogsAWarning()
    {
        using var provider = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(provider));

        BuildModel(
            m => { ItemOnly(m); m.Entity<Item>().HasIndex(i => i.Name).IsUnique(); },
            loggerFactory);

        Assert.Contains(
            provider.Messages,
            m => m.Level == LogLevel.Warning && m.Message.Contains("unique index", StringComparison.Ordinal)
                && m.Message.Contains("Name", StringComparison.Ordinal));
    }
}
