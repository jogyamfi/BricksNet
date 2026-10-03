using BricksNet.AdoNet;
using BricksNet.Contracts.Options;
using BricksNet.EntityFrameworkCore.Infrastructure.Internal;
using BricksNet.EntityFrameworkCore.Storage.Internal;
using BricksNet.EntityFrameworkCore.TestUtilities;
using BricksNet.EntityFrameworkCore.Update.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Xunit;

namespace BricksNet.EntityFrameworkCore;

public sealed class ProviderRegistrationTests
{
    private static BloggingContext ContextWith(Action<DbContextOptionsBuilder<BloggingContext>> configure)
    {
        var builder = new DbContextOptionsBuilder<BloggingContext>();
        configure(builder);
        return new BloggingContext(builder.Options);
    }

    [Fact]
    public void UseBricksNet_RegistersTheProvider()
    {
        using var context = BloggingContext.Create(new FakeDatabricks());

        Assert.True(context.Database.IsRelational());
        Assert.Equal("BricksNet.EntityFrameworkCore", context.Database.ProviderName);
        Assert.IsType<BricksNetTypeMappingSource>(context.GetService<IRelationalTypeMappingSource>());
        Assert.IsType<BricksNetSqlGenerationHelper>(context.GetService<ISqlGenerationHelper>());
        Assert.IsType<BricksNetRelationalConnection>(context.GetService<IRelationalConnection>());
        Assert.IsType<BricksNetBatchExecutor>(context.GetService<IBatchExecutor>());
    }

    [Fact]
    public void ConnectionString_CreatesBricksNetConnection_KeepingSessionAliveByDefault()
    {
        using var context = ContextWith(b => b.UseBricksNet(FakeDatabricks.ConnectionString));

        var connection = Assert.IsType<BricksNetConnection>(context.Database.GetDbConnection());

        Assert.Equal("fake.local", connection.DataSource);
        Assert.Equal("default", connection.Database);
        Assert.True(connection.KeepSessionAlive);
    }

    [Fact]
    public void ConnectionString_KeepSessionAliveSetting_IsRespected()
    {
        using var context = ContextWith(b => b.UseBricksNet(FakeDatabricks.ConnectionString + ";KeepSessionAlive=false"));

        Assert.False(((BricksNetConnection)context.Database.GetDbConnection()).KeepSessionAlive);
    }

    [Fact]
    public void OptionsBuilder_KeepSessionAlive_OverridesConnectionString()
    {
        using var context = ContextWith(b => b.UseBricksNet(
            FakeDatabricks.ConnectionString + ";KeepSessionAlive=true",
            o => o.KeepSessionAlive(false)));

        Assert.False(((BricksNetConnection)context.Database.GetDbConnection()).KeepSessionAlive);
    }

    [Fact]
    public void ClientOptions_CreateBricksNetConnection()
    {
        var clientOptions = new BricksNetClientOptions { Host = "adb-1.azuredatabricks.net", HttpPath = "/sql/1.0/warehouses/w" };
        using var context = ContextWith(b => b.UseBricksNet(clientOptions, "secret-token"));

        var connection = Assert.IsType<BricksNetConnection>(context.Database.GetDbConnection());

        Assert.Equal("adb-1.azuredatabricks.net", connection.DataSource);
        Assert.True(connection.KeepSessionAlive);
    }

    [Fact]
    public void ClientOptions_ReplaceAnEarlierConnectionString()
    {
        var clientOptions = new BricksNetClientOptions { Host = "from-options", HttpPath = "/p" };
        using var context = ContextWith(b => b
            .UseBricksNet(FakeDatabricks.ConnectionString)
            .UseBricksNet(clientOptions, accessToken: null));

        Assert.Equal("from-options", context.Database.GetDbConnection().DataSource);
        Assert.True(string.IsNullOrEmpty(context.Database.GetConnectionString()));
    }

    [Fact]
    public void ConnectionString_ReplacesEarlierClientOptions()
    {
        var clientOptions = new BricksNetClientOptions { Host = "from-options", HttpPath = "/p" };
        var builder = new DbContextOptionsBuilder<BloggingContext>()
            .UseBricksNet(clientOptions, "token")
            .UseBricksNet(FakeDatabricks.ConnectionString);
        using var context = new BloggingContext(builder.Options);

        Assert.Equal("fake.local", context.Database.GetDbConnection().DataSource);
        Assert.Null(builder.Options.FindExtension<BricksNetOptionsExtension>()!.AccessToken);
    }

    [Fact]
    public void LogFragment_NeverContainsSecrets()
    {
        var clientOptions = new BricksNetClientOptions { Host = "h", HttpPath = "/p" };
        var options = new DbContextOptionsBuilder()
            .UseBricksNet(clientOptions, "secret-token", o => o.KeepSessionAlive(false).CommandTimeout(30))
            .Options;

        var fragment = options.FindExtension<BricksNetOptionsExtension>()!.Info.LogFragment;

        Assert.DoesNotContain("secret-token", fragment, StringComparison.Ordinal);
        Assert.Contains("KeepSessionAlive=False", fragment, StringComparison.Ordinal);
        Assert.Contains("CommandTimeout=30", fragment, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingConnection_IsUsed()
    {
        var databricks = new FakeDatabricks();
        var connection = databricks.CreateConnection();
        using var context = ContextWith(b => b.UseBricksNet(connection));

        Assert.Same(connection, context.Database.GetDbConnection());
    }

    [Fact]
    public void ExistingConnection_KeepsItsOwnKeepSessionAlive_UnlessConfigured()
    {
        var databricks = new FakeDatabricks();
        var untouched = databricks.CreateConnection();
        var configured = databricks.CreateConnection();

        using var first = ContextWith(b => b.UseBricksNet(untouched));
        using var second = ContextWith(b => b.UseBricksNet(configured, o => o.KeepSessionAlive()));
        _ = first.Database.GetDbConnection();
        _ = second.Database.GetDbConnection();

        Assert.False(untouched.KeepSessionAlive);
        Assert.True(configured.KeepSessionAlive);
    }
}
