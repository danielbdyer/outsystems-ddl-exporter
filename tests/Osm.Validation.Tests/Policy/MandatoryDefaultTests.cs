using System;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Validation.Tightening;
using Tests.Support;
using Xunit;

namespace Osm.Validation.Tests.Policy;

public sealed class MandatoryDefaultTests
{
    [Fact]
    public void MandatoryDefaultColumns_TightenWhenDataIsClean()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var policy = new TighteningPolicy();
        var options = TighteningPolicyTestHelper.CreateOptions(TighteningMode.EvidenceGated);

        var decisions = Decide(policy, model, snapshot, options);

        var city = model.Modules.SelectMany(m => m.Entities)
            .Single(e => string.Equals(e.LogicalName.Value, "City", StringComparison.Ordinal));
        var attribute = city.Attributes.Single(a => string.Equals(a.LogicalName.Value, "IsActive", StringComparison.Ordinal));
        var coordinate = new ColumnCoordinate(city.Schema, city.PhysicalName, attribute.ColumnName);

        var decision = decisions.Nullability[coordinate].Outcome;

        Assert.True(decision.MakeNotNull);
        Assert.Contains(TighteningRationales.Mandatory, decision.Rationales);
        Assert.Contains(TighteningRationales.DefaultPresent, decision.Rationales);
        Assert.Contains(TighteningRationales.DataNoNulls, decision.Rationales);
    }

    [Fact]
    public void MandatoryColumnsWithoutDefault_TightenWhenDataIsClean()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var policy = new TighteningPolicy();
        var options = TighteningPolicyTestHelper.CreateOptions(TighteningMode.EvidenceGated);

        var decisions = Decide(policy, model, snapshot, options);

        var city = model.Modules.SelectMany(m => m.Entities)
            .Single(e => string.Equals(e.LogicalName.Value, "City", StringComparison.Ordinal));
        var attribute = city.Attributes.Single(a => string.Equals(a.LogicalName.Value, "Name", StringComparison.Ordinal));
        var coordinate = new ColumnCoordinate(city.Schema, city.PhysicalName, attribute.ColumnName);

        var decision = decisions.Nullability[coordinate].Outcome;

        Assert.True(decision.MakeNotNull);
        Assert.Contains(TighteningRationales.Mandatory, decision.Rationales);
        Assert.DoesNotContain(TighteningRationales.DefaultPresent, decision.Rationales);
        Assert.Contains(TighteningRationales.DataNoNulls, decision.Rationales);
    }

    private static PolicyDecisionSet Decide(
        TighteningPolicy policy,
        OsmModel model,
        ProfileSnapshot snapshot,
        TighteningOptions options)
    {
        var result = policy.Decide(model, snapshot, options);
        Assert.Equal(PolicyResultKind.Decision, result.Kind);
        return result.Decision;
    }
}
