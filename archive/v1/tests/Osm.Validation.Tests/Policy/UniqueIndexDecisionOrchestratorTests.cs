using System;
using System.Collections.Generic;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Validation.Tightening;
using Osm.Validation.Tightening.Opportunities;
using Tests.Support;
using TighteningAnalyzer = Osm.Validation.Tightening.ITighteningAnalyzer;

namespace Osm.Validation.Tests.Policy;

public sealed class UniqueIndexDecisionOrchestratorTests
{
    [Fact]
    public void Evaluate_CreatesUniqueDecisionsAndOpportunities()
    {
        var model = ModelFixtures.LoadModel("model.micro-unique.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.MicroUniqueWithDuplicates);
        var options = TighteningPolicyTestHelper.CreateOptions(TighteningMode.EvidenceGated);

        var columnProfiles = snapshot.Columns.ToDictionary(ColumnCoordinate.From, static c => c);
        var uniqueProfiles = snapshot.UniqueCandidates.ToDictionary(ColumnCoordinate.From, static u => u);
        var foreignKeyReality = snapshot.ForeignKeys.ToDictionary(f => ColumnCoordinate.From(f.Reference), static f => f);

        var lookupResolution = EntityLookupResolver.Resolve(model, options.Emission.NamingOverrides);
        var attributeIndex = EntityAttributeIndex.Create(model);
        var foreignKeyTargets = ForeignKeyTargetIndex.Create(attributeIndex, lookupResolution.Lookup);

        var uniqueEvidence = UniqueIndexEvidenceAggregator.Create(
            model,
            uniqueProfiles,
            snapshot.CompositeUniqueCandidates,
            options.Uniqueness.EnforceSingleColumnUnique,
            options.Uniqueness.EnforceMultiColumnUnique);

        var analyzers = BuildAnalyzers(
            options,
            columnProfiles,
            uniqueProfiles,
            foreignKeyReality,
            foreignKeyTargets,
            uniqueEvidence);

        var columnAggregation = new ColumnDecisionAggregator().Aggregate(
            model,
            attributeIndex,
            columnProfiles,
            uniqueProfiles,
            foreignKeyReality,
            foreignKeyTargets,
            uniqueEvidence,
            analyzers);

        var uniqueStrategy = new UniqueIndexDecisionStrategy(options, columnProfiles, uniqueProfiles, uniqueEvidence);
        var orchestrator = new UniqueIndexDecisionOrchestrator(new OpportunityBuilder());
        var aggregation = orchestrator.Evaluate(model, uniqueStrategy, columnAggregation.ColumnAnalyses);

        var entity = GetEntity(model, "User");
        var indexCoordinate = GetIndexCoordinate(entity, "UX_USER_EMAIL");
        var uniqueDecision = aggregation.Decisions[indexCoordinate];

        Assert.False(uniqueDecision.EnforceUnique);
        Assert.Contains(TighteningRationales.UniqueDuplicatesPresent, uniqueDecision.Rationales);
        Assert.Equal(entity.Module.Value, aggregation.IndexModules[indexCoordinate]);

        var columnCoordinate = GetCoordinate(entity, "Email");
        var builder = columnAggregation.ColumnAnalyses[columnCoordinate];
        Assert.Contains(uniqueDecision, builder.UniqueIndexes);

        var opportunity = Assert.Single(builder.Opportunities);
        Assert.Equal("Unique index was not enforced. Resolve duplicate values before enforcement can proceed.", opportunity.Summary);
        Assert.Equal(OpportunityDisposition.NeedsRemediation, opportunity.Disposition);
        Assert.Equal(OpportunityType.UniqueIndex, opportunity.Type);
    }

    private static TighteningAnalyzer[] BuildAnalyzers(
        TighteningOptions options,
        IReadOnlyDictionary<ColumnCoordinate, ColumnProfile> columnProfiles,
        IReadOnlyDictionary<ColumnCoordinate, UniqueCandidateProfile> uniqueProfiles,
        IReadOnlyDictionary<ColumnCoordinate, ForeignKeyReality> foreignKeyReality,
        ForeignKeyTargetIndex foreignKeyTargets,
        UniqueIndexEvidenceAggregator uniqueEvidence)
        => new TighteningAnalyzer[]
        {
            new NullabilityEvaluator(
                options,
                columnProfiles,
                uniqueProfiles,
                foreignKeyReality,
                foreignKeyTargets,
                uniqueEvidence.SingleColumnClean,
                uniqueEvidence.SingleColumnDuplicates,
                uniqueEvidence.CompositeClean,
                uniqueEvidence.CompositeDuplicates),
            new ForeignKeyEvaluator(options.ForeignKeys, foreignKeyReality, foreignKeyTargets, options.Policy.Mode)
        };

    private static EntityModel GetEntity(OsmModel model, string logicalName)
        => model.Modules.SelectMany(m => m.Entities).Single(e => string.Equals(e.LogicalName.Value, logicalName, StringComparison.Ordinal));

    private static ColumnCoordinate GetCoordinate(EntityModel entity, string attributeName)
    {
        var attribute = entity.Attributes.Single(a => string.Equals(a.LogicalName.Value, attributeName, StringComparison.Ordinal));
        return new ColumnCoordinate(entity.Schema, entity.PhysicalName, attribute.ColumnName);
    }

    private static IndexCoordinate GetIndexCoordinate(EntityModel entity, string indexName)
    {
        var index = entity.Indexes.Single(i => string.Equals(i.Name.Value, indexName, StringComparison.Ordinal));
        return new IndexCoordinate(entity.Schema, entity.PhysicalName, index.Name);
    }
}

