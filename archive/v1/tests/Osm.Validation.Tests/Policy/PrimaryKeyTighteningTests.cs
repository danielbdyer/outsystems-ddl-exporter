using System.Linq;
using Osm.Domain.Configuration;
using Osm.Validation.Tightening;
using Tests.Support;

namespace Osm.Validation.Tests.Policy;

public sealed class PrimaryKeyTighteningTests
{
    [Fact]
    public void PrimaryKeyColumns_AreAlwaysTightened()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var policy = new TighteningPolicy();
        var options = TighteningPolicyTestHelper.CreateOptions(TighteningMode.Cautious);

        var decisions = policy.Decide(model, snapshot, options);

        var primaryCoordinates = model.Modules
            .SelectMany(m => m.Entities)
            .SelectMany(entity => entity.Attributes
                .Where(attribute => attribute.IsIdentifier)
                .Select(attribute => new ColumnCoordinate(entity.Schema, entity.PhysicalName, attribute.ColumnName)))
            .ToArray();

        Assert.NotEmpty(primaryCoordinates);

        foreach (var coordinate in primaryCoordinates)
        {
            var decision = decisions.Nullability[coordinate];
            Assert.True(decision.MakeNotNull);
            Assert.Contains(TighteningRationales.PrimaryKey, decision.Rationales);
            Assert.False(decision.RequiresRemediation);
        }
    }
}
