using Osm.Validation.Tightening;

namespace Osm.Validation.Tests.Policy;

public sealed class CrossSchemaToggleTests
{
    [Fact]
    public void EvidenceGated_SuppressesCrossSchemaForeignKeysByDefault()
    {
        var scenario = CrossScopeForeignKeyScenario.Create(targetSchema: "crm");
        var options = CrossScopeForeignKeyScenario.CreateOptions(allowCrossSchema: false, allowCrossCatalog: false);

        var decisions = new TighteningPolicy().Decide(scenario.Model, scenario.Snapshot, options);

        var fkDecision = decisions.ForeignKeys[scenario.Coordinate];
        Assert.False(fkDecision.CreateConstraint);
        Assert.Contains(TighteningRationales.CrossSchema, fkDecision.Rationales);

        var nullability = decisions.Nullability[scenario.Coordinate];
        Assert.False(nullability.MakeNotNull);
        Assert.DoesNotContain(TighteningRationales.ForeignKeyEnforced, nullability.Rationales);
    }

    [Fact]
    public void EvidenceGated_AllowsCrossSchemaWhenToggleEnabled()
    {
        var scenario = CrossScopeForeignKeyScenario.Create(targetSchema: "crm");
        var options = CrossScopeForeignKeyScenario.CreateOptions(allowCrossSchema: true, allowCrossCatalog: false);

        var decisions = new TighteningPolicy().Decide(scenario.Model, scenario.Snapshot, options);

        var fkDecision = decisions.ForeignKeys[scenario.Coordinate];
        Assert.True(fkDecision.CreateConstraint);
        Assert.Contains(TighteningRationales.PolicyEnableCreation, fkDecision.Rationales);
        Assert.DoesNotContain(TighteningRationales.CrossSchema, fkDecision.Rationales);

        var nullability = decisions.Nullability[scenario.Coordinate];
        Assert.True(nullability.MakeNotNull);
        Assert.False(nullability.RequiresRemediation);
        Assert.Contains(TighteningRationales.ForeignKeyEnforced, nullability.Rationales);
        Assert.Contains(TighteningRationales.DataNoNulls, nullability.Rationales);
    }
}
