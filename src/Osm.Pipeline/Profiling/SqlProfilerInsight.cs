using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Profiling;

namespace Osm.Pipeline.Profiling;

public enum SqlProfilerInsightSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2
}

public sealed record SqlProfilerInsight(
    SqlProfilerInsightSeverity Severity,
    string Message,
    ImmutableArray<string> Details)
{
    public static SqlProfilerInsight Create(
        SqlProfilerInsightSeverity severity,
        string message,
        IEnumerable<string>? details = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Message must be provided.", nameof(message));
        }

        var detailArray = details is null
            ? ImmutableArray<string>.Empty
            : details
                .Where(static detail => !string.IsNullOrWhiteSpace(detail))
                .Select(static detail => detail.Trim())
                .ToImmutableArray();

        return new SqlProfilerInsight(severity, message.Trim(), detailArray);
    }
}

internal static class SqlProfilerInsightBuilder
{
    public static ImmutableArray<SqlProfilerInsight> FromSnapshot(ProfileSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        var insights = new List<SqlProfilerInsight>();

        BuildForeignKeyInsights(snapshot, insights);
        BuildUniqueCandidateInsights(snapshot, insights);

        return insights
            .OrderByDescending(static insight => insight.Severity)
            .ThenBy(static insight => insight.Message, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static void BuildForeignKeyInsights(ProfileSnapshot snapshot, ICollection<SqlProfilerInsight> insights)
    {
        foreach (var foreignKey in snapshot.ForeignKeys)
        {
            if (foreignKey.HasOrphan)
            {
                insights.Add(SqlProfilerInsight.Create(
                    SqlProfilerInsightSeverity.Error,
                    $"Orphaned values detected for foreign key {FormatForeignKey(foreignKey)}.",
                    new[]
                    {
                        $"Source column: {FormatColumn(foreignKey.Reference.FromSchema.Value, foreignKey.Reference.FromTable.Value, foreignKey.Reference.FromColumn.Value)}",
                        $"Target column: {FormatColumn(foreignKey.Reference.ToSchema.Value, foreignKey.Reference.ToTable.Value, foreignKey.Reference.ToColumn.Value)}"
                    }));
            }

            if (foreignKey.IsNoCheck)
            {
                insights.Add(SqlProfilerInsight.Create(
                    SqlProfilerInsightSeverity.Warning,
                    $"Foreign key {FormatForeignKey(foreignKey)} is marked WITH NOCHECK (not trusted).",
                    new[]
                    {
                        "SQL Server is not enforcing this constraint; consider enabling it after data remediation."
                    }));
            }
        }
    }

    private static void BuildUniqueCandidateInsights(ProfileSnapshot snapshot, ICollection<SqlProfilerInsight> insights)
    {
        foreach (var unique in snapshot.UniqueCandidates)
        {
            if (!unique.HasDuplicate)
            {
                continue;
            }

            insights.Add(SqlProfilerInsight.Create(
                SqlProfilerInsightSeverity.Warning,
                $"Duplicate values detected for unique candidate {FormatColumn(unique.Schema.Value, unique.Table.Value, unique.Column.Value)}.",
                new[]
                {
                    "Review the data set before enforcing a UNIQUE constraint on this column."
                }));
        }

        foreach (var composite in snapshot.CompositeUniqueCandidates)
        {
            if (!composite.HasDuplicate)
            {
                continue;
            }

            var columnList = string.Join(", ", composite.Columns.Select(static column => column.Value));
            insights.Add(SqlProfilerInsight.Create(
                SqlProfilerInsightSeverity.Warning,
                $"Duplicate values detected for composite unique candidate {FormatTable(composite.Schema.Value, composite.Table.Value)}.",
                new[]
                {
                    $"Columns: {columnList}"
                }));
        }
    }

    private static string FormatForeignKey(ForeignKeyReality foreignKey)
    {
        var from = FormatColumn(
            foreignKey.Reference.FromSchema.Value,
            foreignKey.Reference.FromTable.Value,
            foreignKey.Reference.FromColumn.Value);
        var to = FormatColumn(
            foreignKey.Reference.ToSchema.Value,
            foreignKey.Reference.ToTable.Value,
            foreignKey.Reference.ToColumn.Value);

        return FormattableString.Invariant($"{from} -> {to}");
    }

    private static string FormatColumn(string schema, string table, string column)
        => FormattableString.Invariant($"{schema}.{table}.{column}");

    private static string FormatTable(string schema, string table)
        => FormattableString.Invariant($"{schema}.{table}");
}
