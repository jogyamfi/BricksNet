using BricksNet.AdoNet;
using BricksNet.Contracts;
using BricksNet.Contracts.Options;
using BricksNet.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BricksNet.FluentApi;

public sealed class BricksNetSessionTests
{
    // ---- helpers ----

    private static readonly BricksNetClientOptions DefaultClientOptions = new()
    {
        Host = "workspace.azuredatabricks.net",
        HttpPath = "/sql/1.0/warehouses/abc",
    };

    private static readonly SessionOptions DefaultSessionOptions = new()
    {
        Catalog = "main",
        Schema = "default",
        MaxRows = 5_000,
    };

    private static BricksNetSession CreateSession(
        ISessionBackend backend,
        SessionOptions? sessionOptions = null,
        Action<BricksNetSession>? onClosed = null)
        => new(backend,
            sessionOptions ?? DefaultSessionOptions,
            DefaultClientOptions,
            "rest",
            NullLoggerFactory.Instance,
            onClosed);

    private static Mock<ISessionBackend> DefaultSessionMock()
    {
        var mock = new Mock<ISessionBackend>();
        mock.Setup(m => m.Id).Returns("session-001");
        return mock;
    }

    private static Mock<IOperationBackend> DefaultOperationMock(string id = "op-001")
    {
        var op = new Mock<IOperationBackend>();
        op.Setup(m => m.Id).Returns(id);
        op.Setup(m => m.GetStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(QueryStatus.Succeeded);
        op.Setup(m => m.FetchResultsAsync(It.IsAny<FetchOptions>()))
            .ReturnsAsync(new FetchResult(
                ResultFormat.Arrow, ReadOnlyMemory<byte>.Empty, null,
                Array.Empty<ColumnInfo>()));
        return op;
    }

    // ---- Session.Id ----

    [Fact]
    public void Id_ReturnsBackendId()
    {
        var session = CreateSession(DefaultSessionMock().Object);
        Assert.Equal("session-001", session.Id);
    }

    // ---- ExecuteStatementAsync ----

    [Fact]
    public async Task ExecuteStatementAsync_DelegatesToBackend()
    {
        var sessionMock = DefaultSessionMock();
        var opMock = DefaultOperationMock();
        sessionMock
            .Setup(m => m.ExecuteStatementAsync(
                "SELECT 1", It.IsAny<StatementOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(opMock.Object);

        var session = CreateSession(sessionMock.Object);
        var op = await session.ExecuteStatementAsync("SELECT 1");

        Assert.NotNull(op);
        sessionMock.Verify(m => m.ExecuteStatementAsync(
            "SELECT 1", It.IsAny<StatementOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteStatementAsync_EmptySql_Throws()
    {
        var session = CreateSession(DefaultSessionMock().Object);

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            session.ExecuteStatementAsync(string.Empty));
    }

    [Fact]
    public async Task ExecuteStatementAsync_MergesSessionCatalog_WhenStatementOptionsOmit()
    {
        StatementOptions? capturedOptions = null;
        var sessionMock = DefaultSessionMock();
        sessionMock
            .Setup(m => m.ExecuteStatementAsync(
                It.IsAny<string>(), It.IsAny<StatementOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, StatementOptions?, CancellationToken>(
                (_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(DefaultOperationMock().Object);

        var session = CreateSession(sessionMock.Object);
        // Statement options don't specify Catalog/Schema
        await session.ExecuteStatementAsync("SELECT 1", new StatementOptions());

        Assert.NotNull(capturedOptions);
        Assert.Equal("main", capturedOptions!.Catalog);
        Assert.Equal("default", capturedOptions.Schema);
    }

    [Fact]
    public async Task ExecuteStatementAsync_StatementOptionsCatalogOverrides_SessionDefault()
    {
        StatementOptions? capturedOptions = null;
        var sessionMock = DefaultSessionMock();
        sessionMock
            .Setup(m => m.ExecuteStatementAsync(
                It.IsAny<string>(), It.IsAny<StatementOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, StatementOptions?, CancellationToken>(
                (_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(DefaultOperationMock().Object);

        var session = CreateSession(sessionMock.Object);
        await session.ExecuteStatementAsync("SELECT 1", new StatementOptions { Catalog = "other_cat" });

        Assert.Equal("other_cat", capturedOptions!.Catalog);
        Assert.Equal("default", capturedOptions.Schema); // falls back to session default
    }

    [Fact]
    public async Task ExecuteStatementAsync_MergesSessionMaxRows_WhenStatementOptionsOmit()
    {
        StatementOptions? capturedOptions = null;
        var sessionMock = DefaultSessionMock();
        sessionMock
            .Setup(m => m.ExecuteStatementAsync(
                It.IsAny<string>(), It.IsAny<StatementOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, StatementOptions?, CancellationToken>(
                (_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(DefaultOperationMock().Object);

        var session = CreateSession(sessionMock.Object);
        // MaxRows = 0 is the sentinel meaning "not specified"; session default (5_000) is used.
        await session.ExecuteStatementAsync("SELECT 1", new StatementOptions { MaxRows = 0 });

        Assert.NotNull(capturedOptions);
        Assert.Equal(5_000, capturedOptions!.MaxRows); // from session defaults
    }

    // ---- Metadata methods ----

    [Fact]
    public async Task GetCatalogsAsync_DelegatesToBackend()
    {
        var sessionMock = DefaultSessionMock();
        sessionMock
            .Setup(m => m.GetCatalogsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(DefaultOperationMock().Object);

        var session = CreateSession(sessionMock.Object);
        var op = await session.GetCatalogsAsync();

        Assert.NotNull(op);
        sessionMock.Verify(m => m.GetCatalogsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSchemasAsync_DelegatesToBackend()
    {
        var sessionMock = DefaultSessionMock();
        sessionMock
            .Setup(m => m.GetSchemasAsync("cat", "pat%", It.IsAny<CancellationToken>()))
            .ReturnsAsync(DefaultOperationMock().Object);

        var session = CreateSession(sessionMock.Object);
        var op = await session.GetSchemasAsync("cat", "pat%");

        Assert.NotNull(op);
        sessionMock.Verify(m => m.GetSchemasAsync("cat", "pat%", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTablesAsync_DelegatesToBackend()
    {
        var sessionMock = DefaultSessionMock();
        sessionMock
            .Setup(m => m.GetTablesAsync(null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DefaultOperationMock().Object);

        var session = CreateSession(sessionMock.Object);
        await session.GetTablesAsync();

        sessionMock.Verify(m => m.GetTablesAsync(null, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetColumnsAsync_DelegatesToBackend()
    {
        var sessionMock = DefaultSessionMock();
        sessionMock
            .Setup(m => m.GetColumnsAsync(null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DefaultOperationMock().Object);

        var session = CreateSession(sessionMock.Object);
        await session.GetColumnsAsync();

        sessionMock.Verify(
            m => m.GetColumnsAsync(null, null, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- CloseAsync ----

    [Fact]
    public async Task CloseAsync_CallsBackendCloseSession()
    {
        var sessionMock = DefaultSessionMock();
        sessionMock
            .Setup(m => m.CloseSessionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var session = CreateSession(sessionMock.Object);
        await session.CloseAsync();

        sessionMock.Verify(m => m.CloseSessionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CloseAsync_IsIdempotent()
    {
        var sessionMock = DefaultSessionMock();
        sessionMock
            .Setup(m => m.CloseSessionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var session = CreateSession(sessionMock.Object);
        await session.CloseAsync();
        await session.CloseAsync();

        sessionMock.Verify(m => m.CloseSessionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CloseAsync_InvokesOnClosedCallback()
    {
        var sessionMock = DefaultSessionMock();
        sessionMock
            .Setup(m => m.CloseSessionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        BricksNetSession? closedSession = null;
        var session = CreateSession(sessionMock.Object, onClosed: s => closedSession = s);
        await session.CloseAsync();

        Assert.NotNull(closedSession);
    }

    [Fact]
    public async Task DisposeAsync_ClosesSession()
    {
        var sessionMock = DefaultSessionMock();
        sessionMock
            .Setup(m => m.CloseSessionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var session = CreateSession(sessionMock.Object);
        await session.DisposeAsync(); // explicit call so verify runs after dispose

        sessionMock.Verify(m => m.CloseSessionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- ObjectDisposedException after close ----

    [Fact]
    public async Task ExecuteStatementAsync_AfterClose_ThrowsObjectDisposed()
    {
        var sessionMock = DefaultSessionMock();
        sessionMock
            .Setup(m => m.CloseSessionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var session = CreateSession(sessionMock.Object);
        await session.CloseAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            session.ExecuteStatementAsync("SELECT 1"));
    }

    [Fact]
    public async Task GetCatalogsAsync_AfterClose_ThrowsObjectDisposed()
    {
        var sessionMock = DefaultSessionMock();
        sessionMock
            .Setup(m => m.CloseSessionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var session = CreateSession(sessionMock.Object);
        await session.CloseAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            session.GetCatalogsAsync());
    }
}
