using System.Linq;
using Osm.Domain.Configuration;
using Osm.Validation.Tightening;
using Tests.Support;

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

        var decisions = policy.Decide(model, snapshot, options);
        var entity = model.Modules.SelectMany(m => m.Entities).Single();
        var attribute = entity.Attributes.Single(a => a.LogicalName.Value == "ExternalCode");
        var coordinate = new ColumnCoordinate(entity.Schema, entity.PhysicalName, attribute.ColumnName);

        var decision = decisions.Nullability[coordinate];

        Assert.True(decision.MakeNotNull);
        Assert.Contains(TighteningRationales.PhysicalNotNull, decision.Rationales);
        Assert.DoesNotContain(TighteningRationales.DataNoNulls, decision.Rationales);
    }
}
