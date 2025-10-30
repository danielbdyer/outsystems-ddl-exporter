using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;

namespace Osm.Validation.Tightening.Opportunities;

public enum OpportunityType
{
    Nullability,
    UniqueIndex,
    ForeignKey
}

public enum OpportunityDisposition
{
    Unknown = 0,
    ReadyToApply = 1,
    NeedsRemediation = 2
}

public sealed record OpportunityEvidenceSummary(
    bool RequiresRemediation,
    bool EvidenceAvailable,
    bool? DataClean,
    bool? HasDuplicates,
    bool? HasOrphans);

public sealed record OpportunityColumn(
    ColumnIdentity Identity,
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
    string? DeleteRule)
{
    public ColumnCoordinate Coordinate => Identity.Coordinate;

    public string Module => Identity.ModuleName;

    public string Entity => Identity.EntityName;

    public string Attribute => Identity.AttributeName;
}

public sealed record Opportunity(
    OpportunityType Type,
    string Title,
    string Summary,
    ChangeRisk Risk,
    OpportunityDisposition Disposition,
    ImmutableArray<string> Evidence,
    ColumnCoordinate? Column,
    IndexCoordinate? Index,
    string? Schema,
    string? Table,
    string? ConstraintName,
    ImmutableArray<string> Statements,
    ImmutableArray<string> Rationales,
    OpportunityEvidenceSummary? EvidenceSummary,
    ImmutableArray<OpportunityColumn> Columns)
{
    public bool HasStatements => !Statements.IsDefaultOrEmpty && Statements.Length > 0;

    public static Opportunity Create(
        OpportunityType type,
        string title,
        string summary,
        ChangeRisk risk,
        IEnumerable<string> evidence,
        ColumnCoordinate? column = null,
        IndexCoordinate? index = null,
        OpportunityDisposition disposition = OpportunityDisposition.Unknown,
        IEnumerable<string>? statements = null,
        IEnumerable<string>? rationales = null,
        OpportunityEvidenceSummary? evidenceSummary = null,
        IEnumerable<OpportunityColumn>? columns = null,
        string? schema = null,
        string? table = null,
        string? constraintName = null)
    {
        if (risk is null)
        {
            throw new ArgumentNullException(nameof(risk));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title must be provided.", nameof(title));
        }

        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new ArgumentException("Summary must be provided.", nameof(summary));
        }

        var evidenceArray = NormalizeEvidence(evidence);
        var statementsArray = NormalizeSequence(statements);
        var rationalesArray = NormalizeSequence(rationales);
        var columnArray = columns is null
            ? ImmutableArray<OpportunityColumn>.Empty
            : columns.ToImmutableArray();

        return new Opportunity(
            type,
            title,
            summary,
            risk,
            disposition,
            evidenceArray,
            column,
            index,
            schema,
            table,
            constraintName,
            statementsArray,
            rationalesArray,
            evidenceSummary,
            columnArray);
    }

    private static ImmutableArray<string> NormalizeEvidence(IEnumerable<string> evidence)
    {
        if (evidence is null)
        {
            return ImmutableArray<string>.Empty;
        }

        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var entry in evidence)
        {
            if (!string.IsNullOrWhiteSpace(entry))
            {
                set.Add(entry);
            }
        }

        return set.ToImmutableArray();
    }

    private static ImmutableArray<string> NormalizeSequence(IEnumerable<string>? source)
    {
        if (source is null)
        {
            return ImmutableArray<string>.Empty;
        }

        return source.Where(static s => !string.IsNullOrWhiteSpace(s)).ToImmutableArray();
    }
}
