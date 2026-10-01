using Microsoft.EntityFrameworkCore;

namespace BricksNet.EntityFrameworkCore.TestUtilities;

internal enum Mood
{
    Calm = 1,
    Busy = 2,
}

/// <summary>An entity with a property of every CLR type the provider maps.</summary>
internal sealed class AllTypes
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

    public decimal Money { get; set; }

    public string Text { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string? Note { get; set; }

    public char Char { get; set; }

    public DateOnly Date { get; set; }

    public DateTime DateTime { get; set; }

    public DateTimeOffset DateTimeOffset { get; set; }

    public TimeOnly TimeOnly { get; set; }

    public TimeSpan TimeSpan { get; set; }

    public byte[] Bytes { get; set; } = [];

    public Guid Guid { get; set; }

    public Mood Mood { get; set; }

    public int? NullableInt { get; set; }
}

internal sealed class AllTypesContext(DbContextOptions<AllTypesContext> options) : DbContext(options)
{
    public DbSet<AllTypes> AllTypes => Set<AllTypes>();

    public static AllTypesContext Create(FakeDatabricks databricks) =>
        new(new DbContextOptionsBuilder<AllTypesContext>()
            .UseBricksNet(databricks.CreateConnection(), contextOwnsConnection: true)
            .Options);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AllTypes>();
        entity.ToTable("all_types");
        entity.Property(e => e.Money).HasPrecision(10, 4);
        entity.Property(e => e.Code).HasColumnType("VARCHAR(12)");
        entity.Property(e => e.Note).HasMaxLength(200);
    }
}
