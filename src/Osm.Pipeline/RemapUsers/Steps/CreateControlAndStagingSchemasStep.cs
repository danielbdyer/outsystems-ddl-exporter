using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.RemapUsers.Steps;

internal sealed class CreateControlAndStagingSchemasStep : RemapUsersPipelineStep
{
    public CreateControlAndStagingSchemasStep()
        : base("create-control-and-staging-schemas")
    {
    }

    protected override async Task ExecuteCoreAsync(RemapUsersContext context, CancellationToken cancellationToken)
    {
        await EnsureSchemasAsync(context, cancellationToken).ConfigureAwait(false);
        await EnsureControlTablesAsync(context, cancellationToken).ConfigureAwait(false);
        await EnsureStagingTablesAsync(context, cancellationToken).ConfigureAwait(false);
        await SeedForeignKeyCatalogAsync(context, cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureSchemasAsync(RemapUsersContext context, CancellationToken cancellationToken)
    {
        const string command = @"
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = @SchemaName)
BEGIN
    DECLARE @Sql nvarchar(400);
    SET @Sql = N'CREATE SCHEMA ' + QUOTENAME(@SchemaName);
    EXEC (@Sql);
END;";

        foreach (var schema in new[] { "ctl", "stg" })
        {
            await context.SqlRunner.ExecuteAsync(
                command,
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["SchemaName"] = schema },
                context.CommandTimeout,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task EnsureControlTablesAsync(RemapUsersContext context, CancellationToken cancellationToken)
    {
        const string createUserMap = @"
IF OBJECT_ID('ctl.UserMap', 'U') IS NULL
BEGIN
    CREATE TABLE ctl.UserMap (
        SourceEnv nvarchar(32) NOT NULL,
        SourceUserId bigint NOT NULL,
        SourceEmail nvarchar(256) NULL,
        SourceUserName nvarchar(256) NULL,
        SourceEmpNo nvarchar(128) NULL,
        TargetUserId bigint NOT NULL,
        MatchReason nvarchar(64) NOT NULL,
        CreatedAtUtc datetime2 NOT NULL CONSTRAINT DF_UserMap_CreatedAtUtc DEFAULT (sysutcdatetime()),
        CONSTRAINT PK_UserMap PRIMARY KEY (SourceEnv, SourceUserId)
    );
END;";

        const string createCatalog = @"
IF OBJECT_ID('ctl.UserFkCatalog', 'U') IS NULL
BEGIN
    CREATE TABLE ctl.UserFkCatalog (
        TableSchema sysname NOT NULL,
        TableName sysname NOT NULL,
        ColumnName sysname NOT NULL,
        PathHint nvarchar(512) NULL,
        CONSTRAINT PK_UserFkCatalog PRIMARY KEY (TableSchema, TableName, ColumnName)
    );
END;";

        const string createAudit = @"
IF OBJECT_ID('ctl.UserKeyChanges', 'U') IS NULL
BEGIN
    CREATE TABLE ctl.UserKeyChanges (
        TableName sysname NOT NULL,
        ColumnName sysname NOT NULL,
        OldId bigint NULL,
        NewId bigint NULL,
        ChangedAt datetime2 NOT NULL CONSTRAINT DF_UserKeyChanges_ChangedAt DEFAULT (sysutcdatetime())
    );
END;";

        foreach (var command in new[] { createUserMap, createCatalog, createAudit })
        {
            await context.SqlRunner.ExecuteAsync(
                command,
                context.BuildCommonParameters(),
                context.CommandTimeout,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task EnsureStagingTablesAsync(RemapUsersContext context, CancellationToken cancellationToken)
    {
        var tables = context.State.LoadOrder;
        if (tables.Count == 0)
        {
            tables = await context.SchemaGraph.GetTablesAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var table in tables)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"IF OBJECT_ID('stg.{table.Name}', 'U') IS NULL");
            builder.AppendLine("BEGIN");
            builder.AppendLine($"    SELECT TOP 0 * INTO stg.[{table.Name}] FROM {table.QualifiedName};");
            builder.AppendLine("END");
            builder.AppendLine("ELSE");
            builder.AppendLine("BEGIN");
            builder.AppendLine($"    TRUNCATE TABLE stg.[{table.Name}];");
            builder.AppendLine("END;");

            await context.SqlRunner.ExecuteAsync(
                builder.ToString(),
                context.BuildCommonParameters(),
                context.CommandTimeout,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task SeedForeignKeyCatalogAsync(RemapUsersContext context, CancellationToken cancellationToken)
    {
        var catalog = context.State.ForeignKeyCatalog;
        await context.SqlRunner.ExecuteAsync(
            "DELETE FROM ctl.UserFkCatalog;",
            context.BuildCommonParameters(),
            context.CommandTimeout,
            cancellationToken).ConfigureAwait(false);

        if (catalog.Count == 0)
        {
            return;
        }

        var insertBuilder = new StringBuilder();
        insertBuilder.AppendLine("INSERT INTO ctl.UserFkCatalog (TableSchema, TableName, ColumnName, PathHint)");
        insertBuilder.AppendLine("VALUES");
        var rows = catalog
            .Select((entry, index) => (entry, index))
            .Select(tuple => BuildRow(tuple.entry, tuple.index))
            .ToArray();

        insertBuilder.AppendLine(string.Join(",\n", rows.Select(static r => r.sql)) + ";");

        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            parameters[row.paramSchema] = row.entry.TableSchema;
            parameters[row.paramTable] = row.entry.TableName;
            parameters[row.paramColumn] = row.entry.ColumnName;
            parameters[row.paramPath] = row.entry.PathSegments.Count == 0
                ? null
                : string.Join(" > ", row.entry.PathSegments);
        }

        await context.SqlRunner.ExecuteAsync(
            insertBuilder.ToString(),
            parameters,
            context.CommandTimeout,
            cancellationToken).ConfigureAwait(false);
    }

    private static (string sql, string paramSchema, string paramTable, string paramColumn, string paramPath, UserForeignKeyCatalogEntry entry) BuildRow(UserForeignKeyCatalogEntry entry, int index)
    {
        var schemaParam = $"@schema_{index}";
        var tableParam = $"@table_{index}";
        var columnParam = $"@column_{index}";
        var pathParam = $"@path_{index}";
        var sql = string.Format(CultureInfo.InvariantCulture, "({0}, {1}, {2}, {3})", schemaParam, tableParam, columnParam, pathParam);
        return (sql, schemaParam, tableParam, columnParam, pathParam, entry);
    }
}
