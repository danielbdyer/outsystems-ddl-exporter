using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Pipeline.Profiling;
using Xunit;

namespace Tests.Osm.Pipeline.Tests.Profiling;

public sealed class MultiTargetSqlDataProfilerTests
{
    [Fact]
    public async Task CaptureAsync_PrefersMostInformativeNullRowSample()
    {
        var probe = ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UtcNow, 1000);

        var samplePrimary = NullRowSample.Create(
            ImmutableArray.Create("Id"),
            ImmutableArray.Create(new NullRowIdentifier(ImmutableArray.Create<object?>(1))),
            totalNullRows: 1);

        var primaryColumn = ColumnProfile.Create(
            new SchemaName("dbo"),
            new TableName("Customer"),
            new ColumnName("Email"),
            isNullablePhysical: true,
            isComputed: false,
            isPrimaryKey: false,
            isUniqueKey: false,
            defaultDefinition: null,
            rowCount: 100,
            nullCount: 1,
            nullCountStatus: probe,
            nullRowSample: samplePrimary).Value;

        var sampleSecondary = NullRowSample.Create(
            ImmutableArray.Create("Id"),
            ImmutableArray.Create(new NullRowIdentifier(ImmutableArray.Create<object?>(2))),
            totalNullRows: 3);

        var secondaryColumn = ColumnProfile.Create(
            new SchemaName("dbo"),
            new TableName("Customer"),
            new ColumnName("Email"),
            isNullablePhysical: true,
            isComputed: false,
            isPrimaryKey: false,
            isUniqueKey: false,
            defaultDefinition: null,
            rowCount: 200,
            nullCount: 3,
            nullCountStatus: probe,
            nullRowSample: sampleSecondary).Value;

        var primarySnapshot = ProfileSnapshot.Create(
            new[] { primaryColumn },
            Array.Empty<UniqueCandidateProfile>(),
            Array.Empty<CompositeUniqueCandidateProfile>(),
            Array.Empty<ForeignKeyReality>()).Value;

        var secondarySnapshot = ProfileSnapshot.Create(
            new[] { secondaryColumn },
            Array.Empty<UniqueCandidateProfile>(),
            Array.Empty<CompositeUniqueCandidateProfile>(),
            Array.Empty<ForeignKeyReality>()).Value;

        var profiler = new MultiTargetSqlDataProfiler(
            new MultiTargetSqlDataProfiler.ProfilerEnvironment("Primary", new StubProfiler(primarySnapshot), true),
            new[]
            {
                new MultiTargetSqlDataProfiler.ProfilerEnvironment(
                    "Secondary",
                    new StubProfiler(secondarySnapshot),
                    isPrimary: false)
            });

        var result = await profiler.CaptureAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var column = Assert.Single(result.Value.Columns);
        Assert.Equal(3, column.NullCount);

        var sample = Assert.IsType<NullRowSample>(column.NullRowSample);
        Assert.Equal(3, sample.TotalNullRows);
        Assert.True(sample.IsTruncated);
        Assert.Equal(2, sample.SampleRows.Length);
        Assert.Contains(samplePrimary.SampleRows[0], sample.SampleRows);
        Assert.Contains(sampleSecondary.SampleRows[0], sample.SampleRows);
    }

    [Fact]
    public async Task CaptureAsync_PrefersMostInformativeOrphanSample()
    {
        var probe = ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UtcNow, 1000);
        var reference = ForeignKeyReference.Create(
            new SchemaName("dbo"),
            new TableName("Order"),
            new ColumnName("CustomerId"),
            new SchemaName("dbo"),
            new TableName("Customer"),
            new ColumnName("Id"),
            hasDatabaseConstraint: false).Value;

        var primarySample = ForeignKeyOrphanSample.Create(
            ImmutableArray.Create("Id"),
            "CustomerId",
            ImmutableArray.Create(new ForeignKeyOrphanIdentifier(ImmutableArray.Create<object?>(1), 99)),
            totalOrphans: 1);

        var primaryForeignKey = ForeignKeyReality.Create(
            reference,
            hasOrphan: true,
            orphanCount: 1,
            isNoCheck: false,
            probeStatus: probe,
            orphanSample: primarySample).Value;

        var secondarySample = ForeignKeyOrphanSample.Create(
            ImmutableArray.Create("Id"),
            "CustomerId",
            ImmutableArray.Create(
                new ForeignKeyOrphanIdentifier(ImmutableArray.Create<object?>(2), 100),
                new ForeignKeyOrphanIdentifier(ImmutableArray.Create<object?>(3), 101)),
            totalOrphans: 5);

        var secondaryForeignKey = ForeignKeyReality.Create(
            reference,
            hasOrphan: true,
            orphanCount: 5,
            isNoCheck: false,
            probeStatus: probe,
            orphanSample: secondarySample).Value;

        var primarySnapshot = ProfileSnapshot.Create(
            Array.Empty<ColumnProfile>(),
            Array.Empty<UniqueCandidateProfile>(),
            Array.Empty<CompositeUniqueCandidateProfile>(),
            new[] { primaryForeignKey }).Value;

        var secondarySnapshot = ProfileSnapshot.Create(
            Array.Empty<ColumnProfile>(),
            Array.Empty<UniqueCandidateProfile>(),
            Array.Empty<CompositeUniqueCandidateProfile>(),
            new[] { secondaryForeignKey }).Value;

        var profiler = new MultiTargetSqlDataProfiler(
            new MultiTargetSqlDataProfiler.ProfilerEnvironment("Primary", new StubProfiler(primarySnapshot), true),
            new[]
            {
                new MultiTargetSqlDataProfiler.ProfilerEnvironment(
                    "Secondary",
                    new StubProfiler(secondarySnapshot),
                    isPrimary: false)
            });

        var result = await profiler.CaptureAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var foreignKey = Assert.Single(result.Value.ForeignKeys);
        Assert.Equal(5, foreignKey.OrphanCount);

        var sample = Assert.IsType<ForeignKeyOrphanSample>(foreignKey.OrphanSample);
        Assert.Equal(5, sample.TotalOrphans);
        Assert.True(sample.IsTruncated);
        Assert.Equal(3, sample.SampleRows.Length);
        Assert.Contains(primarySample.SampleRows[0], sample.SampleRows);
        Assert.Contains(secondarySample.SampleRows[0], sample.SampleRows);
        Assert.Contains(secondarySample.SampleRows[1], sample.SampleRows);
    }

    [Fact]
    public async Task CaptureAsync_RetainsNullSamplesWhenSecondaryLacksRows()
    {
        var probe = ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UtcNow, 1000);

        var primarySample = NullRowSample.Create(
            ImmutableArray.Create("Id"),
            ImmutableArray.Create(new NullRowIdentifier(ImmutableArray.Create<object?>(1))),
            totalNullRows: 1);

        var primaryColumn = ColumnProfile.Create(
            new SchemaName("dbo"),
            new TableName("Customer"),
            new ColumnName("Email"),
            isNullablePhysical: false,
            isComputed: false,
            isPrimaryKey: false,
            isUniqueKey: false,
            defaultDefinition: null,
            rowCount: 100,
            nullCount: 1,
            nullCountStatus: probe,
            nullRowSample: primarySample).Value;

        var secondarySample = NullRowSample.Create(
            ImmutableArray<string>.Empty,
            ImmutableArray<NullRowIdentifier>.Empty,
            totalNullRows: 5);

        var secondaryColumn = ColumnProfile.Create(
            new SchemaName("dbo"),
            new TableName("Customer"),
            new ColumnName("Email"),
            isNullablePhysical: false,
            isComputed: false,
            isPrimaryKey: false,
            isUniqueKey: false,
            defaultDefinition: null,
            rowCount: 200,
            nullCount: 5,
            nullCountStatus: probe,
            nullRowSample: secondarySample).Value;

        var primarySnapshot = ProfileSnapshot.Create(
            new[] { primaryColumn },
            Array.Empty<UniqueCandidateProfile>(),
            Array.Empty<CompositeUniqueCandidateProfile>(),
            Array.Empty<ForeignKeyReality>()).Value;

        var secondarySnapshot = ProfileSnapshot.Create(
            new[] { secondaryColumn },
            Array.Empty<UniqueCandidateProfile>(),
            Array.Empty<CompositeUniqueCandidateProfile>(),
            Array.Empty<ForeignKeyReality>()).Value;

        var profiler = new MultiTargetSqlDataProfiler(
            new MultiTargetSqlDataProfiler.ProfilerEnvironment("Primary", new StubProfiler(primarySnapshot), true),
            new[]
            {
                new MultiTargetSqlDataProfiler.ProfilerEnvironment(
                    "Secondary",
                    new StubProfiler(secondarySnapshot),
                    isPrimary: false)
            });

        var result = await profiler.CaptureAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var column = Assert.Single(result.Value.Columns);
        Assert.Equal(5, column.NullCount);

        var sample = Assert.IsType<NullRowSample>(column.NullRowSample);
        Assert.Equal(5, sample.TotalNullRows);
        Assert.True(sample.IsTruncated);
        Assert.Single(sample.SampleRows);
        Assert.Contains(primarySample.SampleRows[0], sample.SampleRows);
    }

    [Fact]
    public async Task CaptureAsync_RetainsOrphanSamplesWhenSecondaryLacksRows()
    {
        var probe = ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UtcNow, 1000);
        var reference = ForeignKeyReference.Create(
            new SchemaName("dbo"),
            new TableName("Order"),
            new ColumnName("CustomerId"),
            new SchemaName("dbo"),
            new TableName("Customer"),
            new ColumnName("Id"),
            hasDatabaseConstraint: false).Value;

        var primarySample = ForeignKeyOrphanSample.Create(
            ImmutableArray.Create("Id"),
            "CustomerId",
            ImmutableArray.Create(new ForeignKeyOrphanIdentifier(ImmutableArray.Create<object?>(1), 42)),
            totalOrphans: 1);

        var primaryForeignKey = ForeignKeyReality.Create(
            reference,
            hasOrphan: true,
            orphanCount: 1,
            isNoCheck: false,
            probeStatus: probe,
            orphanSample: primarySample).Value;

        var secondarySample = ForeignKeyOrphanSample.Create(
            ImmutableArray<string>.Empty,
            "CustomerId",
            ImmutableArray<ForeignKeyOrphanIdentifier>.Empty,
            totalOrphans: 5);

        var secondaryForeignKey = ForeignKeyReality.Create(
            reference,
            hasOrphan: true,
            orphanCount: 5,
            isNoCheck: false,
            probeStatus: probe,
            orphanSample: secondarySample).Value;

        var primarySnapshot = ProfileSnapshot.Create(
            Array.Empty<ColumnProfile>(),
            Array.Empty<UniqueCandidateProfile>(),
            Array.Empty<CompositeUniqueCandidateProfile>(),
            new[] { primaryForeignKey }).Value;

        var secondarySnapshot = ProfileSnapshot.Create(
            Array.Empty<ColumnProfile>(),
            Array.Empty<UniqueCandidateProfile>(),
            Array.Empty<CompositeUniqueCandidateProfile>(),
            new[] { secondaryForeignKey }).Value;

        var profiler = new MultiTargetSqlDataProfiler(
            new MultiTargetSqlDataProfiler.ProfilerEnvironment("Primary", new StubProfiler(primarySnapshot), true),
            new[]
            {
                new MultiTargetSqlDataProfiler.ProfilerEnvironment(
                    "Secondary",
                    new StubProfiler(secondarySnapshot),
                    isPrimary: false)
            });

        var result = await profiler.CaptureAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var foreignKey = Assert.Single(result.Value.ForeignKeys);
        Assert.Equal(5, foreignKey.OrphanCount);

        var sample = Assert.IsType<ForeignKeyOrphanSample>(foreignKey.OrphanSample);
        Assert.Equal(5, sample.TotalOrphans);
        Assert.True(sample.IsTruncated);
        Assert.Single(sample.SampleRows);
        Assert.Contains(primarySample.SampleRows[0], sample.SampleRows);
    }

    private sealed class StubProfiler : IDataProfiler
    {
        private readonly Result<ProfileSnapshot> _result;

        public StubProfiler(ProfileSnapshot snapshot)
        {
            _result = Result<ProfileSnapshot>.Success(snapshot);
        }

        public Task<Result<ProfileSnapshot>> CaptureAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }
    }
}
