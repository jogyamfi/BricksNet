using BricksNet.EntityFrameworkCore.Metadata.Internal;
using BricksNet.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace BricksNet.EntityFrameworkCore.Migrations.Internal;

/// <summary>
/// Generates the DDL behind <c>EnsureCreated</c>, <c>EnsureDeleted</c> and
/// <c>GenerateCreateScript</c>: <c>CREATE SCHEMA</c>, <c>CREATE TABLE ... USING DELTA</c> and
/// <c>DROP TABLE</c>. Migrations themselves are not supported.
/// </summary>
/// <remarks>
/// <para>
/// Tables are created without primary-key, foreign-key, unique or check constraints. Databricks only
/// accepts (informational, unenforced) key constraints on Unity Catalog tables, and EF does not need
/// them. Indexes are skipped because Databricks has none. Column <c>NOT NULL</c> and comments are kept.
/// </para>
/// <para>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </para>
/// </remarks>
public class BricksNetMigrationsSqlGenerator : MigrationsSqlGenerator
{
    /// <summary>Initialises a new instance.</summary>
    public BricksNetMigrationsSqlGenerator(MigrationsSqlGeneratorDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <summary>Dispatches the operations used to create and drop tables; anything else is unsupported.</summary>
    protected override void Generate(MigrationOperation operation, IModel? model, MigrationCommandListBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        switch (operation)
        {
            case EnsureSchemaOperation ensureSchema:
                GenerateEnsureSchema(ensureSchema, model, builder);
                break;
            case CreateTableOperation createTable:
                GenerateCreateTable(createTable, model, builder);
                break;
            case DropTableOperation dropTable:
                builder
                    .Append("DROP TABLE IF EXISTS ")
                    .Append(QualifiedTable(dropTable.Name, dropTable.Schema, model))
                    .EndCommand(suppressTransaction: true);
                break;
            case CreateIndexOperation:
                // Databricks has no indexes; Delta relies on data skipping and clustering instead.
                break;
            default:
                throw new NotSupportedException(
                    $"The '{operation.GetType().Name}' operation is not supported. "
                    + BricksNetHistoryRepository.NotSupportedMessage);
        }
    }

    private void GenerateEnsureSchema(EnsureSchemaOperation operation, IModel? model, MigrationCommandListBuilder builder)
    {
        var catalog = model?.GetDefaultCatalog();
        builder
            .Append("CREATE SCHEMA IF NOT EXISTS ")
            .Append(catalog is null ? string.Empty : Dependencies.SqlGenerationHelper.DelimitIdentifier(catalog) + ".")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
            .EndCommand(suppressTransaction: true);
    }

    private void GenerateCreateTable(CreateTableOperation operation, IModel? model, MigrationCommandListBuilder builder)
    {
        builder
            .Append("CREATE TABLE ")
            .Append(QualifiedTable(operation.Name, operation.Schema, model))
            .AppendLine(" (");

        using (builder.Indent())
        {
            for (var i = 0; i < operation.Columns.Count; i++)
            {
                var column = operation.Columns[i];
                builder
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(column.Name))
                    .Append(" ")
                    .Append(column.ColumnType ?? GetColumnType(column.Schema, column.Table, column.Name, column, model)!);

                if (!column.IsNullable)
                {
                    builder.Append(" NOT NULL");
                }

                if (!string.IsNullOrEmpty(column.Comment))
                {
                    builder.Append(" COMMENT ").Append(BricksNetSqlGenerationHelper.GenerateStringLiteral(column.Comment));
                }

                builder.AppendLine(i < operation.Columns.Count - 1 ? "," : string.Empty);
            }
        }

        builder.Append(") USING DELTA");
        if (!string.IsNullOrEmpty(operation.Comment))
        {
            builder.AppendLine().Append("COMMENT ").Append(BricksNetSqlGenerationHelper.GenerateStringLiteral(operation.Comment));
        }

        builder.EndCommand(suppressTransaction: true);
    }

    private string QualifiedTable(string name, string? schema, IModel? model)
    {
        var table = Dependencies.SqlGenerationHelper.DelimitIdentifier(name, schema);
        var catalog = model?.GetRelationalModel().FindTable(name, schema)?.FindAnnotation(BricksNetAnnotationNames.Catalog)?.Value as string
            ?? model?.GetDefaultCatalog();
        return catalog is null ? table : Dependencies.SqlGenerationHelper.DelimitIdentifier(catalog) + "." + table;
    }
}
