using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Osm.Domain.Abstractions;
using Osm.Domain.ValueObjects;

namespace Osm.Domain.Profiling;

public interface IProfilingInsightAnalyzer
{
    ImmutableArray<ProfilingInsight> Analyze(ProfileSnapshot snapshot);
}

public sealed class ProfilingInsightAnalyzer : IProfilingInsightAnalyzer
{
    private readonly ProfilingInsightOptions _options;

    public ProfilingInsightAnalyzer(ProfilingInsightOptions? options = null)
    {
        _options = options ?? ProfilingInsightOptions.Default;
    }

    public ImmutableArray<ProfilingInsight> Analyze(ProfileSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        var builder = ImmutableArray.CreateBuilder<ProfilingInsight>();
        var columnLookup = snapshot.Columns.ToDictionary(
            profile => ColumnKey.From(profile.Schema, profile.Table, profile.Column),
            profile => profile,
            ColumnKeyComparer.Instance);

        foreach (var column in snapshot.Columns)
        {
            EvaluateHighNullDensity(column, builder);
            EvaluateNullFreeNullable(column, builder);
            EvaluatePhysicalNullViolation(column, builder);
        }

        foreach (var candidate in snapshot.UniqueCandidates)
        {
            EvaluateUniqueCandidate(candidate, columnLookup, builder);
        }

        foreach (var candidate in snapshot.CompositeUniqueCandidates)
        {
            EvaluateCompositeCandidate(candidate, builder);
        }

        foreach (var foreignKey in snapshot.ForeignKeys)
        {
            EvaluateForeignKey(foreignKey, builder);
        }

        var insights = builder.ToImmutable();
        if (insights.IsDefaultOrEmpty)
        {
            return ImmutableArray<ProfilingInsight>.Empty;
        }

        return insights
            .OrderByDescending(insight => insight.Severity)
            .ThenBy(insight => insight.Schema.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(insight => insight.Table.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(insight => FormatColumnList(insight.Columns), StringComparer.OrdinalIgnoreCase)
            .ThenBy(insight => insight.Code, StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
    }

    private void EvaluateHighNullDensity(ColumnProfile column, ImmutableArray<ProfilingInsight>.Builder builder)
    {
        if (column.IsComputed)
        {
            return;
        }

        if (column.RowCount < _options.MinimumRowCountForRatioInsights)
        {
            return;
        }

        if (!TryGetNullRatio(column, out var ratio))
        {
            return;
        }

        if (ratio < _options.HighNullRatioThreshold)
        {
            return;
        }

        var metadata = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
        metadata["rowCount"] = column.RowCount.ToString(CultureInfo.InvariantCulture);
        metadata["nullCount"] = column.NullCount.ToString(CultureInfo.InvariantCulture);
        metadata["nullRatio"] = ratio.ToString("P4", CultureInfo.InvariantCulture);

        var result = ProfilingInsight.Create(
            ProfilingInsightCodes.HighNullDensity,
            ProfilingInsightSeverity.Warning,
            column.Schema,
            column.Table,
            ImmutableArray.Create(column.Column),
            $"{column.NullCount:N0} of {column.RowCount:N0} rows are NULL ({ratio:P1}). Consider auditing defaults or tightening the data feed.",
            metadata.ToImmutable());

        TryAdd(builder, result);
    }

    private void EvaluateNullFreeNullable(ColumnProfile column, ImmutableArray<ProfilingInsight>.Builder builder)
    {
        if (!column.IsNullablePhysical)
        {
            return;
        }

        if (column.RowCount < _options.MinimumRowCountForOpportunities)
        {
            return;
        }

        if (!TryGetNullRatio(column, out var ratio))
        {
            return;
        }

        if (ratio > _options.NullFreeNullableThreshold)
        {
            return;
        }

        var metadata = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
        metadata["rowCount"] = column.RowCount.ToString(CultureInfo.InvariantCulture);
        metadata["nullCount"] = column.NullCount.ToString(CultureInfo.InvariantCulture);
        metadata["nullRatio"] = ratio.ToString("P4", CultureInfo.InvariantCulture);

        var result = ProfilingInsight.Create(
            ProfilingInsightCodes.NullFreeButNullable,
            ProfilingInsightSeverity.Info,
            column.Schema,
            column.Table,
            ImmutableArray.Create(column.Column),
            $"Column is nullable but profiling observed {column.NullCount:N0} NULL value(s) across {column.RowCount:N0} rows.",
            metadata.ToImmutable());

        TryAdd(builder, result);
    }

    private static void EvaluatePhysicalNullViolation(ColumnProfile column, ImmutableArray<ProfilingInsight>.Builder builder)
    {
        if (column.IsNullablePhysical)
        {
            return;
        }

        if (column.NullCount <= 0)
        {
            return;
        }

        var metadata = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
        metadata["rowCount"] = column.RowCount.ToString(CultureInfo.InvariantCulture);
        metadata["nullCount"] = column.NullCount.ToString(CultureInfo.InvariantCulture);

        var result = ProfilingInsight.Create(
            ProfilingInsightCodes.PhysicalNullViolations,
            ProfilingInsightSeverity.Critical,
            column.Schema,
            column.Table,
            ImmutableArray.Create(column.Column),
            $"Column is marked NOT NULL but profiling found {column.NullCount:N0} NULL value(s).", 
            metadata.ToImmutable());

        TryAdd(builder, result);
    }

    private void EvaluateUniqueCandidate(
        UniqueCandidateProfile candidate,
        IReadOnlyDictionary<ColumnKey, ColumnProfile> columnLookup,
        ImmutableArray<ProfilingInsight>.Builder builder)
    {
        var key = ColumnKey.From(candidate.Schema, candidate.Table, candidate.Column);
        columnLookup.TryGetValue(key, out var columnProfile);

        if (candidate.HasDuplicate)
        {
            var metadata = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
            metadata["hasPhysicalUnique"] = (columnProfile?.IsUniqueKey ?? false) ? "true" : "false";

            var result = ProfilingInsight.Create(
                ProfilingInsightCodes.UniqueCandidateDuplicates,
                ProfilingInsightSeverity.Warning,
                candidate.Schema,
                candidate.Table,
                ImmutableArray.Create(candidate.Column),
                "Duplicates detected for logical unique candidate.",
                metadata.ToImmutable());

            TryAdd(builder, result);
            return;
        }

        if (columnProfile is null)
        {
            return;
        }

        if (columnProfile.IsUniqueKey)
        {
            return;
        }

        if (columnProfile.RowCount < _options.MinimumRowCountForOpportunities)
        {
            return;
        }

        var opportunityMetadata = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
        opportunityMetadata["rowCount"] = columnProfile.RowCount.ToString(CultureInfo.InvariantCulture);

        var opportunity = ProfilingInsight.Create(
            ProfilingInsightCodes.UniqueCandidateOpportunity,
            ProfilingInsightSeverity.Info,
            candidate.Schema,
            candidate.Table,
            ImmutableArray.Create(candidate.Column),
            "No duplicates observed for logical unique candidate; consider enforcing a unique index.",
            opportunityMetadata.ToImmutable());

        TryAdd(builder, opportunity);
    }

    private void EvaluateCompositeCandidate(
        CompositeUniqueCandidateProfile candidate,
        ImmutableArray<ProfilingInsight>.Builder builder)
    {
        if (candidate.HasDuplicate)
        {
            var metadata = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
            metadata["columnCount"] = candidate.Columns.Length.ToString(CultureInfo.InvariantCulture);

            var result = ProfilingInsight.Create(
                ProfilingInsightCodes.CompositeUniqueDuplicates,
                ProfilingInsightSeverity.Warning,
                candidate.Schema,
                candidate.Table,
                candidate.Columns,
                "Duplicates detected across composite unique candidate columns.",
                metadata.ToImmutable());

            TryAdd(builder, result);
            return;
        }

        var opportunity = ProfilingInsight.Create(
            ProfilingInsightCodes.CompositeUniqueOpportunity,
            ProfilingInsightSeverity.Info,
            candidate.Schema,
            candidate.Table,
            candidate.Columns,
            "Composite unique candidate is clean; consider materialising a unique constraint if appropriate.",
            ImmutableDictionary<string, string?>.Empty);

        TryAdd(builder, opportunity);
    }

    private void EvaluateForeignKey(ForeignKeyReality foreignKey, ImmutableArray<ProfilingInsight>.Builder builder)
    {
        var reference = foreignKey.Reference;

        if (foreignKey.HasOrphan)
        {
            var metadata = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
            metadata["target"] = FormatTarget(reference);
            metadata["hasConstraint"] = reference.HasDatabaseConstraint ? "true" : "false";

            var result = ProfilingInsight.Create(
                ProfilingInsightCodes.ForeignKeyOrphans,
                ProfilingInsightSeverity.Critical,
                reference.FromSchema,
                reference.FromTable,
                ImmutableArray.Create(reference.FromColumn),
                $"Orphaned rows detected referencing {reference.ToSchema.Value}.{reference.ToTable.Value}.{reference.ToColumn.Value}.",
                metadata.ToImmutable());

            TryAdd(builder, result);
        }

        if (foreignKey.IsNoCheck)
        {
            var metadata = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
            metadata["target"] = FormatTarget(reference);

            var result = ProfilingInsight.Create(
                ProfilingInsightCodes.ForeignKeyUntrusted,
                ProfilingInsightSeverity.Warning,
                reference.FromSchema,
                reference.FromTable,
                ImmutableArray.Create(reference.FromColumn),
                "Foreign key is marked NOT TRUSTED; consider validating data and re-enabling CHECK.",
                metadata.ToImmutable());

            TryAdd(builder, result);
        }

        if (!reference.HasDatabaseConstraint && !foreignKey.HasOrphan)
        {
            var metadata = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
            metadata["target"] = FormatTarget(reference);

            var result = ProfilingInsight.Create(
                ProfilingInsightCodes.ForeignKeyOpportunity,
                ProfilingInsightSeverity.Info,
                reference.FromSchema,
                reference.FromTable,
                ImmutableArray.Create(reference.FromColumn),
                $"No orphans detected referencing {reference.ToSchema.Value}.{reference.ToTable.Value}.{reference.ToColumn.Value}. Consider adding a foreign key.",
                metadata.ToImmutable());

            TryAdd(builder, result);
        }
    }

    private static bool TryGetNullRatio(ColumnProfile column, out double ratio)
    {
        if (column.RowCount <= 0)
        {
            ratio = 0;
            return false;
        }

        ratio = (double)column.NullCount / column.RowCount;
        return true;
    }

    private static void TryAdd(ImmutableArray<ProfilingInsight>.Builder builder, Result<ProfilingInsight> result)
    {
        if (result.IsFailure)
        {
            return;
        }

        builder.Add(result.Value);
    }

    private static string FormatTarget(ForeignKeyReference reference)
        => $"{reference.ToSchema.Value}.{reference.ToTable.Value}.{reference.ToColumn.Value}";

    private static string FormatColumnList(ImmutableArray<ColumnName> columns)
    {
        if (columns.IsDefaultOrEmpty)
        {
            return string.Empty;
        }

        return string.Join(',', columns.Select(column => column.Value));
    }

    private readonly record struct ColumnKey(string Schema, string Table, string Column)
    {
        public static ColumnKey From(SchemaName schema, TableName table, ColumnName column)
            => new(schema.Value, table.Value, column.Value);
    }

    private sealed class ColumnKeyComparer : IEqualityComparer<ColumnKey>
    {
        public static ColumnKeyComparer Instance { get; } = new();

        public bool Equals(ColumnKey x, ColumnKey y)
            => StringComparer.OrdinalIgnoreCase.Equals(x.Schema, y.Schema)
                && StringComparer.OrdinalIgnoreCase.Equals(x.Table, y.Table)
                && StringComparer.OrdinalIgnoreCase.Equals(x.Column, y.Column);

        public int GetHashCode(ColumnKey obj)
        {
            var hash = new HashCode();
            hash.Add(obj.Schema, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.Table, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.Column, StringComparer.OrdinalIgnoreCase);
            return hash.ToHashCode();
        }
    }
}
