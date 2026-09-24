using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Validation.Tightening;
using Tests.Support;

namespace Osm.Validation.Tests.Policy;

public sealed class ColumnDecisionAggregatorTests
{
    [Fact]
    public void Aggregate_ComputesNullabilityAndForeignKeyDecisions()
    {
        var model = ModelFixtures.LoadModel("model.micro-fk-protect.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.MicroFkProtect);
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

        var aggregator = new ColumnDecisionAggregator();
        var aggregation = aggregator.Aggregate(
            model,
            attributeIndex,
            columnProfiles,
            uniqueProfiles,
            foreignKeyReality,
            foreignKeyTargets,
            uniqueEvidence,
            analyzers);

        var child = GetEntity(model, "Child");
        var parentId = GetCoordinate(child, "ParentId");

        var nullability = aggregation.Nullability[parentId];
        Assert.True(nullability.MakeNotNull);
        Assert.Contains(TighteningRationales.ForeignKeyEnforced, nullability.Rationales);

        var foreignKey = aggregation.ForeignKeys[parentId];
        Assert.True(foreignKey.CreateConstraint);
        Assert.Contains(TighteningRationales.DatabaseConstraintPresent, foreignKey.Rationales);

        var builder = aggregation.ColumnAnalyses[parentId];
        Assert.Equal(nullability, builder.Nullability);
        Assert.Equal(foreignKey, builder.ForeignKey);
        Assert.Equal(child.Module.Value, aggregation.ColumnIdentities[parentId].ModuleName);
    }

    [Fact]
    public void Aggregate_InvokesAllAnalyzersPerColumn()
    {
        var model = ModelFixtures.LoadModel("model.micro-unique.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.MicroUnique);
        var options = TighteningPolicyTestHelper.CreateOptions(TighteningMode.EvidenceGated);

        var columnProfiles = snapshot.Columns.ToDictionary(ColumnCoordinate.From, static c => c);
        var uniqueProfiles = snapshot.UniqueCandidates.ToDictionary(ColumnCoordinate.From, static u => u);
        var foreignKeyReality = new Dictionary<ColumnCoordinate, ForeignKeyReality>();
        var lookupResolution = EntityLookupResolver.Resolve(model, options.Emission.NamingOverrides);
        var attributeIndex = EntityAttributeIndex.Create(model);
        var foreignKeyTargets = ForeignKeyTargetIndex.Create(attributeIndex, lookupResolution.Lookup);

        var uniqueEvidence = UniqueIndexEvidenceAggregator.Create(
            model,
            uniqueProfiles,
            snapshot.CompositeUniqueCandidates,
            options.Uniqueness.EnforceSingleColumnUnique,
            options.Uniqueness.EnforceMultiColumnUnique);

        var invocationCounter = new CountingAnalyzer();
        var analyzers = new ITighteningAnalyzer[]
        {
            invocationCounter,
            new NullabilityEvaluator(
                options,
                columnProfiles,
                uniqueProfiles,
                foreignKeyReality,
                foreignKeyTargets,
                uniqueEvidence.SingleColumnClean,
                uniqueEvidence.SingleColumnDuplicates,
                uniqueEvidence.CompositeClean,
                uniqueEvidence.CompositeDuplicates)
        };

        var aggregator = new ColumnDecisionAggregator();
        _ = aggregator.Aggregate(
            model,
            attributeIndex,
            columnProfiles,
            uniqueProfiles,
            foreignKeyReality,
            foreignKeyTargets,
            uniqueEvidence,
            analyzers);

        var expected = attributeIndex
            .GetAttributes(GetEntity(model, "User"))
            .Count();

        Assert.Equal(expected, invocationCounter.Invocations.Count);
    }

    private static ITighteningAnalyzer[] BuildAnalyzers(
        TighteningOptions options,
        IReadOnlyDictionary<ColumnCoordinate, ColumnProfile> columnProfiles,
        IReadOnlyDictionary<ColumnCoordinate, UniqueCandidateProfile> uniqueProfiles,
        IReadOnlyDictionary<ColumnCoordinate, ForeignKeyReality> foreignKeyReality,
        ForeignKeyTargetIndex foreignKeyTargets,
        UniqueIndexEvidenceAggregator uniqueEvidence)
        => new ITighteningAnalyzer[]
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

    private sealed class CountingAnalyzer : ITighteningAnalyzer
    {
        public List<ColumnCoordinate> Invocations { get; } = new();

        public void Analyze(EntityContext context, ColumnAnalysisBuilder builder)
        {
            Invocations.Add(context.Column);
            builder.SetNullability(NullabilityDecision.Create(context.Column, makeNotNull: false, requiresRemediation: false, ImmutableArray<string>.Empty));
        }
    }
}

