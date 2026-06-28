using System.Data.Common;
using BricksNet.Contracts.Options;

namespace BricksNet.AdoNet;

/// <summary>
/// Typed wrapper around an ADO.NET connection string for Databricks.
/// Parse logic is implemented in Phase 8; this stub exposes typed property
/// signatures over the underlying <see cref="DbConnectionStringBuilder"/> dictionary.
/// </summary>
public sealed class BricksNetConnectionStringBuilder : DbConnectionStringBuilder
{
    private const string HostKey = "Host";
    private const string PortKey = "Port";
    private const string HttpPathKey = "HttpPath";
    private const string TokenKey = "Token";
    private const string AuthTypeKey = "AuthType";
    private const string UseThriftKey = "UseThrift";
    private const string CatalogKey = "Catalog";
    private const string SchemaKey = "Schema";

    /// <summary>Databricks workspace hostname.</summary>
    public string? Host
    {
        get => TryGetValue(HostKey, out object? v) ? v as string : null;
        set => this[HostKey] = value;
    }

    /// <summary>HTTPS port. Defaults to <c>443</c>.</summary>
    public int Port
    {
        get => TryGetValue(PortKey, out object? v) && int.TryParse(v?.ToString(), out int p) ? p : 443;
        set => this[PortKey] = value;
    }

    /// <summary>HTTP path to the SQL warehouse or cluster.</summary>
    public string? HttpPath
    {
        get => TryGetValue(HttpPathKey, out object? v) ? v as string : null;
        set => this[HttpPathKey] = value;
    }

    /// <summary>
    /// Personal Access Token. Value is never echoed in logs; use <c>"***"</c>
    /// in any diagnostic output that references this builder.
    /// </summary>
    public string? Token
    {
        get => TryGetValue(TokenKey, out object? v) ? v as string : null;
        set => this[TokenKey] = value;
    }

    /// <summary>Authentication type. Defaults to <see cref="AuthType.Pat"/>.</summary>
    public AuthType AuthType
    {
        get => TryGetValue(AuthTypeKey, out object? v) && Enum.TryParse<AuthType>(v?.ToString(), out AuthType a) ? a : AuthType.Pat;
        set => this[AuthTypeKey] = value.ToString();
    }

    /// <summary>When <see langword="true"/>, connects via the Thrift backend.</summary>
    public bool UseThrift
    {
        get => TryGetValue(UseThriftKey, out object? v) && bool.TryParse(v?.ToString(), out bool b) ? b : true;
        set => this[UseThriftKey] = value;
    }

    /// <summary>Default catalog for unqualified object references.</summary>
    public string? Catalog
    {
        get => TryGetValue(CatalogKey, out object? v) ? v as string : null;
        set => this[CatalogKey] = value;
    }

    /// <summary>Default schema for unqualified object references.</summary>
    public string? Schema
    {
        get => TryGetValue(SchemaKey, out object? v) ? v as string : null;
        set => this[SchemaKey] = value;
    }
}
