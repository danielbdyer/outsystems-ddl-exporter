using Osm.Validation.Tightening;

namespace Osm.Validation.Tests.Policy;

public sealed class CrossCatalogSuppressionTests
{
    [Fact]
    public void EvidenceGated_SuppressesCrossCatalogForeignKeysByDefault()
    {
        var scenario = CrossScopeForeignKeyScenario.Create(targetSchema: "dbo", targetCatalog: "ReportingDb");
        var options = CrossScopeForeignKeyScenario.CreateOptions(allowCrossSchema: false, allowCrossCatalog: false);

        var decisions = new TighteningPolicy().Decide(scenario.Model, scenario.Snapshot, options);

        var fkDecision = decisions.ForeignKeys[scenario.Coordinate];
        Assert.False(fkDecision.CreateConstraint);
        Assert.Contains(TighteningRationales.CrossCatalog, fkDecision.Rationales);

        var nullability = decisions.Nullability[scenario.Coordinate];
        Assert.False(nullability.MakeNotNull);
        Assert.DoesNotContain(TighteningRationales.ForeignKeyEnforced, nullability.Rationales);
    }

    [Fact]
    public void EvidenceGated_AllowsCrossCatalogWhenToggleEnabled()
    {
        var scenario = CrossScopeForeignKeyScenario.Create(targetSchema: "dbo", targetCatalog: "ReportingDb");
        var options = CrossScopeForeignKeyScenario.CreateOptions(allowCrossSchema: false, allowCrossCatalog: true);

        var decisions = new TighteningPolicy().Decide(scenario.Model, scenario.Snapshot, options);

        var fkDecision = decisions.ForeignKeys[scenario.Coordinate];
        Assert.True(fkDecision.CreateConstraint);
        Assert.Contains(TighteningRationales.PolicyEnableCreation, fkDecision.Rationales);
        Assert.DoesNotContain(TighteningRationales.CrossCatalog, fkDecision.Rationales);

        var nullability = decisions.Nullability[scenario.Coordinate];
        Assert.True(nullability.MakeNotNull);
        Assert.False(nullability.RequiresRemediation);
        Assert.Contains(TighteningRationales.ForeignKeyEnforced, nullability.Rationales);
        Assert.Contains(TighteningRationales.DataNoNulls, nullability.Rationales);
    }
}
