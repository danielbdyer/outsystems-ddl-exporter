using System;
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
        var analysis = policy.Analyze(model, snapshot, options);

        return TighteningDecisions.Create(analysis.Decisions.Nullability, analysis.Decisions.ForeignKeys, analysis.Decisions.UniqueIndexes);
    }

    public PolicyDecisionSet Decide(OsmModel model, ProfileSnapshot snapshot, TighteningOptions options)
        => Analyze(model, snapshot, options).Decisions;

    public PolicyAnalysisResult Analyze(OsmModel model, ProfileSnapshot snapshot, TighteningOptions options)
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

        var lookupContext = TighteningLookupContext.Create(model, snapshot, options);

        var uniqueStrategy = new UniqueIndexDecisionStrategy(
            options,
            lookupContext.ColumnProfiles,
            lookupContext.UniqueProfiles,
            lookupContext.UniqueEvidence);

        var nullabilityEvaluator = new NullabilityEvaluator(
            options,
            lookupContext.ColumnProfiles,
            lookupContext.UniqueProfiles,
            lookupContext.ForeignKeyReality,
            lookupContext.ForeignKeyTargets,
            lookupContext.UniqueEvidence.SingleColumnClean,
            lookupContext.UniqueEvidence.SingleColumnDuplicates,
            lookupContext.UniqueEvidence.CompositeClean,
            lookupContext.UniqueEvidence.CompositeDuplicates);

        var foreignKeyEvaluator = new ForeignKeyEvaluator(
            options.ForeignKeys,
            lookupContext.ForeignKeyReality,
            lookupContext.ForeignKeyTargets);

        var columnService = new ColumnDecisionService(
            lookupContext,
            new ITighteningAnalyzer[] { nullabilityEvaluator, foreignKeyEvaluator });
        var columnResult = columnService.Analyze();

        var uniqueService = new UniqueIndexDecisionService(lookupContext, uniqueStrategy);
        var uniqueResult = uniqueService.Analyze(columnResult.AnalysisBuilders);

        var decisions = PolicyDecisionSet.Create(
            columnResult.NullabilityDecisions,
            columnResult.ForeignKeyDecisions,
            uniqueResult.UniqueDecisions,
            lookupContext.LookupResolution.Diagnostics,
            columnResult.ColumnModules,
            uniqueResult.IndexModules,
            options);

        var report = OpportunitiesReport.Create(columnResult.AnalysisBuilders.Values.Select(builder => builder.Build()));

        return PolicyAnalysisResult.Create(decisions, report);
    }
}
