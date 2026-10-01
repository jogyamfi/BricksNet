using BricksNet.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BricksNet.EntityFrameworkCore;

/// <summary>The SQL the provider sends, checked against <see cref="FakeDatabricks"/>.</summary>
public sealed class RawSqlAndQueryTests
{
    private static readonly (string, string)[] BlogColumns = [("Id", "INT"), ("IsActive", "BOOLEAN"), ("Name", "STRING")];

    [Fact]
    public async Task ExecuteSqlInterpolated_SendsNamedParameters_AndReturnsRowsAffected()
    {
        var databricks = new FakeDatabricks { Responder = _ => FakeResult.AffectedRows(3) };
        await using var context = BloggingContext.Create(databricks);
        var name = "it's";
        var id = 7;

        var affected = await context.Database.ExecuteSqlAsync($"UPDATE blogs SET name = {name} WHERE id > {id}");

        Assert.Equal(3, affected);
        var statement = Assert.Single(databricks.Statements);
        Assert.Equal("UPDATE blogs SET name = :p0 WHERE id > :p1", statement.Sql);
        Assert.Equal(["p0", "p1"], statement.Parameters.Select(p => p.Name));
        Assert.Equal(["it's", 7], statement.Parameters.Select(p => p.Value));
        Assert.Equal(["STRING", "INT"], statement.Parameters.Select(p => p.TypeHint));
    }

    [Fact]
    public async Task SqlQueryRaw_ReadsScalarValues()
    {
        var databricks = new FakeDatabricks { Responder = _ => FakeResult.Table([("Value", "INT")], [1], [2]) };
        await using var context = BloggingContext.Create(databricks);

        var values = await context.Database.SqlQueryRaw<int>("SELECT id AS Value FROM blogs").ToListAsync();

        Assert.Equal([1, 2], values);
        Assert.Equal("SELECT id AS Value FROM blogs", Assert.Single(databricks.Statements).Sql.TrimEnd());
    }

    [Fact]
    public async Task FromSqlRaw_MaterialisesEntities()
    {
        var databricks = new FakeDatabricks
        {
            Responder = _ => FakeResult.Table(BlogColumns, [1, true, "alpha"], [2, false, "beta"]),
        };
        await using var context = BloggingContext.Create(databricks);

        var blogs = await context.Blogs.FromSqlRaw("SELECT * FROM blogs").AsNoTracking().ToListAsync();

        Assert.Equal(["alpha", "beta"], blogs.Select(b => b.Name));
        Assert.Equal([true, false], blogs.Select(b => b.IsActive));
    }

    [Fact]
    public async Task Linq_Where_UsesBacktickIdentifiers_AndNamedParameters()
    {
        var databricks = new FakeDatabricks { Responder = _ => FakeResult.Table(BlogColumns, [5, true, "gamma"]) };
        await using var context = BloggingContext.Create(databricks);
        var minId = 4;

        var blogs = await context.Blogs.Where(b => b.Id > minId && b.IsActive).ToListAsync();

        Assert.Equal("gamma", Assert.Single(blogs).Name);
        var statement = Assert.Single(databricks.Statements);
        Assert.Equal(
            """
            SELECT `b`.`Id`, `b`.`IsActive`, `b`.`Name`
            FROM `blogs` AS `b`
            WHERE `b`.`Id` > :minId AND `b`.`IsActive`
            """.ReplaceLineEndings("\n"),
            statement.Sql.ReplaceLineEndings("\n"));
        Assert.Equal(["minId"], statement.Parameters.Select(p => p.Name));
    }

    [Fact]
    public async Task Linq_StringConstant_IsEscapedForDatabricks()
    {
        var databricks = new FakeDatabricks { Responder = _ => FakeResult.Table(BlogColumns) };
        await using var context = BloggingContext.Create(databricks);

        await context.Blogs.Where(b => b.Name == @"it's a \ test").ToListAsync();

        Assert.EndsWith(
            @"WHERE `b`.`Name` = 'it\'s a \\ test'",
            Assert.Single(databricks.Statements).Sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Queries_ReuseOneSession_AcrossConnectionOpenClose()
    {
        var databricks = new FakeDatabricks { Responder = _ => FakeResult.Table([("Value", "INT")], [1]) };
        await using var context = new BloggingContext(new DbContextOptionsBuilder<BloggingContext>()
            .UseBricksNet(databricks.CreateConnection(), contextOwnsConnection: true, o => o.KeepSessionAlive())
            .Options);

        await context.Database.SqlQueryRaw<int>("SELECT 1 AS Value").ToListAsync();
        await context.Database.SqlQueryRaw<int>("SELECT 1 AS Value").ToListAsync();

        Assert.Equal(2, databricks.Statements.Count);
        Assert.Equal(1, databricks.SessionsOpened);
    }
}
