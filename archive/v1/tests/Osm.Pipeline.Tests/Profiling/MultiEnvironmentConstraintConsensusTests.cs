using System;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Pipeline.Profiling;
using Xunit;

namespace Tests.Osm.Pipeline.Tests.Profiling;

public sealed class MultiEnvironmentConstraintConsensusTests
{
    [Fact]
    public void Analyze_MarksMissingEnvironmentAsUnsafeForNotNull()
    {
        var probe = ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UtcNow, 1000);

        var primaryColumn = ColumnProfile.Create(
            new SchemaName("dbo"),
            new TableName("Customer"),
            new ColumnName("Email"),
            isNullablePhysical: true,
            isComputed: false,
            isPrimaryKey: false,
            isUniqueKey: false,
            defaultDefinition: null,
            rowCount: 1000,
            nullCount: 0,
            nullCountStatus: probe,
            nullRowSample: null).Value;

        var primarySnapshot = ProfileSnapshot.Create(
            new[] { primaryColumn },
            Array.Empty<UniqueCandidateProfile>(),
            Array.Empty<CompositeUniqueCandidateProfile>(),
            Array.Empty<ForeignKeyReality>()).Value;

        var secondarySnapshot = ProfileSnapshot.Create(
            Array.Empty<ColumnProfile>(),
            Array.Empty<UniqueCandidateProfile>(),
            Array.Empty<CompositeUniqueCandidateProfile>(),
            Array.Empty<ForeignKeyReality>()).Value;

        var consensus = MultiEnvironmentConstraintConsensus.Analyze(new[]
        {
            new ProfilingEnvironmentSnapshot(
                "Primary",
                true,
                MultiTargetSqlDataProfiler.EnvironmentLabelOrigin.Provided,
                false,
                primarySnapshot,
                TimeSpan.FromSeconds(5)),
            new ProfilingEnvironmentSnapshot(
                "QA",
                false,
                MultiTargetSqlDataProfiler.EnvironmentLabelOrigin.Provided,
                false,
                secondarySnapshot,
                TimeSpan.FromSeconds(5))
        });

        var notNull = Assert.Single(consensus.NullabilityConsensus);
        Assert.False(notNull.IsSafeToApply);
        Assert.Equal(1, notNull.SafeEnvironmentCount);
        Assert.Equal(2, notNull.TotalEnvironmentCount);
        Assert.Equal(0.5, notNull.ConsensusRatio);
        Assert.Contains("Missing in environments", notNull.Recommendation, StringComparison.OrdinalIgnoreCase);
    }
}
