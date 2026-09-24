using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;

namespace Osm.Validation.Profiling;

public interface IProfilingInsightGenerator
{
    ImmutableArray<ProfilingInsight> Generate(ProfileSnapshot snapshot);
}

public sealed class ProfilingInsightGenerator : IProfilingInsightGenerator
{
    public ImmutableArray<ProfilingInsight> Generate(ProfileSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        var builder = ImmutableArray.CreateBuilder<ProfilingInsight>();

        EvaluateNullabilityInsights(snapshot.Columns, builder);
        EvaluateDuplicateInsights(snapshot.UniqueCandidates, snapshot.CompositeUniqueCandidates, builder);
        EvaluateForeignKeyInsights(snapshot.ForeignKeys, builder);

        return builder.ToImmutable();
    }

    private static void EvaluateNullabilityInsights(
        ImmutableArray<ColumnProfile> columns,
        ImmutableArray<ProfilingInsight>.Builder builder)
    {
        var notNullCandidates = new Dictionary<(SchemaName Schema, TableName Table), List<ColumnProfile>>();

        foreach (var column in columns)
        {
            var coordinateResult = ProfilingInsightCoordinate.Create(column.Schema, column.Table, column.Column);
            var coordinate = coordinateResult.IsSuccess ? coordinateResult.Value : null;
            var coordinateText = FormatCoordinate(column.Schema, column.Table, column.Column);

            TryAddEvidenceInsight(
                builder,
                column.NullCountStatus,
                coordinate,
                string.Format(CultureInfo.InvariantCulture, "Null count probe for {0}", coordinateText));

            if (column.IsComputed)
            {
                var computedResult = ProfilingInsight.Create(
                    ProfilingInsightSeverity.Info,
                    ProfilingInsightCategory.ComputedColumn,
                    $"Computed column {coordinateText} is excluded from tightening heuristics.",
                    coordinate);

                if (computedResult.IsSuccess)
                {
                    builder.Add(computedResult.Value);
                }

                continue;
            }

            if (ShouldRecommendNotNull(column))
            {
                var key = (column.Schema, column.Table);
                if (!notNullCandidates.TryGetValue(key, out var list))
                {
                    list = new List<ColumnProfile>();
                    notNullCandidates[key] = list;
                }

                list.Add(column);
            }
        }

        EmitNullabilityRecommendations(builder, notNullCandidates);
    }

    private static void EvaluateDuplicateInsights(
        ImmutableArray<UniqueCandidateProfile> uniqueCandidates,
        ImmutableArray<CompositeUniqueCandidateProfile> compositeCandidates,
        ImmutableArray<ProfilingInsight>.Builder builder)
    {
        foreach (var candidate in uniqueCandidates)
        {
            var coordinateResult = ProfilingInsightCoordinate.Create(candidate.Schema, candidate.Table, candidate.Column);
            var coordinate = coordinateResult.IsSuccess ? coordinateResult.Value : null;
            var coordinateText = FormatCoordinate(candidate.Schema, candidate.Table, candidate.Column);

            TryAddEvidenceInsight(
                builder,
                candidate.ProbeStatus,
                coordinate,
                string.Format(CultureInfo.InvariantCulture, "Uniqueness probe for {0}", coordinateText));

            if (!candidate.HasDuplicate)
            {
                continue;
            }

            var message = $"Unique candidate {coordinateText} contains duplicates in profiling data.";

            var insightResult = ProfilingInsight.Create(
                ProfilingInsightSeverity.Warning,
                ProfilingInsightCategory.Uniqueness,
                message,
                coordinate);

            if (insightResult.IsSuccess)
            {
                builder.Add(insightResult.Value);
            }
        }

        foreach (var candidate in compositeCandidates.Where(static candidate => candidate.HasDuplicate))
        {
            var coordinateResult = ProfilingInsightCoordinate.Create(candidate.Schema, candidate.Table);
            var coordinate = coordinateResult.IsSuccess ? coordinateResult.Value : null;
            var columnList = string.Join(", ", candidate.Columns.Select(static column => column.Value));
            var message = $"Composite unique candidate {FormatCoordinate(candidate.Schema, candidate.Table, null)} [{columnList}] contains duplicates in profiling data.";

            var insightResult = ProfilingInsight.Create(
                ProfilingInsightSeverity.Warning,
                ProfilingInsightCategory.Uniqueness,
                message,
                coordinate);

            if (insightResult.IsSuccess)
            {
                builder.Add(insightResult.Value);
            }
        }
    }

    private static void EvaluateForeignKeyInsights(
        ImmutableArray<ForeignKeyReality> foreignKeys,
        ImmutableArray<ProfilingInsight>.Builder builder)
    {
        foreach (var foreignKey in foreignKeys)
        {
            var fromCoordinate = FormatCoordinate(
                foreignKey.Reference.FromSchema,
                foreignKey.Reference.FromTable,
                foreignKey.Reference.FromColumn);
            var toCoordinate = FormatCoordinate(
                foreignKey.Reference.ToSchema,
                foreignKey.Reference.ToTable,
                foreignKey.Reference.ToColumn);
            var coordinateResult = ProfilingInsightCoordinate.Create(
                foreignKey.Reference.FromSchema,
                foreignKey.Reference.FromTable,
                foreignKey.Reference.FromColumn,
                foreignKey.Reference.ToSchema,
                foreignKey.Reference.ToTable,
                foreignKey.Reference.ToColumn);
            var coordinate = coordinateResult.IsSuccess ? coordinateResult.Value : null;

            TryAddEvidenceInsight(
                builder,
                foreignKey.ProbeStatus,
                coordinate,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Foreign key probe for {0} -> {1}",
                    fromCoordinate,
                    toCoordinate));

            if (!foreignKey.HasOrphan)
            {
                continue;
            }

            var message = BuildOrphanInsightMessage(foreignKey, fromCoordinate, toCoordinate);

            var insightResult = ProfilingInsight.Create(
                ProfilingInsightSeverity.Warning,
                ProfilingInsightCategory.ForeignKey,
                message,
                coordinate);

            if (insightResult.IsSuccess)
            {
                builder.Add(insightResult.Value);
            }
        }
    }

    private static void TryAddEvidenceInsight(
        ImmutableArray<ProfilingInsight>.Builder builder,
        ProfilingProbeStatus status,
        ProfilingInsightCoordinate? coordinate,
        string probeDescription)
    {
        string? message = status.Outcome switch
        {
            ProfilingProbeOutcome.FallbackTimeout => string.Format(
                CultureInfo.InvariantCulture,
                "{0} timed out; sampled {1:N0} rows at {2:O}. Profiling evidence may be incomplete.",
                probeDescription,
                status.SampleSize,
                status.CapturedAtUtc),
            ProfilingProbeOutcome.Cancelled => string.Format(
                CultureInfo.InvariantCulture,
                "{0} was cancelled; sampled {1:N0} rows at {2:O}. Profiling evidence may be incomplete.",
                probeDescription,
                status.SampleSize,
                status.CapturedAtUtc),
            ProfilingProbeOutcome.AmbiguousMapping => string.Format(
                CultureInfo.InvariantCulture,
                "{0} was skipped because the referenced entity could not be resolved unambiguously at {1:O}. Profiling evidence is unavailable.",
                probeDescription,
                status.CapturedAtUtc),
            _ => null
        };

        if (message is null)
        {
            return;
        }

        var insightResult = ProfilingInsight.Create(
            ProfilingInsightSeverity.Warning,
            ProfilingInsightCategory.Evidence,
            message,
            coordinate);

        if (insightResult.IsSuccess)
        {
            builder.Add(insightResult.Value);
        }
    }

    private static bool ShouldRecommendNotNull(ColumnProfile column)
    {
        if (column is null)
        {
            return false;
        }

        if (!column.IsNullablePhysical || column.IsPrimaryKey || column.IsUniqueKey)
        {
            return false;
        }

        if (column.NullCountStatus.Outcome is ProfilingProbeOutcome.FallbackTimeout or ProfilingProbeOutcome.Cancelled)
        {
            return false;
        }

        return column.NullCount == 0 && column.RowCount > 0;
    }

    private static void EmitNullabilityRecommendations(
        ImmutableArray<ProfilingInsight>.Builder builder,
        IDictionary<(SchemaName Schema, TableName Table), List<ColumnProfile>> candidates)
    {
        foreach (var ((schema, table), columns) in candidates)
        {
            if (columns is null || columns.Count == 0)
            {
                continue;
            }

            var orderedColumns = columns
                .OrderBy(static c => c.Column.Value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ProfilingInsightCoordinate? coordinate;
            var coordinateResult = orderedColumns.Count == 1
                ? ProfilingInsightCoordinate.Create(schema, table, orderedColumns[0].Column)
                : ProfilingInsightCoordinate.Create(schema, table);

            coordinate = coordinateResult.IsSuccess ? coordinateResult.Value : null;

            var message = BuildNullabilityRecommendationMessage(schema, table, orderedColumns);

            var insightResult = ProfilingInsight.Create(
                ProfilingInsightSeverity.Recommendation,
                ProfilingInsightCategory.Nullability,
                message,
                coordinate);

            if (insightResult.IsSuccess)
            {
                builder.Add(insightResult.Value);
            }
        }
    }

    private static string BuildNullabilityRecommendationMessage(
        SchemaName schema,
        TableName table,
        IReadOnlyList<ColumnProfile> columns)
    {
        if (columns.Count == 1)
        {
            var column = columns[0];
            var coordinateText = FormatCoordinate(schema, table, column.Column);
            return string.Format(
                CultureInfo.InvariantCulture,
                "Column {0} contains 0 NULL values across {1:N0} rows; tighten to NOT NULL.",
                coordinateText,
                column.RowCount);
        }

        var columnList = string.Join(
            ", ",
            columns.Select(static c => c.Column.Value));

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}.{1} has {2} columns with 0 NULL values: {3}. Tighten to NOT NULL.",
            schema.Value,
            table.Value,
            columns.Count,
            columnList);
    }

    private static string BuildOrphanInsightMessage(
        ForeignKeyReality foreignKey,
        string fromCoordinate,
        string toCoordinate)
    {
        var summary = FormatOrphanSummary(foreignKey);
        var samples = FormatOrphanSamples(foreignKey.OrphanSample);

        var message = string.Format(
            CultureInfo.InvariantCulture,
            "Orphaned rows detected for {0} referencing {1}; {2}.",
            fromCoordinate,
            toCoordinate,
            summary);

        if (!string.IsNullOrWhiteSpace(samples))
        {
            message += $" Sample rows: {samples}";
        }

        return message;
    }

    private static string FormatOrphanSummary(ForeignKeyReality foreignKey)
    {
        if (foreignKey.OrphanSample is { SampleRows.Length: > 0 } sample)
        {
            var suffix = sample.IsTruncated ? ", truncated" : string.Empty;
            return string.Format(
                CultureInfo.InvariantCulture,
                "showing {0} of {1:N0} orphan rows{2}",
                sample.SampleRows.Length,
                sample.TotalOrphans,
                suffix);
        }

        var orphanCount = Math.Max(foreignKey.OrphanCount, 0);
        return string.Format(CultureInfo.InvariantCulture, "{0:N0} orphan rows detected", orphanCount);
    }

    private static string FormatOrphanSamples(ForeignKeyOrphanSample? sample)
    {
        if (sample is not { SampleRows.Length: > 0 })
        {
            return string.Empty;
        }

        return string.Join(
            "; ",
            sample.SampleRows
                .Select(static row => row.ToString())
                .Where(static s => !string.IsNullOrWhiteSpace(s)));
    }

    private static string FormatCoordinate(SchemaName schema, TableName table, ColumnName? column)
    {
        var parts = new List<string>(3)
        {
            schema.Value,
            table.Value
        };

        if (column.HasValue)
        {
            parts.Add(column.Value.Value);
        }

        return string.Join('.', parts);
    }
}
