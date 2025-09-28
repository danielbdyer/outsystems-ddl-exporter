using System;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Osm.Json;
using Osm.Pipeline.Profiling;
using Tests.Support;

namespace Osm.Pipeline.Tests;

public sealed class FixtureDataProfilerTests
{
    [Fact]
    public async Task CaptureAsync_ShouldReturnSnapshot_WhenFixtureExists()
    {
        var path = FixtureFile.GetPath(FixtureProfileSource.EdgeCase);
        var profiler = new FixtureDataProfiler(path, new ProfileSnapshotDeserializer());

        var result = await profiler.CaptureAsync();

        Assert.True(result.IsSuccess);
        var snapshot = result.Value;
        Assert.Equal(14, snapshot.Columns.Length);

        var triggered = snapshot.Columns.Single(c => c.Table.Value == "OSUSR_XYZ_JOBRUN" && c.Column.Value == "TRIGGEREDBYUSERID");
        Assert.Equal(950_000, triggered.NullCount);

        var accountNumber = snapshot.UniqueCandidates.Single(u => u.Column.Value == "ACCOUNTNUMBER");
        Assert.False(accountNumber.HasDuplicate);

        var fk = snapshot.ForeignKeys.Single(f => f.Reference.FromTable.Value == "OSUSR_XYZ_JOBRUN");
        Assert.True(fk.HasOrphan);
    }

    [Fact]
    public async Task CaptureAsync_ShouldPreserveExternalSchemaAndUniqueSignals()
    {
        var path = FixtureFile.GetPath(FixtureProfileSource.EdgeCase);
        var profiler = new FixtureDataProfiler(path, new ProfileSnapshotDeserializer());

        var result = await profiler.CaptureAsync();

        Assert.True(result.IsSuccess);
        var snapshot = result.Value;

        var externalColumns = snapshot.Columns
            .Where(c => string.Equals(c.Schema.Value, "billing", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.NotEmpty(externalColumns);
        Assert.All(externalColumns, column => Assert.Equal("BILLING_ACCOUNT", column.Table.Value));

        var uniqueAccount = snapshot.UniqueCandidates.Single(candidate =>
            candidate.Table.Value == "BILLING_ACCOUNT" && candidate.Column.Value == "ACCOUNTNUMBER");

        Assert.False(uniqueAccount.HasDuplicate);
    }

    [Fact]
    public async Task CaptureAsync_ShouldSurfaceIgnoreForeignKeyReality()
    {
        var path = FixtureFile.GetPath(FixtureProfileSource.EdgeCase);
        var profiler = new FixtureDataProfiler(path, new ProfileSnapshotDeserializer());

        var result = await profiler.CaptureAsync();

        Assert.True(result.IsSuccess);
        var snapshot = result.Value;

        var ignoreReference = snapshot.ForeignKeys.Single(fk =>
            fk.Reference.FromTable.Value == "OSUSR_XYZ_JOBRUN" &&
            fk.Reference.FromColumn.Value == "TRIGGEREDBYUSERID");

        Assert.False(ignoreReference.Reference.HasDatabaseConstraint);
        Assert.True(ignoreReference.HasOrphan);
    }

    [Fact]
    public async Task CaptureAsync_ShouldPreservePhysicalMetadataFlags()
    {
        var path = FixtureFile.GetPath(FixtureProfileSource.EdgeCase);
        var profiler = new FixtureDataProfiler(path, new ProfileSnapshotDeserializer());

        var result = await profiler.CaptureAsync();

        Assert.True(result.IsSuccess);
        var snapshot = result.Value;

        var createdOn = snapshot.Columns.Single(c =>
            c.Table.Value == "OSUSR_XYZ_JOBRUN" &&
            c.Column.Value == "CREATEDON");

        Assert.True(createdOn.IsComputed);
        Assert.True(createdOn.IsNullablePhysical);
        Assert.Equal("(getutcdate())", createdOn.DefaultDefinition);

        var firstName = snapshot.Columns.Single(c =>
            c.Table.Value == "OSUSR_ABC_CUSTOMER" &&
            c.Column.Value == "FIRSTNAME");

        Assert.True(firstName.IsNullablePhysical);
        Assert.False(firstName.IsComputed);
        Assert.Equal("('')", firstName.DefaultDefinition);

        var identifier = snapshot.Columns.Single(c =>
            c.Table.Value == "OSUSR_ABC_CUSTOMER" &&
            c.Column.Value == "ID");

        Assert.False(identifier.IsNullablePhysical);
        Assert.True(identifier.IsPrimaryKey);
    }

    [Fact]
    public async Task CaptureAsync_ShouldReturnFailure_WhenFixtureMissing()
    {
        var fileSystem = new MockFileSystem();
        var profiler = new FixtureDataProfiler("missing.json", new ProfileSnapshotDeserializer(), fileSystem);

        var result = await profiler.CaptureAsync();

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("profiler.fixture.missing", error.Code);
    }
}
