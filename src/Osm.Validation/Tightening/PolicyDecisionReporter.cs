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

        var columnRationales = AggregateRationales(columnReports.SelectMany(r => r.Rationales));
        var foreignKeyRationales = AggregateRationales(foreignKeyReports.SelectMany(r => r.Rationales));

        return new PolicyDecisionReport(columnReports, foreignKeyReports, columnRationales, foreignKeyRationales);
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
}

public sealed record PolicyDecisionReport(
    ImmutableArray<ColumnDecisionReport> Columns,
    ImmutableArray<ForeignKeyDecisionReport> ForeignKeys,
    ImmutableDictionary<string, int> ColumnRationaleCounts,
    ImmutableDictionary<string, int> ForeignKeyRationaleCounts)
{
    public int ColumnCount => Columns.Length;

    public int TightenedColumnCount => Columns.Count(static c => c.MakeNotNull);

    public int RemediationColumnCount => Columns.Count(static c => c.RequiresRemediation);

    public int ForeignKeyCount => ForeignKeys.Length;

    public int ForeignKeysCreatedCount => ForeignKeys.Count(static f => f.CreateConstraint);
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
