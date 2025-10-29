using System;
using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Linq;
using Osm.Domain.Sql;

namespace Osm.Pipeline.Profiling;

internal sealed class NullCountQueryBuilder
{
    public void Configure(DbCommand command, TableProfilingPlan plan, bool useSampling, int sampleSize)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        if (plan.Columns.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Profiling plan does not contain any columns.", nameof(plan));
        }

        command.Parameters.Clear();
        command.CommandText = BuildCommandText(plan.Schema, plan.Table, plan.Columns, useSampling);

        if (useSampling)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@SampleSize";
            parameter.DbType = DbType.Int32;
            parameter.Value = sampleSize;
            command.Parameters.Add(parameter);
        }
    }

    internal static string BuildCommandText(string schema, string table, ImmutableArray<string> columns, bool useSampling)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("WITH Source AS (");
        builder.Append("    SELECT ");
        if (useSampling)
        {
            builder.Append("TOP (@SampleSize) ");
        }

        builder.Append(string.Join(", ", columns.Select(SqlIdentifierFormatter.Quote)));
        builder.AppendLine();
        builder.Append("    FROM ").Append(SqlIdentifierFormatter.Qualify(schema, table)).Append(" WITH (NOLOCK)");
        if (useSampling)
        {
            builder.AppendLine();
            builder.AppendLine("    ORDER BY (SELECT NULL)");
        }

        builder.AppendLine(")");
        builder.AppendLine("SELECT ColumnName, NullCount");
        builder.AppendLine("FROM (");
        for (var i = 0; i < columns.Length; i++)
        {
            if (i > 0)
            {
                builder.AppendLine("    UNION ALL");
            }

            var column = SqlIdentifierFormatter.Quote(columns[i]);
            builder.Append("    SELECT '");
            builder.Append(columns[i]);
            builder.Append("' AS ColumnName, SUM(CASE WHEN ");
            builder.Append(column);
            builder.Append(" IS NULL THEN 1 ELSE 0 END) AS NullCount");
            builder.AppendLine();
            builder.Append("    FROM Source");
            builder.AppendLine();
        }

        builder.AppendLine(") AS results;");
        return builder.ToString();
    }
}
