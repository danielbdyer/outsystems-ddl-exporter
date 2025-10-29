using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Linq;
using Osm.Domain.Sql;

namespace Osm.Pipeline.Profiling;

internal sealed class ForeignKeyProbeQueryBuilder
{
    public void ConfigureRealityCommand(DbCommand command, TableProfilingPlan plan, bool useSampling, int sampleSize)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        if (plan.ForeignKeys.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Profiling plan does not contain any foreign key candidates.", nameof(plan));
        }

        var sourceColumns = plan.ForeignKeys
            .Select(static fk => fk.Column)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(SqlIdentifierFormatter.Quote)
            .ToArray();

        command.Parameters.Clear();
        command.CommandText = BuildRealityCommandText(plan.Schema, plan.Table, sourceColumns, plan.ForeignKeys, useSampling, command);

        if (useSampling)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@SampleSize";
            parameter.DbType = DbType.Int32;
            parameter.Value = sampleSize;
            command.Parameters.Add(parameter);
        }
    }

    public void ConfigureMetadataCommand(DbCommand command, string schema, string table)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        command.Parameters.Clear();
        command.CommandText = BuildMetadataCommandText();

        var schemaParameter = command.CreateParameter();
        schemaParameter.ParameterName = "@SchemaName";
        schemaParameter.DbType = DbType.String;
        schemaParameter.Value = schema;
        command.Parameters.Add(schemaParameter);

        var tableParameter = command.CreateParameter();
        tableParameter.ParameterName = "@TableName";
        tableParameter.DbType = DbType.String;
        tableParameter.Value = table;
        command.Parameters.Add(tableParameter);
    }

    internal static string BuildRealityCommandText(
        string schema,
        string table,
        IReadOnlyCollection<string> sourceColumns,
        ImmutableArray<ForeignKeyPlan> candidates,
        bool useSampling,
        DbCommand command)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("WITH Source AS (");
        builder.Append("    SELECT ");
        if (useSampling)
        {
            builder.Append("TOP (@SampleSize) ");
        }

        builder.Append(string.Join(", ", sourceColumns));
        builder.AppendLine();
        builder.Append("    FROM ").Append(SqlIdentifierFormatter.Qualify(schema, table)).Append(" WITH (NOLOCK)");
        if (useSampling)
        {
            builder.AppendLine();
            builder.AppendLine("    ORDER BY (SELECT NULL)");
        }

        builder.AppendLine(")");
        builder.AppendLine("SELECT CandidateId, HasOrphans");
        builder.AppendLine("FROM (");
        for (var i = 0; i < candidates.Length; i++)
        {
            if (i > 0)
            {
                builder.AppendLine("    UNION ALL");
            }

            var parameter = command.CreateParameter();
            parameter.ParameterName = $"@fk{i}";
            parameter.DbType = DbType.String;
            parameter.Value = candidates[i].Key;
            command.Parameters.Add(parameter);

            builder.Append("    SELECT ");
            builder.Append(parameter.ParameterName);
            builder.Append(" AS CandidateId, CASE WHEN EXISTS (SELECT 1 FROM Source AS source LEFT JOIN ");
            builder.Append(SqlIdentifierFormatter.Qualify(candidates[i].TargetSchema, candidates[i].TargetTable));
            builder.Append(" AS target WITH (NOLOCK) ON source.");
            builder.Append(SqlIdentifierFormatter.Quote(candidates[i].Column));
            builder.Append(" = target.");
            builder.Append(SqlIdentifierFormatter.Quote(candidates[i].TargetColumn));
            builder.Append(" WHERE source.");
            builder.Append(SqlIdentifierFormatter.Quote(candidates[i].Column));
            builder.Append(" IS NOT NULL AND target.");
            builder.Append(SqlIdentifierFormatter.Quote(candidates[i].TargetColumn));
            builder.Append(" IS NULL) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasOrphans");
            builder.AppendLine();
        }

        builder.AppendLine(") AS results;");
        return builder.ToString();
    }

    internal static string BuildMetadataCommandText()
    {
        return @"SELECT
    parentColumn.name AS ColumnName,
    targetSchema.name AS TargetSchema,
    targetTable.name AS TargetTable,
    targetColumn.name AS TargetColumn,
    fk.is_not_trusted AS IsNotTrusted,
    fk.is_disabled AS IsDisabled
FROM sys.foreign_keys AS fk
JOIN sys.tables AS parentTable ON fk.parent_object_id = parentTable.object_id
JOIN sys.schemas AS parentSchema ON parentTable.schema_id = parentSchema.schema_id
JOIN sys.foreign_key_columns AS fkc ON fk.object_id = fkc.constraint_object_id
JOIN sys.columns AS parentColumn ON fkc.parent_object_id = parentColumn.object_id AND fkc.parent_column_id = parentColumn.column_id
JOIN sys.tables AS targetTable ON fk.referenced_object_id = targetTable.object_id
JOIN sys.schemas AS targetSchema ON targetTable.schema_id = targetSchema.schema_id
JOIN sys.columns AS targetColumn ON fkc.referenced_object_id = targetColumn.object_id AND fkc.referenced_column_id = targetColumn.column_id
WHERE parentSchema.name = @SchemaName AND parentTable.name = @TableName;";
    }
}
