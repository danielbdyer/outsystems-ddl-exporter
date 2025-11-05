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
        Assert.Same(sampleSecondary, column.NullRowSample);
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
