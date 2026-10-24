using System;
using System.Collections.Immutable;
using Osm.Validation.Tightening.Opportunities;

namespace Osm.Validation.Tightening.Validations;

public sealed record ValidationReport(
    ImmutableArray<ValidationFinding> Validations,
    ImmutableDictionary<OpportunityType, int> TypeCounts,
    DateTimeOffset GeneratedAtUtc)
{
    public int TotalCount => Validations.Length;

    public static ValidationReport Empty(DateTimeOffset generatedAtUtc)
        => new(ImmutableArray<ValidationFinding>.Empty, ImmutableDictionary<OpportunityType, int>.Empty, generatedAtUtc);
}
