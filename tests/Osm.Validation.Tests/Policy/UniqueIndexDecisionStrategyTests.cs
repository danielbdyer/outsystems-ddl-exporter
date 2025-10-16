using System;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Validation.Tightening;
using Tests.Support;

namespace Osm.Validation.Tests.Policy;

public sealed class UniqueIndexDecisionStrategyTests
{
    [Fact]
    public void PhysicalUniqueWithDuplicatesStillEnforcesInEvidenceMode()
    {
        var model = ModelFixtures.LoadModel("model.micro-unique.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.MicroUniqueWithDuplicates);
        var options = TighteningPolicyTestHelper.CreateOptions(TighteningMode.EvidenceGated);

        var adjustedSnapshot = PromotePhysicalUnique(snapshot);
        var strategy = CreateStrategy(model, adjustedSnapshot, options);
        var entity = GetEntity(model, "User");
        var index = entity.Indexes.Single(i => string.Equals(i.Name.Value, "UX_USER_EMAIL", StringComparison.Ordinal));

        var decision = strategy.Decide(entity, index);

        Assert.True(decision.EnforceUnique);
        Assert.False(decision.RequiresRemediation);
        Assert.Contains(TighteningRationales.PhysicalUniqueKey, decision.Rationales);
        Assert.Contains(TighteningRationales.UniqueDuplicatesPresent, decision.Rationales);
    }

    [Fact]
    public void AggressiveModeWithoutEvidenceRequiresRemediation()
    {
        var model = ModelFixtures.LoadModel("model.micro-unique.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.MicroUnique);
        var options = TighteningPolicyTestHelper.CreateOptions(TighteningMode.Aggressive);

        var strippedSnapshot = RemoveEvidence(snapshot);
        var strategy = CreateStrategy(model, strippedSnapshot, options);
        var entity = GetEntity(model, "User");
        var index = entity.Indexes.Single(i => string.Equals(i.Name.Value, "UX_USER_EMAIL", StringComparison.Ordinal));

        var decision = strategy.Decide(entity, index);

        Assert.True(decision.EnforceUnique);
        Assert.True(decision.RequiresRemediation);
        Assert.Contains(TighteningRationales.ProfileMissing, decision.Rationales);
        Assert.Contains(TighteningRationales.RemediateBeforeTighten, decision.Rationales);
    }

    [Fact]
    public void EvidenceModeTreatsIncludedColumnsAsSingleColumnIndex()
    {
        var model = ModelFixtures.LoadModel("model.micro-unique.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.MicroUnique);
        var options = TighteningPolicyTestHelper.CreateOptions(TighteningMode.EvidenceGated);

        var entity = GetEntity(model, "User");
        var index = entity.Indexes.Single(i => string.Equals(i.Name.Value, "UX_USER_EMAIL", StringComparison.Ordinal));
        var includedAttribute = entity.Attributes.Single(a => string.Equals(a.LogicalName.Value, "Id", StringComparison.Ordinal));

        var includedColumn = IndexColumnModel.Create(
            includedAttribute.LogicalName,
            includedAttribute.ColumnName,
            index.Columns.Length + 1,
            isIncluded: true,
            IndexColumnDirection.Ascending).Value;

        var updatedIndex = index with { Columns = index.Columns.Add(includedColumn) };
        var updatedModules = model.Modules
            .Select(module => string.Equals(module.Name.Value, entity.Module.Value, StringComparison.Ordinal)
                ? module with { Entities = module.Entities.Replace(entity, entity with { Indexes = entity.Indexes.Replace(index, updatedIndex) }) }
                : module)
            .ToImmutableArray();

        var updatedModel = model with { Modules = updatedModules };
        var strategy = CreateStrategy(updatedModel, snapshot, options);
        var updatedEntity = GetEntity(updatedModel, "User");
        var updatedIndexReference = updatedEntity.Indexes.Single(i => string.Equals(i.Name.Value, "UX_USER_EMAIL", StringComparison.Ordinal));

        var decision = strategy.Decide(updatedEntity, updatedIndexReference);

        Assert.True(decision.EnforceUnique);
        Assert.False(decision.RequiresRemediation);
        Assert.Contains(TighteningRationales.UniqueNoNulls, decision.Rationales);
        Assert.DoesNotContain(TighteningRationales.ProfileMissing, decision.Rationales);
        Assert.DoesNotContain(TighteningRationales.CompositeUniqueNoNulls, decision.Rationales);
    }

    private static UniqueIndexDecisionStrategy CreateStrategy(OsmModel model, ProfileSnapshot snapshot, TighteningOptions options)
    {
        var columnProfiles = snapshot.Columns.ToDictionary(ColumnCoordinate.From, static c => c);
        var uniqueProfiles = snapshot.UniqueCandidates.ToDictionary(ColumnCoordinate.From, static u => u);
        var evidence = UniqueIndexEvidenceAggregator.Create(
            model,
            uniqueProfiles,
            snapshot.CompositeUniqueCandidates,
            options.Uniqueness.EnforceSingleColumnUnique,
            options.Uniqueness.EnforceMultiColumnUnique);

        return new UniqueIndexDecisionStrategy(options, columnProfiles, uniqueProfiles, evidence);
    }

    private static EntityModel GetEntity(OsmModel model, string logicalName)
        => model.Modules.SelectMany(static m => m.Entities).Single(e => string.Equals(e.LogicalName.Value, logicalName, StringComparison.Ordinal));

    private static ProfileSnapshot PromotePhysicalUnique(ProfileSnapshot snapshot)
    {
        var updatedColumns = snapshot.Columns
            .Select(c => string.Equals(c.Column.Value, "EMAIL", StringComparison.Ordinal)
                ? c with { IsUniqueKey = true }
                : c)
            .ToImmutableArray();

        return snapshot with { Columns = updatedColumns };
    }

    private static ProfileSnapshot RemoveEvidence(ProfileSnapshot snapshot)
    {
        var updatedColumns = snapshot.Columns
            .Select(c => string.Equals(c.Column.Value, "EMAIL", StringComparison.Ordinal)
                ? c with { IsUniqueKey = false }
                : c)
            .ToImmutableArray();

        return snapshot with
        {
            Columns = updatedColumns,
            UniqueCandidates = ImmutableArray<UniqueCandidateProfile>.Empty,
            CompositeUniqueCandidates = ImmutableArray<CompositeUniqueCandidateProfile>.Empty
        };
    }
}
