using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Validation.Tightening;

namespace Osm.Validation.Tightening.Opportunities;

public enum ChangeRisk
{
    SafeToApply,
    NeedsRemediation
}

public enum ConstraintType
{
    NotNull,
    Unique,
    ForeignKey
}

public sealed record Opportunity(
    ConstraintType Constraint,
    ChangeRisk Risk,
    string Schema,
    string Table,
    string Name,
    ImmutableArray<string> Statements,
    ImmutableArray<string> Rationales,
    ImmutableArray<string> Evidence,
    OpportunityMetrics Metrics,
    ImmutableArray<ColumnAnalysis> Columns)
{
    public bool HasStatements => !Statements.IsDefaultOrEmpty && Statements.Length > 0;
}

public sealed record OpportunityMetrics(
    bool RequiresRemediation,
    bool EvidenceAvailable,
    bool? DataClean,
    bool? HasDuplicates,
    bool? HasOrphans);

public sealed record ColumnAnalysis(
    ColumnCoordinate Coordinate,
    string Module,
    string Entity,
    string Attribute,
    string DataType,
    string? SqlType,
    bool? PhysicalNullable,
    bool? PhysicalUnique,
    long? RowCount,
    long? NullCount,
    ProfilingProbeStatus? NullProbeStatus,
    bool? HasDuplicates,
    ProfilingProbeStatus? UniqueProbeStatus,
    bool? HasOrphans,
    bool? HasDatabaseConstraint,
    string? DeleteRule);

public sealed record OpportunitiesReport(
    ImmutableArray<Opportunity> Opportunities,
    ImmutableDictionary<ChangeRisk, int> RiskCounts,
    ImmutableDictionary<ConstraintType, int> ConstraintCounts,
    DateTimeOffset GeneratedAtUtc)
{
    public int TotalCount => Opportunities.Length;
}

public interface ITighteningAnalyzer
{
    OpportunitiesReport Analyze(OsmModel model, ProfileSnapshot profile, PolicyDecisionSet decisions);
}
