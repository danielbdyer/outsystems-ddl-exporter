using System.Linq;
using Osm.Pipeline.Profiling;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests;

public sealed class FakeProfilerTests
{
    [Fact]
    public async Task CaptureAsync_ShouldReturnSnapshotFromFixture()
    {
        var profiler = new FakeProfiler(FixtureProfileSource.EdgeCase);

        var result = await profiler.CaptureAsync();

        Assert.True(result.IsSuccess);
        var snapshot = result.Value.Snapshot;

        Assert.Equal(14, snapshot.Columns.Length);
        var email = snapshot.Columns.Single(c => c.Table.Value == "OSUSR_ABC_CUSTOMER" && c.Column.Value == "EMAIL");
        Assert.True(email.IsUniqueKey);
        Assert.Equal(0, email.NullCount);

        var fk = snapshot.ForeignKeys.Single(f => f.Reference.FromTable.Value == "OSUSR_XYZ_JOBRUN");
        Assert.True(fk.HasOrphan);
        Assert.False(fk.Reference.HasDatabaseConstraint);
        Assert.True(fk.IsNoCheck);

        var accountNumber = snapshot.UniqueCandidates.Single(u => u.Column.Value == "ACCOUNTNUMBER");
        Assert.False(accountNumber.HasDuplicate);
    }
}
