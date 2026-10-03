using System.Data.Common;
using System.Globalization;
using System.Text;
using BricksNet.Contracts.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace BricksNet.EntityFrameworkCore.Infrastructure.Internal;

/// <summary>
/// The EF Core options extension for the BricksNet provider. Carries the connection settings
/// (a connection string, an existing connection, or <see cref="BricksNetClientOptions"/> plus an
/// access token) and the provider-specific options set through <see cref="BricksNetDbContextOptionsBuilder"/>.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetOptionsExtension : RelationalOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;
    private BricksNetClientOptions? _clientOptions;
    private string? _accessToken;
    private bool? _keepSessionAlive;
    private int _maxRetryCount;
    private TimeSpan _maxRetryDelay;

    /// <summary>Initialises an empty extension.</summary>
    public BricksNetOptionsExtension()
    {
    }

    /// <summary>Initialises a copy of <paramref name="copyFrom"/>.</summary>
    protected BricksNetOptionsExtension(BricksNetOptionsExtension copyFrom)
        : base(copyFrom)
    {
        _clientOptions = copyFrom._clientOptions;
        _accessToken = copyFrom._accessToken;
        _keepSessionAlive = copyFrom._keepSessionAlive;
        _maxRetryCount = copyFrom._maxRetryCount;
        _maxRetryDelay = copyFrom._maxRetryDelay;
    }

    /// <inheritdoc />
    public override DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <summary>
    /// Client options used to create connections when the context was configured with
    /// <see cref="BricksNetClientOptions"/> instead of a connection string.
    /// </summary>
    public virtual BricksNetClientOptions? ClientOptions => _clientOptions;

    /// <summary>The access token that accompanies <see cref="ClientOptions"/>, when any.</summary>
    public virtual string? AccessToken => _accessToken;

    /// <summary>
    /// Whether connections keep their Databricks session open between EF's open/close cycles.
    /// <see langword="null"/> means the provider default: on, unless the connection string sets
    /// <c>KeepSessionAlive</c> itself.
    /// </summary>
    public virtual bool? KeepSessionAlive => _keepSessionAlive;

    /// <summary>
    /// How many times a <c>SaveChanges</c> statement that failed transiently is retried; <c>0</c>
    /// (the default) disables retries. Set by <c>EnableRetryOnFailure</c>.
    /// </summary>
    public virtual int MaxRetryCount => _maxRetryCount;

    /// <summary>The longest delay between <c>SaveChanges</c> statement retries.</summary>
    public virtual TimeSpan MaxRetryDelay => _maxRetryDelay;

    /// <summary>
    /// Returns a copy configured to connect with <paramref name="clientOptions"/>, replacing any
    /// connection string or connection configured earlier.
    /// </summary>
    public virtual BricksNetOptionsExtension WithClientOptions(BricksNetClientOptions clientOptions, string? accessToken)
    {
        ArgumentNullException.ThrowIfNull(clientOptions);

        var clone = (BricksNetOptionsExtension)WithConnectionString(null).WithConnection(null);
        clone._clientOptions = clientOptions;
        clone._accessToken = accessToken;
        return clone;
    }

    /// <summary>Returns a copy using <paramref name="connectionString"/>, replacing any client options configured earlier.</summary>
    public override RelationalOptionsExtension WithConnectionString(string? connectionString) =>
        ((BricksNetOptionsExtension)base.WithConnectionString(connectionString)).WithoutClientOptions();

    /// <summary>Returns a copy using <paramref name="connection"/>, replacing any client options configured earlier.</summary>
    public override RelationalOptionsExtension WithConnection(DbConnection? connection) =>
        ((BricksNetOptionsExtension)base.WithConnection(connection)).WithoutClientOptions();

    /// <summary>Returns a copy using <paramref name="connection"/>, replacing any client options configured earlier.</summary>
    public override RelationalOptionsExtension WithConnection(DbConnection? connection, bool owned) =>
        ((BricksNetOptionsExtension)base.WithConnection(connection, owned)).WithoutClientOptions();

    /// <summary>Returns a copy with the <see cref="KeepSessionAlive"/> setting changed.</summary>
    public virtual BricksNetOptionsExtension WithKeepSessionAlive(bool keepSessionAlive)
    {
        var clone = (BricksNetOptionsExtension)Clone();
        clone._keepSessionAlive = keepSessionAlive;
        return clone;
    }

    /// <summary>Returns a copy that retries transiently failed <c>SaveChanges</c> statements.</summary>
    public virtual BricksNetOptionsExtension WithRetryOnFailure(int maxRetryCount, TimeSpan maxRetryDelay)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetryCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRetryDelay, TimeSpan.Zero);

        var clone = (BricksNetOptionsExtension)Clone();
        clone._maxRetryCount = maxRetryCount;
        clone._maxRetryDelay = maxRetryDelay;
        return clone;
    }

    /// <inheritdoc />
    protected override RelationalOptionsExtension Clone() => new BricksNetOptionsExtension(this);

    /// <inheritdoc />
    public override void ApplyServices(IServiceCollection services) => services.AddEntityFrameworkBricksNet();

    // Called on a fresh clone, so it mutates rather than cloning again.
    private BricksNetOptionsExtension WithoutClientOptions()
    {
        _clientOptions = null;
        _accessToken = null;
        return this;
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : RelationalExtensionInfo(extension)
    {
        private string? _logFragment;

        private new BricksNetOptionsExtension Extension => (BricksNetOptionsExtension)base.Extension;

        public override bool IsDatabaseProvider => true;

        // Never includes the access token or connection string.
        public override string LogFragment
        {
            get
            {
                if (_logFragment is null)
                {
                    var builder = new StringBuilder(base.LogFragment);
                    if (Extension._keepSessionAlive is { } keepSessionAlive)
                    {
                        builder.Append(CultureInfo.InvariantCulture, $"KeepSessionAlive={keepSessionAlive} ");
                    }

                    if (Extension._maxRetryCount > 0)
                    {
                        builder.Append(CultureInfo.InvariantCulture, $"MaxRetryCount={Extension._maxRetryCount} ");
                    }

                    if (Extension._clientOptions is not null)
                    {
                        builder.Append("ClientOptions ");
                    }

                    _logFragment = builder.ToString();
                }

                return _logFragment;
            }
        }

        // None of the BricksNet-specific options change the services EF builds, so contexts that
        // differ only in them share an internal service provider.
        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["BricksNet:" + nameof(KeepSessionAlive)] =
                (Extension._keepSessionAlive?.GetHashCode() ?? 0).ToString(CultureInfo.InvariantCulture);
        }
    }
}
