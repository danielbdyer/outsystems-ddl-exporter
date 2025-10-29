using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Validation.Tightening;

namespace Osm.Validation.Tightening.Opportunities;

public sealed class TighteningOpportunitiesAnalyzer : ITighteningAnalyzer
{
    public OpportunitiesReport Analyze(OsmModel model, ProfileSnapshot profile, PolicyDecisionSet decisions)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        if (decisions is null)
        {
            throw new ArgumentNullException(nameof(decisions));
        }

        var context = OpportunityContext.Create(model, profile);
        var notNullBuilder = new NotNullOpportunityBuilder(context);
        var uniqueBuilder = new UniqueIndexOpportunityBuilder(context);
        var foreignKeyBuilder = new ForeignKeyOpportunityBuilder(context);

        var opportunities = ImmutableArray.CreateBuilder<Opportunity>();
        var riskCounts = new Dictionary<ChangeRisk, int>();
        var typeCounts = new Dictionary<ConstraintType, int>();

        foreach (var opportunity in notNullBuilder.Build(decisions.Nullability.Values))
        {
            RecordOpportunity(opportunity, opportunities, riskCounts, typeCounts);
        }

        foreach (var opportunity in uniqueBuilder.Build(decisions.UniqueIndexes.Values))
        {
            RecordOpportunity(opportunity, opportunities, riskCounts, typeCounts);
        }

        foreach (var opportunity in foreignKeyBuilder.Build(decisions.ForeignKeys.Values))
        {
            RecordOpportunity(opportunity, opportunities, riskCounts, typeCounts);
        }

        return new OpportunitiesReport(
            opportunities.ToImmutable(),
            riskCounts.ToImmutableDictionary(),
            typeCounts.ToImmutableDictionary(),
            DateTimeOffset.UtcNow);
    }

    private static void RecordOpportunity(
        Opportunity opportunity,
        ImmutableArray<Opportunity>.Builder accumulator,
        IDictionary<ChangeRisk, int> riskCounts,
        IDictionary<ConstraintType, int> typeCounts)
    {
        accumulator.Add(opportunity);
        Increment(riskCounts, opportunity.Risk);
        Increment(typeCounts, opportunity.Constraint);
    }

    private static void Increment<TKey>(IDictionary<TKey, int> map, TKey key)
        where TKey : notnull
    {
        if (map.TryGetValue(key, out var count))
        {
            map[key] = count + 1;
        }
        else
        {
            map[key] = 1;
        }
    }
}
