using System;
using System.Collections.Immutable;

namespace Osm.Validation.Tightening.Opportunities;

public sealed record OpportunitiesReport(
    ImmutableArray<Opportunity> Opportunities,
    ImmutableDictionary<OpportunityDisposition, int> DispositionCounts,
    ImmutableDictionary<OpportunityCategory, int> CategoryCounts,
    ImmutableDictionary<OpportunityType, int> TypeCounts,
    ImmutableDictionary<RiskLevel, int> RiskCounts,
    DateTimeOffset GeneratedAtUtc)
{
    public int TotalCount => Opportunities.Length;

    public int ContradictionCount => CategoryCounts.TryGetValue(OpportunityCategory.Contradiction, out var count) ? count : 0;

    public int RecommendationCount => CategoryCounts.TryGetValue(OpportunityCategory.Recommendation, out var count) ? count : 0;

    public int ValidationCount => CategoryCounts.TryGetValue(OpportunityCategory.Validation, out var count) ? count : 0;
}
