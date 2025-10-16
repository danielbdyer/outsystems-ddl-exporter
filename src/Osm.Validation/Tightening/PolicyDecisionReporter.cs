using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Osm.Validation.Tightening;

public static class PolicyDecisionReporter
{
    public static PolicyDecisionReport Create(PolicyDecisionSet decisions)
    {
        if (decisions is null)
        {
            throw new ArgumentNullException(nameof(decisions));
        }

        var columnReports = decisions.Nullability.Values
            .Select(ColumnDecisionReport.From)
            .OrderBy(r => r.Column.Schema.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Column.Table.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Column.Column.Value, StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();

        var foreignKeyReports = decisions.ForeignKeys.Values
            .Select(ForeignKeyDecisionReport.From)
            .OrderBy(r => r.Column.Schema.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Column.Table.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Column.Column.Value, StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();

        var uniqueIndexReports = decisions.UniqueIndexes.Values
            .Select(UniqueIndexDecisionReport.From)
            .OrderBy(r => r.Index.Schema.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Index.Table.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Index.Index.Value, StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();

        var columnRationales = AggregateRationales(columnReports.SelectMany(r => r.Rationales));
        var uniqueRationales = AggregateRationales(uniqueIndexReports.SelectMany(r => r.Rationales));
        var foreignKeyRationales = AggregateRationales(foreignKeyReports.SelectMany(r => r.Rationales));

        var diagnostics = decisions.Diagnostics
            .OrderBy(d => d.Severity)
            .ThenBy(d => d.LogicalName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.CanonicalModule, StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();

        var moduleRollups = BuildModuleRollups(columnReports, uniqueIndexReports, foreignKeyReports, decisions);

        return new PolicyDecisionReport(
            columnReports,
            uniqueIndexReports,
            foreignKeyReports,
            columnRationales,
            uniqueRationales,
            foreignKeyRationales,
            diagnostics,
            moduleRollups,
            decisions.Toggles);
    }

    private static ImmutableDictionary<string, int> AggregateRationales(IEnumerable<string> rationales)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);

        foreach (var rationale in rationales)
        {
            if (builder.TryGetValue(rationale, out var count))
            {
                builder[rationale] = count + 1;
            }
            else
            {
                builder[rationale] = 1;
            }
        }

        return builder.ToImmutable();
    }

    private static ImmutableDictionary<string, ModuleDecisionRollup> BuildModuleRollups(
        ImmutableArray<ColumnDecisionReport> columns,
        ImmutableArray<UniqueIndexDecisionReport> uniqueIndexes,
        ImmutableArray<ForeignKeyDecisionReport> foreignKeys,
        PolicyDecisionSet decisions)
    {
        var accumulator = new Dictionary<string, ModuleDecisionRollupBuilder>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in columns)
        {
            if (!decisions.ColumnModules.TryGetValue(column.Column, out var module))
            {
                continue;
            }

            var rollup = GetOrAdd(accumulator, module);
            rollup.ColumnCount++;
            if (column.MakeNotNull)
            {
                rollup.TightenedColumnCount++;
            }

            if (column.RequiresRemediation)
            {
                rollup.RemediationColumnCount++;
            }
        }

        foreach (var index in uniqueIndexes)
        {
            if (!decisions.IndexModules.TryGetValue(index.Index, out var module))
            {
                continue;
            }

            var rollup = GetOrAdd(accumulator, module);
            rollup.UniqueIndexCount++;
            if (index.EnforceUnique)
            {
                rollup.UniqueIndexesEnforcedCount++;
            }

            if (index.RequiresRemediation)
            {
                rollup.UniqueIndexesRequireRemediationCount++;
            }
        }

        foreach (var foreignKey in foreignKeys)
        {
            if (!decisions.ColumnModules.TryGetValue(foreignKey.Column, out var module))
            {
                continue;
            }

            var rollup = GetOrAdd(accumulator, module);
            rollup.ForeignKeyCount++;
            if (foreignKey.CreateConstraint)
            {
                rollup.ForeignKeysCreatedCount++;
            }
        }

        var builder = ImmutableDictionary.CreateBuilder<string, ModuleDecisionRollup>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in accumulator)
        {
            builder[pair.Key] = pair.Value.Build();
        }

        return builder.ToImmutable();

        static ModuleDecisionRollupBuilder GetOrAdd(Dictionary<string, ModuleDecisionRollupBuilder> map, string module)
        {
            if (!map.TryGetValue(module, out var builder))
            {
                builder = new ModuleDecisionRollupBuilder();
                map[module] = builder;
            }

            return builder;
        }
    }
}

public sealed record PolicyDecisionReport(
    ImmutableArray<ColumnDecisionReport> Columns,
    ImmutableArray<UniqueIndexDecisionReport> UniqueIndexes,
    ImmutableArray<ForeignKeyDecisionReport> ForeignKeys,
    ImmutableDictionary<string, int> ColumnRationaleCounts,
    ImmutableDictionary<string, int> UniqueIndexRationaleCounts,
    ImmutableDictionary<string, int> ForeignKeyRationaleCounts,
    ImmutableArray<TighteningDiagnostic> Diagnostics,
    ImmutableDictionary<string, ModuleDecisionRollup> ModuleRollups,
    TighteningToggleSnapshot Toggles)
{
    public int ColumnCount => Columns.Length;

    public int TightenedColumnCount => Columns.Count(static c => c.MakeNotNull);

    public int RemediationColumnCount => Columns.Count(static c => c.RequiresRemediation);

    public int UniqueIndexCount => UniqueIndexes.Length;

    public int UniqueIndexesEnforcedCount => UniqueIndexes.Count(static u => u.EnforceUnique);

    public int UniqueIndexesRequireRemediationCount => UniqueIndexes.Count(static u => u.RequiresRemediation);

    public int ForeignKeyCount => ForeignKeys.Length;

    public int ForeignKeysCreatedCount => ForeignKeys.Count(static f => f.CreateConstraint);

    public int ModuleCount => ModuleRollups.Count;
}

public sealed record ModuleDecisionRollup(
    int ColumnCount,
    int TightenedColumnCount,
    int RemediationColumnCount,
    int UniqueIndexCount,
    int UniqueIndexesEnforcedCount,
    int UniqueIndexesRequireRemediationCount,
    int ForeignKeyCount,
    int ForeignKeysCreatedCount);

internal sealed class ModuleDecisionRollupBuilder
{
    public int ColumnCount;
    public int TightenedColumnCount;
    public int RemediationColumnCount;
    public int UniqueIndexCount;
    public int UniqueIndexesEnforcedCount;
    public int UniqueIndexesRequireRemediationCount;
    public int ForeignKeyCount;
    public int ForeignKeysCreatedCount;

    public ModuleDecisionRollup Build()
        => new(
            ColumnCount,
            TightenedColumnCount,
            RemediationColumnCount,
            UniqueIndexCount,
            UniqueIndexesEnforcedCount,
            UniqueIndexesRequireRemediationCount,
            ForeignKeyCount,
            ForeignKeysCreatedCount);
}

public sealed record ColumnDecisionReport(
    ColumnCoordinate Column,
    bool MakeNotNull,
    bool RequiresRemediation,
    ImmutableArray<string> Rationales)
{
    public static ColumnDecisionReport From(NullabilityDecision decision)
        => new(decision.Column, decision.MakeNotNull, decision.RequiresRemediation, decision.Rationales.IsDefault ? ImmutableArray<string>.Empty : decision.Rationales);
}

public sealed record ForeignKeyDecisionReport(
    ColumnCoordinate Column,
    bool CreateConstraint,
    ImmutableArray<string> Rationales)
{
    public static ForeignKeyDecisionReport From(ForeignKeyDecision decision)
        => new(decision.Column, decision.CreateConstraint, decision.Rationales.IsDefault ? ImmutableArray<string>.Empty : decision.Rationales);
}

public sealed record UniqueIndexDecisionReport(
    IndexCoordinate Index,
    bool EnforceUnique,
    bool RequiresRemediation,
    ImmutableArray<string> Rationales)
{
    public static UniqueIndexDecisionReport From(UniqueIndexDecision decision)
        => new(decision.Index, decision.EnforceUnique, decision.RequiresRemediation, decision.Rationales.IsDefault ? ImmutableArray<string>.Empty : decision.Rationales);
}
