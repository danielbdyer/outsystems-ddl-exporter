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
        }
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

            var message =
                $"Orphaned rows detected for {fromCoordinate} referencing {toCoordinate}; remediate before enabling enforcement.";

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
