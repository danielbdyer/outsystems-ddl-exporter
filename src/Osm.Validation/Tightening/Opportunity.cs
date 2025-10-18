using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Osm.Validation.Tightening;

public enum OpportunityCategory
{
    Nullability,
    ForeignKey,
    UniqueIndex
}

public sealed record Opportunity(
    OpportunityCategory Category,
    string Title,
    string Summary,
    ChangeRisk Risk,
    ImmutableArray<string> Evidence,
    ColumnCoordinate? Column,
    IndexCoordinate? Index)
{
    public static Opportunity Create(
        OpportunityCategory category,
        string title,
        string summary,
        ChangeRisk risk,
        IEnumerable<string> evidence,
        ColumnCoordinate? column = null,
        IndexCoordinate? index = null)
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

        return new Opportunity(category, title, summary, risk, evidenceArray, column, index);
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
}
