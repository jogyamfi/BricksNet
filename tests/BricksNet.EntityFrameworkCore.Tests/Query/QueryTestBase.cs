using BricksNet.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace BricksNet.EntityFrameworkCore.Query;

/// <summary>Runs LINQ queries against <see cref="FakeDatabricks"/> and asserts the SQL it received.</summary>
public abstract class QueryTestBase
{
    private protected FakeDatabricks Databricks { get; } = new();

    private protected ShopContext CreateContext() => ShopContext.Create(Databricks);

    private protected AllTypesContext CreateAllTypesContext() => AllTypesContext.Create(Databricks);

    /// <summary>Asserts the SQL of every statement executed so far, in order.</summary>
    protected void AssertSql(params string[] expected)
    {
        var actual = Databricks.Statements.Select(statement => Normalize(statement.Sql)).ToArray();
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            // Reported in full: xUnit's string diff truncates long statements.
            var expectedSql = Normalize(expected[i]);
            Assert.True(
                expectedSql == actual[i],
                $"Statement {i} differs.{Environment.NewLine}--- Expected:{Environment.NewLine}{expectedSql}"
                + $"{Environment.NewLine}--- Actual:{Environment.NewLine}{actual[i]}");
        }
    }

    private static string Normalize(string sql) => sql.ReplaceLineEndings("\n").Trim();
}
