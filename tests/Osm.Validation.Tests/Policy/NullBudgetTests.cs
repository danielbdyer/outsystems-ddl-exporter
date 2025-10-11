using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Validation.Tightening;
using Tests.Support;
using Xunit;

namespace Osm.Validation.Tests.Policy;

public sealed class NullBudgetTests
{
    [Fact]
    public void NullBudget_AllowsTinyNullRatesWithinTolerance()
    {
        var model = ModelFixtures.LoadModel("model.micro-unique.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.MicroUniqueWithNullDrift);
        var policy = new TighteningPolicy();

        var strictOptions = TighteningPolicyTestHelper.CreateOptions(TighteningMode.EvidenceGated, nullBudget: 0.0);
        var tolerantOptions = TighteningPolicyTestHelper.CreateOptions(TighteningMode.EvidenceGated, nullBudget: 0.1);

        var strictDecision = Decide(policy, model, snapshot, strictOptions);
        var tolerantDecision = Decide(policy, model, snapshot, tolerantOptions);

        var entity = model.Modules.SelectMany(m => m.Entities).Single();
        var attribute = entity.Attributes.Single(a => a.LogicalName.Value == "Email");
        var coordinate = new ColumnCoordinate(entity.Schema, entity.PhysicalName, attribute.ColumnName);

        Assert.False(strictDecision.Nullability[coordinate].Outcome.MakeNotNull);

        var tolerant = tolerantDecision.Nullability[coordinate].Outcome;
        Assert.True(tolerant.MakeNotNull);
        Assert.Contains(TighteningRationales.NullBudgetEpsilon, tolerant.Rationales);
        Assert.Contains(TighteningRationales.DataNoNulls, tolerant.Rationales);
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
