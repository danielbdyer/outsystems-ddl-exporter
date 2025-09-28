using System.Linq;
using Osm.Domain.Configuration;
using Osm.Validation.Tightening;
using Tests.Support;

namespace Osm.Validation.Tests.Policy;

public sealed class DecisionReportTests
{
    [Fact]
    public void Reporter_AggregatesCountsAndRationales()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var policy = new TighteningPolicy();
        var decisions = policy.Decide(model, snapshot, TighteningOptions.Default);

        var report = PolicyDecisionReporter.Create(decisions);

        var expectedColumnCount = model.Modules.SelectMany(m => m.Entities).Sum(e => e.Attributes.Length);
        Assert.Equal(expectedColumnCount, report.ColumnCount);
        Assert.True(report.TightenedColumnCount > 0);
        Assert.Equal(0, report.RemediationColumnCount);

        Assert.True(report.UniqueIndexCount > 0);
        Assert.Equal(report.UniqueIndexCount, report.UniqueIndexesEnforcedCount);
        Assert.Equal(0, report.UniqueIndexesRequireRemediationCount);

        Assert.Equal(2, report.ForeignKeyCount);
        Assert.Equal(1, report.ForeignKeysCreatedCount);

        Assert.Equal(2, report.ColumnRationaleCounts[TighteningRationales.UniqueNoNulls]);
        Assert.True(report.UniqueIndexRationaleCounts[TighteningRationales.PhysicalUniqueKey] >= 2);
        Assert.Equal(1, report.ColumnRationaleCounts[TighteningRationales.ForeignKeyEnforced]);
        Assert.Equal(1, report.ForeignKeyRationaleCounts[TighteningRationales.DataHasOrphans]);

        var suppressedForeignKey = Assert.Single(report.ForeignKeys.Where(f => !f.CreateConstraint));
        Assert.Contains(TighteningRationales.DeleteRuleIgnore, suppressedForeignKey.Rationales);
    }
}
