using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Osm.Domain.Configuration;
using Osm.Emission.Formatting;
using Osm.Smo;
using Osm.Smo.PerTableEmission;

namespace Osm.Emission.Seeds;

public sealed class StaticSeedSqlBuilder
{
    private const int DefaultBatchSize = 1000;
    private readonly SqlLiteralFormatter _literalFormatter;
    private readonly IdentifierFormatter _identifierFormatter;
    private readonly SmoFormatOptions _formatOptions;
    private readonly int _batchSize;

    public StaticSeedSqlBuilder(SqlLiteralFormatter literalFormatter)
        : this(literalFormatter, new IdentifierFormatter(), SmoFormatOptions.Default, DefaultBatchSize)
    {
    }

    internal StaticSeedSqlBuilder(
        SqlLiteralFormatter literalFormatter,
        IdentifierFormatter identifierFormatter,
        SmoFormatOptions formatOptions,
        int batchSize = DefaultBatchSize)
    {
        _literalFormatter = literalFormatter ?? throw new ArgumentNullException(nameof(literalFormatter));
        _identifierFormatter = identifierFormatter ?? throw new ArgumentNullException(nameof(identifierFormatter));
        _formatOptions = formatOptions ?? throw new ArgumentNullException(nameof(formatOptions));
        _batchSize = batchSize > 0 ? batchSize : DefaultBatchSize;
    }

    public string BuildBlock(
        StaticEntityTableData tableData,
        StaticSeedSynchronizationMode synchronizationMode,
        ModuleValidationOverrides? validationOverrides = null)
    {
        if (tableData is null)
        {
            throw new ArgumentNullException(nameof(tableData));
        }

        validationOverrides ??= ModuleValidationOverrides.Empty;

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
            .Select(column => QuoteColumn(column.EffectiveColumnName))
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
                builder.AppendLine("BEGIN;");
                builder.AppendLine($"    THROW 50000, '{driftErrorMessage}', 1;");
                builder.AppendLine("END");
                builder.AppendLine("GO");
                builder.AppendLine();
            }

            builder.AppendLine("-- No data rows were returned for this static entity; MERGE statement omitted.");
            return builder.ToString();
        }

        var hasIdentity = definition.Columns.Any(column => column.IsIdentity);

        // Check if we need to batch this table
        var requiresBatching = rows.Length > _batchSize;
        if (requiresBatching)
        {
            builder.AppendLine($"-- Note: Large table with {rows.Length} rows will be processed in batches of {_batchSize}");
            builder.AppendLine();
        }

        if (synchronizationMode == StaticSeedSynchronizationMode.ValidateThenApply)
        {
            builder.AppendLine($"IF EXISTS (SELECT 1 FROM {targetIdentifier})");
            builder.AppendLine("    AND (EXISTS (");
            builder.Append("        SELECT ");
            builder.AppendLine(sourceProjection);
            builder.AppendLine("        FROM");
            builder.AppendLine("        (");
            AppendValuesClause(builder, tableData, "            ");
            builder.Append("        ) AS Source (");
            builder.Append(columnList);
            builder.AppendLine(")");
            builder.AppendLine("        EXCEPT");
            builder.Append("        SELECT ");
            builder.AppendLine(targetProjection);
            builder.AppendLine($"        FROM {targetIdentifier} AS Existing");
            builder.AppendLine("    )");
            builder.AppendLine("    OR EXISTS (");
            builder.Append("        SELECT ");
            builder.AppendLine(targetProjection);
            builder.AppendLine($"        FROM {targetIdentifier} AS Existing");
            builder.AppendLine("        EXCEPT");
            builder.Append("        SELECT ");
            builder.AppendLine(sourceProjection);
            builder.AppendLine("        FROM");
            builder.AppendLine("        (");
            AppendValuesClause(builder, tableData, "            ");
            builder.Append("        ) AS Source (");
            builder.Append(columnList);
            builder.AppendLine(")");
            builder.AppendLine("    ))");
            builder.AppendLine("BEGIN;");
            builder.AppendLine($"    THROW 50000, '{driftErrorMessage}', 1;");
            builder.AppendLine("END");
            builder.AppendLine("GO");
            builder.AppendLine();
        }

        if (hasIdentity)
        {
            builder.AppendLine($"SET IDENTITY_INSERT {targetIdentifier} ON;");
            builder.AppendLine("GO");
            builder.AppendLine();
        }

        // Determine primary and updatable columns
        var primaryColumns = definition.Columns.Where(column => column.IsPrimaryKey).ToArray();
        if (primaryColumns.Length == 0)
        {
            // Check if missing primary key is allowed via configuration override
            var allowMissingPk = validationOverrides.AllowsMissingPrimaryKey(definition.Module, definition.LogicalName);
            if (!allowMissingPk)
            {
                throw new InvalidOperationException(
                    $"Static entity '{definition.Module}::{definition.LogicalName}' does not define a primary key. " +
                    $"To allow this entity without a primary key, add it to the 'allowMissingPrimaryKey' configuration for module '{definition.Module}'.");
            }

            // If override allows missing PK, use all columns as matching criteria (fallback behavior)
            primaryColumns = definition.Columns.ToArray();
        }

        var updatableColumns = definition.Columns.Where(column => !column.IsPrimaryKey).ToArray();

        // Generate INSERT or MERGE statements (batched if necessary)
        if (requiresBatching)
        {
            var batches = PartitionRows(rows, _batchSize).ToArray();
            for (var batchIndex = 0; batchIndex < batches.Length; batchIndex++)
            {
                var batch = batches[batchIndex];
                var batchData = StaticEntityTableData.Create(definition, batch);
                
                builder.AppendLine($"PRINT 'Applying batch {batchIndex + 1}/{batches.Length} for {targetIdentifier} ({batch.Length} rows)';");
                builder.AppendLine();
                
                if (synchronizationMode == StaticSeedSynchronizationMode.NonDestructive)
                {
                    AppendInsertStatement(builder, batchData, targetIdentifier, columnNames, columnList);
                }
                else
                {
                    AppendMergeStatement(builder, batchData, targetIdentifier, columnNames, columnList, 
                        primaryColumns, updatableColumns, synchronizationMode);
                }
            }
        }
        else
        {
            if (synchronizationMode == StaticSeedSynchronizationMode.NonDestructive)
            {
                AppendInsertStatement(builder, tableData, targetIdentifier, columnNames, columnList);
            }
            else
            {
                AppendMergeStatement(builder, tableData, targetIdentifier, columnNames, columnList,
                    primaryColumns, updatableColumns, synchronizationMode);
            }
        }

        if (hasIdentity)
        {
            builder.AppendLine($"SET IDENTITY_INSERT {targetIdentifier} OFF;");
            builder.AppendLine("GO");
        }

        return builder.ToString();
    }

    private void AppendInsertStatement(
        StringBuilder builder,
        StaticEntityTableData tableData,
        string targetIdentifier,
        string[] columnNames,
        string columnList)
    {
        builder.Append("INSERT INTO ");
        builder.Append(targetIdentifier);
        builder.Append(" (");
        builder.Append(columnList);
        builder.AppendLine(")");
        AppendValuesClause(builder, tableData, "");
        builder.AppendLine(";");
        builder.AppendLine();
        builder.AppendLine("GO");
        builder.AppendLine();
    }

    private void AppendMergeStatement(
        StringBuilder builder,
        StaticEntityTableData tableData,
        string targetIdentifier,
        string[] columnNames,
        string columnList,
        StaticEntitySeedColumn[] primaryColumns,
        StaticEntitySeedColumn[] updatableColumns,
        StaticSeedSynchronizationMode synchronizationMode)
    {
        builder.AppendLine($"MERGE INTO {targetIdentifier} AS Target");
        builder.AppendLine("USING");
        builder.AppendLine("(");
        AppendValuesClause(builder, tableData, "    ");
        builder.Append(") AS Source (");
        builder.Append(columnList);
        builder.AppendLine(")");

        builder.Append("    ON ");
        builder.AppendLine(string.Join(
            " AND ",
            primaryColumns.Select(column =>
                $"Target.{QuoteColumn(column.EffectiveColumnName)} = Source.{QuoteColumn(column.EffectiveColumnName)}")));

        if (updatableColumns.Length > 0)
        {
            builder.AppendLine("WHEN MATCHED THEN UPDATE SET");
            for (var i = 0; i < updatableColumns.Length; i++)
            {
                var column = updatableColumns[i];
                builder.Append("    Target.");
                builder.Append(QuoteColumn(column.EffectiveColumnName));
                builder.Append(" = Source.");
                builder.Append(QuoteColumn(column.EffectiveColumnName));
                if (i < updatableColumns.Length - 1)
                {
                    builder.Append(',');
                }

                builder.AppendLine();
            }
        }

        builder.Append("WHEN NOT MATCHED THEN INSERT (");
        builder.Append(string.Join(", ", tableData.Definition.Columns.Select(column => QuoteColumn(column.EffectiveColumnName))));
        builder.AppendLine(")");
        builder.Append("    VALUES (");
        builder.Append(string.Join(", ", columnNames.Select(name => $"Source.{name}")));
        builder.AppendLine(")");

        if (synchronizationMode == StaticSeedSynchronizationMode.Authoritative)
        {
            builder.AppendLine("WHEN NOT MATCHED BY SOURCE THEN DELETE;");
        }
        else
        {
            builder.AppendLine(";");
        }

        builder.AppendLine();
        builder.AppendLine("GO");
        builder.AppendLine();
    }

    private static ImmutableArray<StaticEntityRow>[] PartitionRows(
        ImmutableArray<StaticEntityRow> rows,
        int batchSize)
    {
        if (rows.IsDefaultOrEmpty)
        {
            return Array.Empty<ImmutableArray<StaticEntityRow>>();
        }

        var batchCount = (rows.Length + batchSize - 1) / batchSize;
        var result = new ImmutableArray<StaticEntityRow>[batchCount];

        for (var i = 0; i < batchCount; i++)
        {
            var start = i * batchSize;
            var length = Math.Min(batchSize, rows.Length - start);
            var builder = ImmutableArray.CreateBuilder<StaticEntityRow>(length);

            for (var j = 0; j < length; j++)
            {
                builder.Add(rows[start + j]);
            }

            result[i] = builder.MoveToImmutable();
        }

        return result;
    }

    private string QuoteColumn(string columnName)
        => _identifierFormatter.QuoteIdentifier(columnName, _formatOptions);

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
