using System;
using System.Collections.Immutable;

namespace Osm.Validation.Tightening.Opportunities;

public sealed record OpportunitiesReport(
    ImmutableArray<Opportunity> Opportunities,
    ImmutableDictionary<OpportunityDisposition, int> DispositionCounts,
    ImmutableDictionary<OpportunityType, int> TypeCounts,
    ImmutableDictionary<RiskLevel, int> RiskCounts,
    DateTimeOffset GeneratedAtUtc)
{
    public int TotalCount => Opportunities.Length;
}
