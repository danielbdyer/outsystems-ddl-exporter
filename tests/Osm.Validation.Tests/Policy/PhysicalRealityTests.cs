using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Validation.Tightening;
using Tests.Support;
using Xunit;

namespace Osm.Validation.Tests.Policy;

public sealed class PhysicalRealityTests
{
    [Fact]
    public void PhysicalNotNullColumns_AreHonoredEvenInCautiousMode()
    {
        var model = ModelFixtures.LoadModel("model.micro-physical.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.MicroPhysical);
        var policy = new TighteningPolicy();
        var options = TighteningPolicyTestHelper.CreateOptions(TighteningMode.Cautious);

        var decisions = Decide(policy, model, snapshot, options);
        var entity = model.Modules.SelectMany(m => m.Entities).Single();
        var attribute = entity.Attributes.Single(a => a.LogicalName.Value == "ExternalCode");
        var coordinate = new ColumnCoordinate(entity.Schema, entity.PhysicalName, attribute.ColumnName);

        var decision = decisions.Nullability[coordinate].Outcome;

        Assert.True(decision.MakeNotNull);
        Assert.Contains(TighteningRationales.PhysicalNotNull, decision.Rationales);
        Assert.DoesNotContain(TighteningRationales.DataNoNulls, decision.Rationales);
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
