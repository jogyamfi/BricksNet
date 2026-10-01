using BricksNet.EntityFrameworkCore.Storage.Internal;
using BricksNet.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace BricksNet.EntityFrameworkCore.Storage;

public sealed class SqlGenerationHelperTests
{
    private static readonly BloggingContext Context = BloggingContext.Create(new FakeDatabricks());

    private static ISqlGenerationHelper Sql => Context.GetService<ISqlGenerationHelper>();

    private static IRelationalTypeMappingSource Mappings => Context.GetService<IRelationalTypeMappingSource>();

    [Theory]
    [InlineData("blogs", "`blogs`")]
    [InlineData("odd`name", "`odd``name`")]
    [InlineData("with space", "`with space`")]
    public void DelimitIdentifier_UsesBackticks(string identifier, string expected) =>
        Assert.Equal(expected, Sql.DelimitIdentifier(identifier));

    [Fact]
    public void DelimitIdentifier_WithSchema() =>
        Assert.Equal("`sales`.`orders`", Sql.DelimitIdentifier("orders", "sales"));

    [Fact]
    public void Parameters_UseNamedMarkers()
    {
        Assert.Equal(":p0", Sql.GenerateParameterName("p0"));
        Assert.Equal(":p0", Sql.GenerateParameterNamePlaceholder("p0"));
    }

    [Fact]
    public void StatementTerminator_IsEmpty() => Assert.Equal(string.Empty, Sql.StatementTerminator);

    [Theory]
    [InlineData("plain", "'plain'")]
    [InlineData("it's", @"'it\'s'")]
    [InlineData(@"back\slash", @"'back\\slash'")]
    [InlineData(@"\'", @"'\\\''")]
    [InlineData("\"double\"", "'\"double\"'")]
    [InlineData("line\nbreak", "'line\nbreak'")]
    [InlineData("", "''")]
    public void StringLiterals_UseBackslashEscapes(string value, string expected)
    {
        Assert.Equal(expected, BricksNetSqlGenerationHelper.GenerateStringLiteral(value));
        Assert.Equal(expected, Mappings.FindMapping(typeof(string))!.GenerateSqlLiteral(value));
    }
}
