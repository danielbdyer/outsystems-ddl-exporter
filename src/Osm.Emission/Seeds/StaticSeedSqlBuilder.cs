using System;
using System.Linq;
using System.Text;
using Osm.Domain.Configuration;
using Osm.Emission.Formatting;
using Osm.Smo;
using Osm.Smo.PerTableEmission;

namespace Osm.Emission.Seeds;

public sealed class StaticSeedSqlBuilder
{
    private readonly SqlLiteralFormatter _literalFormatter;
    private readonly SqlScriptFormatter _scriptFormatter;
    private readonly SmoFormatOptions _formatOptions;

    public StaticSeedSqlBuilder(SqlLiteralFormatter literalFormatter)
        : this(literalFormatter, new SqlScriptFormatter(), SmoFormatOptions.Default)
    {
    }

    internal StaticSeedSqlBuilder(
        SqlLiteralFormatter literalFormatter,
        SqlScriptFormatter scriptFormatter,
        SmoFormatOptions formatOptions)
    {
        _literalFormatter = literalFormatter ?? throw new ArgumentNullException(nameof(literalFormatter));
        _scriptFormatter = scriptFormatter ?? throw new ArgumentNullException(nameof(scriptFormatter));
        _formatOptions = formatOptions ?? throw new ArgumentNullException(nameof(formatOptions));
    }

    public string BuildBlock(StaticEntityTableData tableData, StaticSeedSynchronizationMode synchronizationMode)
    {
        if (tableData is null)
        {
            throw new ArgumentNullException(nameof(tableData));
        }

        var definition = tableData.Definition;
        var schema = definition.Schema;
        var targetName = definition.EffectiveName;
        var physicalName = definition.PhysicalName;
        var rows = tableData.Rows;

        var builder = new StringBuilder();
        builder.AppendLine("--------------------------------------------------------------------------------");
        builder.AppendLine($"-- Module: {definition.Module}");
        builder.AppendLine($"-- Entity: {definition.LogicalName} ({schema}.{physicalName})");
        if (!string.Equals(physicalName, targetName, StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine($"-- Target: {schema}.{targetName}");
        }

        builder.AppendLine("--------------------------------------------------------------------------------");

        var targetIdentifier = SqlIdentifierFormatter.Qualify(schema, targetName);
        var columnNames = definition.Columns
            .Select(column => QuoteColumn(column.TargetColumnName))
            .ToArray();
        var columnList = string.Join(", ", columnNames);
        var sourceProjection = string.Join(", ", columnNames.Select(name => $"Source.{name}"));
        var targetProjection = string.Join(", ", columnNames.Select(name => $"Existing.{name}"));
        var driftErrorMessage = _literalFormatter.EscapeString(
            $"Static entity seed data drift detected for {definition.Module}::{definition.LogicalName} ({schema}.{targetName}).");

        if (rows.Length == 0)
        {
            if (synchronizationMode == StaticSeedSynchronizationMode.ValidateThenApply)
            {
                builder.AppendLine($"IF EXISTS (SELECT 1 FROM {targetIdentifier})");
                builder.AppendLine("BEGIN");
                builder.AppendLine($"    THROW 50000, '{driftErrorMessage}', 1;");
                builder.AppendLine("END;");
                builder.AppendLine();
            }

            builder.AppendLine("-- No data rows were returned for this static entity; MERGE statement omitted.");
            return builder.ToString();
        }

        if (synchronizationMode == StaticSeedSynchronizationMode.ValidateThenApply)
        {
            builder.AppendLine("IF EXISTS (");
            builder.Append("    SELECT ");
            builder.AppendLine(sourceProjection);
            builder.AppendLine("    FROM");
            builder.AppendLine("    (");
            AppendValuesClause(builder, tableData, "        ");
            builder.Append("    ) AS Source (");
            builder.Append(columnList);
            builder.AppendLine(")");
            builder.AppendLine("    EXCEPT");
            builder.Append("    SELECT ");
            builder.AppendLine(targetProjection);
            builder.AppendLine($"    FROM {targetIdentifier} AS Existing");
            builder.AppendLine(")");
            builder.AppendLine("    OR EXISTS (");
            builder.Append("    SELECT ");
            builder.AppendLine(targetProjection);
            builder.AppendLine($"    FROM {targetIdentifier} AS Existing");
            builder.AppendLine("    EXCEPT");
            builder.Append("    SELECT ");
            builder.AppendLine(sourceProjection);
            builder.AppendLine("    FROM");
            builder.AppendLine("    (");
            AppendValuesClause(builder, tableData, "        ");
            builder.Append("    ) AS Source (");
            builder.Append(columnList);
            builder.AppendLine(")");
            builder.AppendLine(")");
            builder.AppendLine("BEGIN");
            builder.AppendLine($"    THROW 50000, '{driftErrorMessage}', 1;");
            builder.AppendLine("END;");
            builder.AppendLine();
        }

        var hasIdentity = definition.Columns.Any(column => column.IsIdentity);

        if (hasIdentity)
        {
            builder.AppendLine($"SET IDENTITY_INSERT {targetIdentifier} ON;");
            builder.AppendLine("GO");
            builder.AppendLine();
        }

        builder.AppendLine($"MERGE INTO {targetIdentifier} AS Target");
        builder.AppendLine("USING");
        builder.AppendLine("(");
        AppendValuesClause(builder, tableData, "    ");
        builder.Append(") AS Source (");
        builder.Append(columnList);
        builder.AppendLine(")");

        var primaryColumns = definition.Columns.Where(column => column.IsPrimaryKey).ToArray();
        if (primaryColumns.Length == 0)
        {
            throw new InvalidOperationException($"Static entity '{definition.Module}::{definition.LogicalName}' does not define a primary key.");
        }

        builder.Append("    ON ");
        builder.AppendLine(string.Join(
            " AND ",
            primaryColumns.Select(column =>
                $"Target.{QuoteColumn(column.TargetColumnName)} = Source.{QuoteColumn(column.TargetColumnName)}")));

        var updatableColumns = definition.Columns.Where(column => !column.IsPrimaryKey).ToArray();
        if (updatableColumns.Length > 0)
        {
            builder.AppendLine("WHEN MATCHED THEN UPDATE SET");
            for (var i = 0; i < updatableColumns.Length; i++)
            {
                var column = updatableColumns[i];
                builder.Append("    Target.");
                builder.Append(QuoteColumn(column.TargetColumnName));
                builder.Append(" = Source.");
                builder.Append(QuoteColumn(column.TargetColumnName));
                if (i < updatableColumns.Length - 1)
                {
                    builder.Append(',');
                }

                builder.AppendLine();
            }
        }

        builder.Append("WHEN NOT MATCHED THEN INSERT (");
        builder.Append(string.Join(", ", definition.Columns.Select(column => QuoteColumn(column.TargetColumnName))));
        builder.AppendLine(")");
        builder.Append("    VALUES (");
        builder.Append(string.Join(", ", columnNames.Select(name => $"Source.{name}")));
        builder.AppendLine(");");

        if (synchronizationMode == StaticSeedSynchronizationMode.Authoritative)
        {
            builder.AppendLine("WHEN NOT MATCHED BY SOURCE THEN DELETE;");
        }

        builder.AppendLine();
        builder.AppendLine("GO");

        if (hasIdentity)
        {
            builder.AppendLine();
            builder.AppendLine($"SET IDENTITY_INSERT {targetIdentifier} OFF;");
            builder.AppendLine("GO");
        }

        return builder.ToString();
    }

    private string QuoteColumn(string columnName)
        => _scriptFormatter.QuoteIdentifier(columnName, _formatOptions);

    private void AppendValuesClause(StringBuilder builder, StaticEntityTableData tableData, string indent)
    {
        builder.Append(indent);
        builder.AppendLine("VALUES");

        var definition = tableData.Definition;
        var rows = tableData.Rows;
        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            builder.Append(indent);
            builder.Append("    (");
            for (var j = 0; j < definition.Columns.Length; j++)
            {
                if (j > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(_literalFormatter.FormatValue(row.Values[j]));
            }

            builder.Append(')');
            if (i < rows.Length - 1)
            {
                builder.Append(',');
            }

            builder.AppendLine();
        }
    }
}
