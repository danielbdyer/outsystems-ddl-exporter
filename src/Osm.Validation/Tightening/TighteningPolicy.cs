using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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

        var foreignKeyEvaluator = new ForeignKeyEvaluator(options.ForeignKeys, foreignKeyReality, foreignKeyTargets);
        var analyzers = new ITighteningAnalyzer[] { nullabilityEvaluator, foreignKeyEvaluator };

        var nullabilityBuilder = ImmutableDictionary.CreateBuilder<ColumnCoordinate, NullabilityDecision>();
        var foreignKeyBuilder = ImmutableDictionary.CreateBuilder<ColumnCoordinate, ForeignKeyDecision>();
        var uniqueIndexBuilder = ImmutableDictionary.CreateBuilder<IndexCoordinate, UniqueIndexDecision>();
        var columnModuleBuilder = ImmutableDictionary.CreateBuilder<ColumnCoordinate, string>();
        var indexModuleBuilder = ImmutableDictionary.CreateBuilder<IndexCoordinate, string>();
        var columnAnalysisBuilder = new Dictionary<ColumnCoordinate, ColumnAnalysisBuilder>();

        foreach (var entity in model.Modules.SelectMany(m => m.Entities))
        {
            foreach (var attribute in attributeIndex.GetAttributes(entity))
            {
                var coordinate = new ColumnCoordinate(entity.Schema, entity.PhysicalName, attribute.ColumnName);
                var builder = new ColumnAnalysisBuilder(coordinate);
                columnAnalysisBuilder[coordinate] = builder;

                var columnProfile = columnProfiles.TryGetValue(coordinate, out var profile) ? profile : null;
                var uniqueProfile = uniqueProfiles.TryGetValue(coordinate, out var uniqueCandidate) ? uniqueCandidate : null;
                var foreignKey = foreignKeyReality.TryGetValue(coordinate, out var fk) ? fk : null;

                var context = new EntityContext(
                    entity,
                    attribute,
                    coordinate,
                    columnProfile,
                    uniqueProfile,
                    foreignKey,
                    foreignKeyTargets.GetTarget(coordinate),
                    uniqueEvidence.SingleColumnClean.Contains(coordinate),
                    uniqueEvidence.SingleColumnDuplicates.Contains(coordinate),
                    uniqueEvidence.CompositeClean.Contains(coordinate),
                    uniqueEvidence.CompositeDuplicates.Contains(coordinate));

                foreach (var analyzer in analyzers)
                {
                    analyzer.Analyze(context, builder);
                }

                nullabilityBuilder[coordinate] = builder.Nullability;
                columnModuleBuilder[coordinate] = entity.Module.Value;

                if (builder.ForeignKey is not null)
                {
                    foreignKeyBuilder[coordinate] = builder.ForeignKey;
                }
            }

            foreach (var index in entity.Indexes.Where(static i => i.IsUnique))
            {
                var indexAnalysis = uniqueStrategy.Evaluate(entity, index);
                uniqueIndexBuilder[indexAnalysis.Index] = indexAnalysis.Decision;
                indexModuleBuilder[indexAnalysis.Index] = entity.Module.Value;

                foreach (var column in indexAnalysis.Columns)
                {
                    if (!columnAnalysisBuilder.TryGetValue(column, out var builder))
                    {
                        continue;
                    }

                    builder.AddUniqueDecision(indexAnalysis.Decision);

                    if (!ShouldCreateUniqueOpportunity(indexAnalysis))
                    {
                        continue;
                    }

                    var summary = BuildUniqueSummary(indexAnalysis);
                    var risk = ChangeRiskClassifier.ForUniqueIndex(indexAnalysis);
                    var opportunity = Opportunity.Create(
                        OpportunityType.UniqueIndex,
                        "UNIQUE",
                        summary,
                        risk,
                        indexAnalysis.Rationales,
                        column: column,
                        index: indexAnalysis.Index,
                        disposition: OpportunityDisposition.NeedsRemediation);

                    builder.AddOpportunity(opportunity);
                }
            }
        }

        var decisions = PolicyDecisionSet.Create(
            nullabilityBuilder.ToImmutable(),
            foreignKeyBuilder.ToImmutable(),
            uniqueIndexBuilder.ToImmutable(),
            lookupResolution.Diagnostics,
            columnModuleBuilder.ToImmutable(),
            indexModuleBuilder.ToImmutable(),
            options);

        var report = OpportunitiesReport.Create(columnAnalysisBuilder.Values.Select(builder => builder.Build()));

        return PolicyAnalysisResult.Create(decisions, report);
    }

    private static bool ShouldCreateUniqueOpportunity(UniqueIndexDecisionStrategy.UniqueIndexAnalysis analysis)
        => !analysis.Decision.EnforceUnique || analysis.Decision.RequiresRemediation;

    private static string BuildUniqueSummary(UniqueIndexDecisionStrategy.UniqueIndexAnalysis analysis)
    {
        if (analysis.Decision.RequiresRemediation)
        {
            return "Remediate data before enforcing the unique index.";
        }

        if (analysis.HasDuplicates)
        {
            return "Resolve duplicate values before enforcing the unique index.";
        }

        if (analysis.PolicyDisabled)
        {
            return "Enable policy support before enforcing the unique index.";
        }

        if (!analysis.HasEvidence)
        {
            return "Collect profiling evidence before enforcing the unique index.";
        }

        return "Review unique index enforcement before applying.";
    }
}
