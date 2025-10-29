using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Linq;
using Osm.Domain.Sql;

namespace Osm.Pipeline.Profiling;

internal sealed class UniqueCandidateQueryBuilder
{
    public void Configure(DbCommand command, TableProfilingPlan plan, bool useSampling, int sampleSize)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        if (plan.UniqueCandidates.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Profiling plan does not contain any unique candidates.", nameof(plan));
        }

        var columnSet = plan.UniqueCandidates
            .SelectMany(static candidate => candidate.Columns)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        command.Parameters.Clear();
        command.CommandText = BuildCommandText(plan.Schema, plan.Table, columnSet, plan.UniqueCandidates, useSampling, command);

        if (useSampling)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@SampleSize";
            parameter.DbType = DbType.Int32;
            parameter.Value = sampleSize;
            command.Parameters.Add(parameter);
        }
    }

    internal static string BuildCommandText(
        string schema,
        string table,
        IEnumerable<string> columnSet,
        ImmutableArray<UniqueCandidatePlan> candidates,
        bool useSampling,
        DbCommand command)
    {
        var builder = new System.Text.StringBuilder();
        var projectedColumns = columnSet.Select(SqlIdentifierFormatter.Quote).ToArray();
        builder.AppendLine("WITH Source AS (");
        builder.Append("    SELECT ");
        if (useSampling)
        {
            builder.Append("TOP (@SampleSize) ");
        }

        builder.Append(string.Join(", ", projectedColumns));
        builder.AppendLine();
        builder.Append("    FROM ").Append(SqlIdentifierFormatter.Qualify(schema, table)).Append(" WITH (NOLOCK)");
        if (useSampling)
        {
            builder.AppendLine();
            builder.AppendLine("    ORDER BY (SELECT NULL)");
        }

        builder.AppendLine(")");
        builder.AppendLine("SELECT CandidateId, HasDuplicates");
        builder.AppendLine("FROM (");
        for (var i = 0; i < candidates.Length; i++)
        {
            if (i > 0)
            {
                builder.AppendLine("    UNION ALL");
            }

            var parameter = command.CreateParameter();
            parameter.ParameterName = $"@candidate{i}";
            parameter.DbType = DbType.String;
            parameter.Value = candidates[i].Key;
            command.Parameters.Add(parameter);

            builder.Append("    SELECT ");
            builder.Append(parameter.ParameterName);
            builder.Append(" AS CandidateId, CASE WHEN EXISTS (SELECT 1 FROM Source GROUP BY ");
            builder.Append(string.Join(", ", candidates[i].Columns.Select(SqlIdentifierFormatter.Quote)));
            builder.Append(" HAVING COUNT(*) > 1) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasDuplicates");
            builder.AppendLine();
        }

        builder.AppendLine(") AS results;");
        return builder.ToString();
    }
}
