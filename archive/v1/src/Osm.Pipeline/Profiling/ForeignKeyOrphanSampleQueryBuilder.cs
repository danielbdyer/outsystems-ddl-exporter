using System;
using System.Collections.Immutable;
using System.Data.Common;
using System.Linq;
using System.Text;
using Osm.Emission.Formatting;

namespace Osm.Pipeline.Profiling;

internal sealed class ForeignKeyOrphanSampleQueryBuilder
{
    private const int DefaultSampleLimit = 10;

    public void Configure(
        DbCommand command,
        string schema,
        string table,
        ForeignKeyPlan candidate,
        ImmutableArray<string> primaryKeyColumns,
        bool useSampling,
        int samplingParameter,
        int sampleLimit = DefaultSampleLimit)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        command.Parameters.Clear();
        command.CommandText = BuildCommandText(
            schema,
            table,
            candidate,
            primaryKeyColumns,
            useSampling,
            samplingParameter,
            sampleLimit,
            command);
    }

    internal static string BuildCommandText(
        string schema,
        string table,
        ForeignKeyPlan candidate,
        ImmutableArray<string> primaryKeyColumns,
        bool useSampling,
        int samplingParameter,
        int sampleLimit,
        DbCommand command)
    {
        var builder = new StringBuilder();
        var qualifiedTable = SqlIdentifierFormatter.Qualify(schema, table);
        var quotedColumn = SqlIdentifierFormatter.Quote(candidate.Column);
        var targetTable = SqlIdentifierFormatter.Qualify(candidate.TargetSchema, candidate.TargetTable);
        var targetColumn = SqlIdentifierFormatter.Quote(candidate.TargetColumn);

        if (primaryKeyColumns.IsDefaultOrEmpty)
        {
            builder.AppendLine("SELECT NULL AS [_no_pk_], NULL AS [OrphanValue], CAST(0 AS BIGINT) AS [TotalOrphans] WHERE 1 = 0;");
            return builder.ToString();
        }

        builder.AppendLine("WITH Source AS (");
        builder.Append("    SELECT ");
        if (useSampling)
        {
            builder.Append("TOP (@SampleSize) ");
        }

        builder.Append(string.Join(", ", primaryKeyColumns.Select(SqlIdentifierFormatter.Quote)));
        builder.Append(", ");
        builder.Append(quotedColumn);
        builder.AppendLine(" AS [OrphanValue]");
        builder.Append("    FROM ").Append(qualifiedTable).Append(" WITH (NOLOCK)");
        if (useSampling)
        {
            builder.AppendLine();
            builder.AppendLine("    ORDER BY (SELECT NULL)");
        }
        else
        {
            builder.AppendLine();
        }
        builder.AppendLine(")");

        if (useSampling)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@SampleSize";
            parameter.Value = samplingParameter;
            command.Parameters.Add(parameter);
        }

        builder.AppendLine("SELECT TOP (@SampleLimit)");
        builder.Append("    ");
        builder.Append(string.Join(", ", primaryKeyColumns.Select(SqlIdentifierFormatter.Quote)));
        builder.AppendLine(", source.[OrphanValue],");
        builder.AppendLine("    (SELECT COUNT_BIG(*)");
        builder.AppendLine("     FROM Source AS innerSource");
        builder.Append("     LEFT JOIN ").Append(targetTable).Append(" AS innerTarget WITH (NOLOCK)");
        builder.Append(" ON innerSource.[OrphanValue] = innerTarget.").Append(targetColumn).AppendLine();
        builder.Append("     WHERE innerSource.[OrphanValue] IS NOT NULL AND innerTarget.").Append(targetColumn).AppendLine(" IS NULL");
        builder.AppendLine("    ) AS [TotalOrphans]");
        builder.AppendLine("FROM Source AS source");
        builder.Append("LEFT JOIN ").Append(targetTable).Append(" AS target WITH (NOLOCK)");
        builder.Append(" ON source.[OrphanValue] = target.").Append(targetColumn).AppendLine();
        builder.Append("WHERE source.[OrphanValue] IS NOT NULL AND target.").Append(targetColumn).AppendLine(" IS NULL");
        builder.Append("ORDER BY ");
        builder.Append(string.Join(", ", primaryKeyColumns.Select(SqlIdentifierFormatter.Quote)));
        builder.AppendLine(";");

        var limitParameter = command.CreateParameter();
        limitParameter.ParameterName = "@SampleLimit";
        limitParameter.Value = sampleLimit;
        command.Parameters.Add(limitParameter);

        return builder.ToString();
    }
}
