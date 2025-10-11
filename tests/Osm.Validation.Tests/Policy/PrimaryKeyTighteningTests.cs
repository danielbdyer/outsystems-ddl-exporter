using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Validation.Tightening;
using Tests.Support;
using Xunit;

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

        var decisions = Decide(policy, model, snapshot, options);

        var primaryCoordinates = model.Modules
            .SelectMany(m => m.Entities)
            .SelectMany(entity => entity.Attributes
                .Where(attribute => attribute.IsIdentifier)
                .Select(attribute => new ColumnCoordinate(entity.Schema, entity.PhysicalName, attribute.ColumnName)))
            .ToArray();

        Assert.NotEmpty(primaryCoordinates);

        foreach (var coordinate in primaryCoordinates)
        {
            var decision = decisions.Nullability[coordinate].Outcome;
            Assert.True(decision.MakeNotNull);
            Assert.Contains(TighteningRationales.PrimaryKey, decision.Rationales);
            Assert.False(decision.RequiresRemediation);
        }
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
