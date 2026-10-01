using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace BricksNet.EntityFrameworkCore.TestUtilities;

/// <summary>
/// A context whose model is built by a per-test action. Model caching is disabled, because EF
/// otherwise caches one model per context type.
/// </summary>
internal sealed class ConfigurableContext : DbContext
{
    private readonly Action<ModelBuilder> _configureModel;

    private ConfigurableContext(DbContextOptions options, Action<ModelBuilder> configureModel)
        : base(options)
    {
        _configureModel = configureModel;
    }

    public static ConfigurableContext Create(
        FakeDatabricks databricks,
        Action<ModelBuilder> configureModel,
        ILoggerFactory? loggerFactory = null)
    {
        // Not owned by the context: EF fails to dispose an owned connection after a model-build error,
        // which would hide the validation exception these tests assert on.
        var builder = new DbContextOptionsBuilder()
            .UseBricksNet(databricks.CreateConnection(), contextOwnsConnection: false)
            .ReplaceService<IModelCacheKeyFactory, UncachedModelCacheKeyFactory>();
        if (loggerFactory is not null)
        {
            builder.UseLoggerFactory(loggerFactory);
        }

        return new ConfigurableContext(builder.Options, configureModel);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) => _configureModel(modelBuilder);

    private sealed class UncachedModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => new();
    }
}

/// <summary>Collects log messages, for asserting on warnings.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<(LogLevel Level, string Message)> _messages = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Messages
    {
        get
        {
            lock (_messages)
            {
                return [.. _messages];
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void Dispose()
    {
    }

    private sealed class Logger(CapturingLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (provider._messages)
            {
                provider._messages.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
