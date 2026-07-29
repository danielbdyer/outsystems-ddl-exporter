using System;
using System.Collections.Generic;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Validation.Tightening.Opportunities;

namespace Osm.Validation.Tightening;

public sealed class TighteningPolicy
{
    private readonly ILegacyPolicyAdapter _legacyAdapter;

    public TighteningPolicy()
        : this(new LegacyPolicyAdapter())
    {
    }

    public TighteningPolicy(ILegacyPolicyAdapter legacyAdapter)
    {
        _legacyAdapter = legacyAdapter ?? throw new ArgumentNullException(nameof(legacyAdapter));
    }

    public static TighteningDecisions Evaluate(OsmModel model, ProfileSnapshot snapshot, TighteningMode mode)
    {
        var policy = new TighteningPolicy();
        var options = policy._legacyAdapter.Adapt(mode);
        var decisions = policy.Decide(model, snapshot, options);

        return TighteningDecisions.Create(decisions.Nullability, decisions.ForeignKeys, decisions.UniqueIndexes);
    }

    public PolicyDecisionSet Decide(OsmModel model, ProfileSnapshot snapshot, TighteningOptions options)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var columnProfiles = snapshot.Columns.ToDictionary(ColumnCoordinate.From, static c => c);
        var uniqueProfiles = snapshot.UniqueCandidates.ToDictionary(ColumnCoordinate.From, static u => u);
        var foreignKeyReality = snapshot.ForeignKeys.ToDictionary(f => ColumnCoordinate.From(f.Reference), static f => f);

        var lookupResolution = EntityLookupResolver.Resolve(model, options.Emission.NamingOverrides);
        var entityLookup = lookupResolution.Lookup;
        var attributeIndex = EntityAttributeIndex.Create(model);
        var foreignKeyTargets = ForeignKeyTargetIndex.Create(attributeIndex, entityLookup);

        var uniqueEvidence = UniqueIndexEvidenceAggregator.Create(
            model,
            uniqueProfiles,
            snapshot.CompositeUniqueCandidates,
            options.Uniqueness.EnforceSingleColumnUnique,
            options.Uniqueness.EnforceMultiColumnUnique);

        var uniqueStrategy = new UniqueIndexDecisionStrategy(options, columnProfiles, uniqueProfiles, uniqueEvidence);

        var nullabilityEvaluator = new NullabilityEvaluator(
            options,
            columnProfiles,
            uniqueProfiles,
            foreignKeyReality,
            foreignKeyTargets,
            uniqueEvidence.SingleColumnClean,
            uniqueEvidence.SingleColumnDuplicates,
            uniqueEvidence.CompositeClean,
            uniqueEvidence.CompositeDuplicates);

        var foreignKeyEvaluator = new ForeignKeyEvaluator(options.ForeignKeys, foreignKeyReality, foreignKeyTargets, options.Policy.Mode);
        var analyzers = new IColumnDecisionAnalyzer[] { nullabilityEvaluator, foreignKeyEvaluator };

        var columnAggregator = new ColumnDecisionAggregator();
        var columnAggregation = columnAggregator.Aggregate(
            model,
            attributeIndex,
            columnProfiles,
            uniqueProfiles,
            foreignKeyReality,
            foreignKeyTargets,
            uniqueEvidence,
            analyzers);

        var uniqueOrchestrator = new UniqueIndexDecisionOrchestrator();
        var uniqueAggregation = uniqueOrchestrator.Evaluate(model, uniqueStrategy, columnAggregation.ColumnAnalyses);

        // Merge diagnostics from entity lookup resolution and column analysis
        var allDiagnostics = lookupResolution.Diagnostics.AddRange(columnAggregation.Diagnostics);

        return PolicyDecisionSet.Create(
            columnAggregation.Nullability,
            columnAggregation.ForeignKeys,
            uniqueAggregation.Decisions,
            allDiagnostics,
            columnAggregation.ColumnIdentities,
            uniqueAggregation.IndexModules,
            options);
    }

}
