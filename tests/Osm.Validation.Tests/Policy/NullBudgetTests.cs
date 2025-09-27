using System.Linq;
using Osm.Domain.Configuration;
using Osm.Validation.Tightening;
using Tests.Support;

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

        var strictDecision = policy.Decide(model, snapshot, strictOptions);
        var tolerantDecision = policy.Decide(model, snapshot, tolerantOptions);

        var entity = model.Modules.SelectMany(m => m.Entities).Single();
        var attribute = entity.Attributes.Single(a => a.LogicalName.Value == "Email");
        var coordinate = new ColumnCoordinate(entity.Schema, entity.PhysicalName, attribute.ColumnName);

        Assert.False(strictDecision.Nullability[coordinate].MakeNotNull);

        var tolerant = tolerantDecision.Nullability[coordinate];
        Assert.True(tolerant.MakeNotNull);
        Assert.Contains(TighteningRationales.NullBudgetEpsilon, tolerant.Rationales);
        Assert.Contains(TighteningRationales.DataNoNulls, tolerant.Rationales);
    }
}
