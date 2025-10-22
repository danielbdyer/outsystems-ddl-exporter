using System.Collections.Immutable;

namespace Osm.Validation.Tightening;

public sealed record PredicateTelemetry(
    ImmutableArray<TablePredicateTelemetry> Tables,
    ImmutableArray<ColumnPredicateTelemetry> Columns,
    ImmutableArray<IndexPredicateTelemetry> Indexes,
    ImmutableArray<SequencePredicateTelemetry> Sequences,
    ImmutableArray<ExtendedPropertyPredicateTelemetry> ExtendedProperties)
{
    public static PredicateTelemetry Empty { get; } = new(
        ImmutableArray<TablePredicateTelemetry>.Empty,
        ImmutableArray<ColumnPredicateTelemetry>.Empty,
        ImmutableArray<IndexPredicateTelemetry>.Empty,
        ImmutableArray<SequencePredicateTelemetry>.Empty,
        ImmutableArray<ExtendedPropertyPredicateTelemetry>.Empty);
}

public sealed record TablePredicateTelemetry(
    string Module,
    string LogicalName,
    string Schema,
    string PhysicalName,
    ImmutableArray<string> Predicates);

public sealed record ColumnPredicateTelemetry(
    string Module,
    string Entity,
    string Schema,
    string Table,
    string Column,
    ImmutableArray<string> Predicates);

public sealed record IndexPredicateTelemetry(
    string Module,
    string Entity,
    string Schema,
    string Table,
    string Index,
    ImmutableArray<string> Predicates);

public sealed record SequencePredicateTelemetry(
    string Schema,
    string Name,
    ImmutableArray<string> Predicates);

public sealed record ExtendedPropertyPredicateTelemetry(
    string Scope,
    string? Module,
    string? Schema,
    string? Table,
    string? Column,
    ImmutableArray<string> Predicates);
