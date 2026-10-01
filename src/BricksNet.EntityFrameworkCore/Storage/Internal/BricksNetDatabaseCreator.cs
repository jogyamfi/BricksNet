using BricksNet.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// Supports <c>EnsureCreated</c>, <c>EnsureDeleted</c> and <c>CanConnect</c> for development and
/// tests. The "database" is the set of tables in the model: <c>EnsureCreated</c> creates their schemas
/// and Delta tables when none exist yet, and <c>EnsureDeleted</c> drops those tables (never whole
/// schemas, so it cannot remove tables EF does not own).
/// </summary>
/// <remarks>
/// Use SQL or a dedicated tool to manage production schemas; migrations are not supported.
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetDatabaseCreator : RelationalDatabaseCreator
{
    /// <summary>Initialises a new instance.</summary>
    public BricksNetDatabaseCreator(RelationalDatabaseCreatorDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <summary>Returns <see langword="true"/> when Databricks can be reached (<c>SELECT 1</c> succeeds).</summary>
    public override bool Exists()
    {
        Query("SELECT 1");
        return true;
    }

    /// <summary>Nothing to create: Databricks catalogs exist independently of EF; schemas are created with the tables.</summary>
    public override void Create()
    {
    }

    /// <summary>Returns <see langword="true"/> when any table of the model already exists.</summary>
    public override bool HasTables()
    {
        var helper = Dependencies.SqlGenerationHelper;
        foreach (var group in ModelTables().GroupBy(t => (t.Catalog, t.Schema)))
        {
            var (catalog, schema) = group.Key;
            var catalogPrefix = catalog is null ? string.Empty : helper.DelimitIdentifier(catalog) + ".";
            if (schema is not null
                && Query($"SHOW SCHEMAS {(catalog is null ? string.Empty : "IN " + helper.DelimitIdentifier(catalog) + " ")}LIKE "
                    + BricksNetSqlGenerationHelper.GenerateStringLiteral(schema)).Count == 0)
            {
                continue;
            }

            var location = schema is null ? string.Empty : " IN " + catalogPrefix + helper.DelimitIdentifier(schema);
            var existing = Query("SHOW TABLES" + location)
                .Select(row => row.Length > 1 ? row[1] : row[0])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (group.Any(t => existing.Contains(t.Name)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Drops the model's tables. Schemas are left in place.</summary>
    public override void Delete()
    {
        var model = Dependencies.CurrentContext.Context.GetService<IDesignTimeModel>().Model;
        var operations = ModelTables().Select(t => (MigrationOperation)new DropTableOperation { Name = t.Name, Schema = t.Schema }).ToList();
        ExecuteWithoutTransaction(Dependencies.MigrationsSqlGenerator.Generate(operations, model));
    }

    /// <summary>Creates the model's schemas and tables, one statement at a time and without a transaction.</summary>
    public override void CreateTables() => ExecuteWithoutTransaction(GetCreateTablesCommands());

    /// <inheritdoc />
    public override Task CreateTablesAsync(CancellationToken cancellationToken = default)
    {
        CreateTables();
        return Task.CompletedTask;
    }

    private void ExecuteWithoutTransaction(IEnumerable<Microsoft.EntityFrameworkCore.Migrations.MigrationCommand> commands)
    {
        var connection = Dependencies.Connection;
        connection.Open();
        try
        {
            foreach (var command in commands)
            {
                command.ExecuteNonQuery(connection);
            }
        }
        finally
        {
            connection.Close();
        }
    }

    private IEnumerable<(string? Catalog, string? Schema, string Name)> ModelTables() =>
        Dependencies.CurrentContext.Context.GetService<IDesignTimeModel>().Model.GetRelationalModel().Tables
            .Select(t => ((string?)t.FindAnnotation(BricksNetAnnotationNames.Catalog)?.Value, t.Schema, t.Name));

    private List<string[]> Query(string sql)
    {
        var connection = Dependencies.Connection;
        var rows = new List<string[]>();
        connection.Open();
        try
        {
            using var reader = Dependencies.CurrentContext.Context.GetService<IRawSqlCommandBuilder>().Build(sql).ExecuteReader(
                new RelationalCommandParameterObject(
                    connection,
                    parameterValues: null,
                    readerColumns: null,
                    Dependencies.CurrentContext.Context,
                    Dependencies.CommandLogger,
                    CommandSource.Migrations));
            while (reader.Read())
            {
                var row = new string[reader.DbDataReader.FieldCount];
                for (var i = 0; i < row.Length; i++)
                {
                    row[i] = Convert.ToString(reader.DbDataReader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                }

                rows.Add(row);
            }
        }
        finally
        {
            connection.Close();
        }

        return rows;
    }
}
