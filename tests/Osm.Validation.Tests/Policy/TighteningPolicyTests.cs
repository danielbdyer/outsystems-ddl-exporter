using System;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Validation.Tightening;
using Tests.Support;

namespace Osm.Validation.Tests.Policy;

public sealed class TighteningPolicyTests
{
    [Theory]
    [InlineData(TighteningMode.Cautious, false)]
    [InlineData(TighteningMode.EvidenceGated, true)]
    [InlineData(TighteningMode.Aggressive, true)]
    public void UniqueColumn_TighteningDependsOnMode(TighteningMode mode, bool expected)
    {
        var model = ModelFixtures.LoadModel("model.micro-unique.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.MicroUnique);
        var policy = new TighteningPolicy();
        var options = TighteningPolicyTestHelper.CreateOptions(mode);

        var decisions = policy.Decide(model, snapshot, options);
        var entity = GetEntity(model, "User");
        var coordinate = GetCoordinate(entity, "Email");
        var decision = decisions.Nullability[coordinate];

        Assert.Equal(expected, decision.MakeNotNull);
        if (expected)
        {
            Assert.Contains(TighteningRationales.UniqueNoNulls, decision.Rationales);
            Assert.Contains(TighteningRationales.DataNoNulls, decision.Rationales);
        }
        else
        {
            Assert.DoesNotContain(TighteningRationales.DataNoNulls, decision.Rationales);
        }
    }

    [Fact]
    public void ProtectForeignKey_ShouldTightenAndCreateConstraint()
    {
        var model = ModelFixtures.LoadModel("model.micro-fk-protect.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.MicroFkProtect);
        var policy = new TighteningPolicy();
        var options = TighteningPolicyTestHelper.CreateOptions(TighteningMode.EvidenceGated);

        var decisions = policy.Decide(model, snapshot, options);
        var entity = GetEntity(model, "Child");
        var coordinate = GetCoordinate(entity, "ParentId");
        var columnDecision = decisions.Nullability[coordinate];
        var fkDecision = decisions.ForeignKeys[coordinate];

        Assert.True(columnDecision.MakeNotNull);
        Assert.Contains(TighteningRationales.ForeignKeyEnforced, columnDecision.Rationales);
        Assert.Contains(TighteningRationales.DataNoNulls, columnDecision.Rationales);

        Assert.True(fkDecision.CreateConstraint);
        Assert.Contains(TighteningRationales.DatabaseConstraintPresent, fkDecision.Rationales);
    }

    [Fact]
    public void IgnoreForeignKey_ShouldAvoidTighteningOrCreatingConstraint()
    {
        var model = ModelFixtures.LoadModel("model.micro-fk-ignore.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.MicroFkIgnore);
        var policy = new TighteningPolicy();
        var options = TighteningPolicyTestHelper.CreateOptions(TighteningMode.EvidenceGated);

        var decisions = policy.Decide(model, snapshot, options);
        var entity = GetEntity(model, "B");
        var coordinate = GetCoordinate(entity, "AId");
        var columnDecision = decisions.Nullability[coordinate];
        var fkDecision = decisions.ForeignKeys[coordinate];

        Assert.False(columnDecision.MakeNotNull);
        Assert.Contains(TighteningRationales.DeleteRuleIgnore, columnDecision.Rationales);
        Assert.Contains(TighteningRationales.DataHasOrphans, columnDecision.Rationales);

        Assert.False(fkDecision.CreateConstraint);
        Assert.Contains(TighteningRationales.DeleteRuleIgnore, fkDecision.Rationales);
        Assert.Contains(TighteningRationales.DataHasOrphans, fkDecision.Rationales);
    }

    [Fact]
    public void AggressiveMode_ShouldRequireRemediationWhenNullsExist()
    {
        var model = ModelFixtures.LoadModel("model.micro-unique.json");
        var dirtySnapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.MicroUniqueWithNullDrift);
        var policy = new TighteningPolicy();

        var evidenceOptions = TighteningPolicyTestHelper.CreateOptions(TighteningMode.EvidenceGated);
        var aggressiveOptions = TighteningPolicyTestHelper.CreateOptions(TighteningMode.Aggressive);

        var evidenceDecision = policy.Decide(model, dirtySnapshot, evidenceOptions);
        var aggressiveDecision = policy.Decide(model, dirtySnapshot, aggressiveOptions);

        var entity = GetEntity(model, "User");
        var coordinate = GetCoordinate(entity, "Email");

        Assert.False(evidenceDecision.Nullability[coordinate].MakeNotNull);

        var aggressiveColumn = aggressiveDecision.Nullability[coordinate];
        Assert.True(aggressiveColumn.MakeNotNull);
        Assert.True(aggressiveColumn.RequiresRemediation);
        Assert.Contains(TighteningRationales.RemediateBeforeTighten, aggressiveColumn.Rationales);
    }

    [Theory]
    [InlineData(TighteningMode.Cautious, false)]
    [InlineData(TighteningMode.EvidenceGated, true)]
    [InlineData(TighteningMode.Aggressive, true)]
    public void CompositeUnique_TighteningDependsOnMode(TighteningMode mode, bool expected)
    {
        var model = ModelFixtures.LoadModel("model.micro-unique-composite.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.MicroCompositeUnique);
        var policy = new TighteningPolicy();
        var options = TighteningPolicyTestHelper.CreateOptions(mode);

        var decisions = policy.Decide(model, snapshot, options);
        var entity = GetEntity(model, "OrderAllocation");
        var coordinate = GetCoordinate(entity, "CountryId");
        var decision = decisions.Nullability[coordinate];

        Assert.Equal(expected, decision.MakeNotNull);
        if (expected)
        {
            Assert.Contains(TighteningRationales.CompositeUniqueNoNulls, decision.Rationales);
            Assert.Contains(TighteningRationales.DataNoNulls, decision.Rationales);
        }
        else
        {
            Assert.Contains(TighteningRationales.CompositeUniqueNoNulls, decision.Rationales);
            Assert.DoesNotContain(TighteningRationales.DataNoNulls, decision.Rationales);
        }
    }

    [Fact]
    public void CompositeUnique_WithDuplicates_ShouldAvoidTightening()
    {
        var model = ModelFixtures.LoadModel("model.micro-unique-composite.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.MicroCompositeUniqueWithDuplicates);
        var policy = new TighteningPolicy();
        var options = TighteningPolicyTestHelper.CreateOptions(TighteningMode.EvidenceGated);

        var decisions = policy.Decide(model, snapshot, options);
        var entity = GetEntity(model, "OrderAllocation");
        var coordinate = GetCoordinate(entity, "CountryId");
        var decision = decisions.Nullability[coordinate];

        Assert.False(decision.MakeNotNull);
        Assert.Contains(TighteningRationales.CompositeUniqueDuplicatesPresent, decision.Rationales);
    }

    [Fact]
    public void UniqueIndexDecision_DisablesWhenDuplicatesInEvidenceMode()
    {
        var model = ModelFixtures.LoadModel("model.micro-unique.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.MicroUniqueWithDuplicates);
        var policy = new TighteningPolicy();
        var options = TighteningPolicyTestHelper.CreateOptions(TighteningMode.EvidenceGated);

        var decisions = policy.Decide(model, snapshot, options);
        var entity = GetEntity(model, "User");
        var indexCoordinate = GetIndexCoordinate(entity, "UX_USER_EMAIL");
        var decision = decisions.UniqueIndexes[indexCoordinate];

        Assert.False(decision.EnforceUnique);
        Assert.False(decision.RequiresRemediation);
        Assert.Contains(TighteningRationales.UniqueDuplicatesPresent, decision.Rationales);
        Assert.DoesNotContain(TighteningRationales.RemediateBeforeTighten, decision.Rationales);
    }

    [Fact]
    public void UniqueIndexDecision_AggressiveRequiresRemediationWhenDuplicates()
    {
        var model = ModelFixtures.LoadModel("model.micro-unique.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.MicroUniqueWithDuplicates);
        var policy = new TighteningPolicy();
        var options = TighteningPolicyTestHelper.CreateOptions(TighteningMode.Aggressive);

        var decisions = policy.Decide(model, snapshot, options);
        var entity = GetEntity(model, "User");
        var indexCoordinate = GetIndexCoordinate(entity, "UX_USER_EMAIL");
        var decision = decisions.UniqueIndexes[indexCoordinate];

        Assert.True(decision.EnforceUnique);
        Assert.True(decision.RequiresRemediation);
        Assert.Contains(TighteningRationales.UniqueDuplicatesPresent, decision.Rationales);
        Assert.Contains(TighteningRationales.RemediateBeforeTighten, decision.Rationales);
    }

    [Fact]
    public void UniqueIndexDecision_RespectsUniquenessToggle()
    {
        var model = ModelFixtures.LoadModel("model.micro-unique.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.MicroUnique);
        var policy = new TighteningPolicy();

        var defaults = TighteningOptions.Default;
        var policyOptions = PolicyOptions.Create(TighteningMode.EvidenceGated, defaults.Policy.NullBudget).Value;
        var uniqueness = UniquenessOptions.Create(enforceSingleColumnUnique: false, defaults.Uniqueness.EnforceMultiColumnUnique).Value;
        var options = TighteningOptions.Create(
            policyOptions,
            defaults.ForeignKeys,
            uniqueness,
            defaults.Remediation,
            defaults.Emission,
            defaults.Mocking).Value;

        var decisions = policy.Decide(model, snapshot, options);
        var entity = GetEntity(model, "User");
        var indexCoordinate = GetIndexCoordinate(entity, "UX_USER_EMAIL");
        var decision = decisions.UniqueIndexes[indexCoordinate];

        Assert.False(decision.EnforceUnique);
        Assert.Contains(TighteningRationales.UniquePolicyDisabled, decision.Rationales);
    }

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
