using System;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Pipeline.Profiling;
using Xunit;

namespace Tests.Osm.Pipeline.Tests.Profiling;

public sealed class MultiEnvironmentProfileReportTests
{
    [Fact]
    public void Create_generates_actionable_findings()
    {
        var capturedAt = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var nullProbe = ProfilingProbeStatus.CreateSucceeded(capturedAt, 1_000);

        var primarySnapshot = new ProfileSnapshot(
            ImmutableArray.Create(new ColumnProfile(
                new SchemaName("dbo"),
                new TableName("Customer"),
                new ColumnName("Email"),
                true,
                false,
                false,
                false,
                null,
                10_000,
                0,
                nullProbe,
                null)),
            ImmutableArray.Create(new UniqueCandidateProfile(
                new SchemaName("dbo"),
                new TableName("Customer"),
                new ColumnName("Email"),
                false,
                nullProbe)),
            ImmutableArray<CompositeUniqueCandidateProfile>.Empty,
            ImmutableArray.Create(new ForeignKeyReality(
                new ForeignKeyReference(
                    new SchemaName("dbo"),
                    new TableName("Order"),
                    new ColumnName("CustomerId"),
                    new SchemaName("dbo"),
                    new TableName("Customer"),
                    new ColumnName("Id"),
                    true),
                false,
                false,
                ProfilingProbeStatus.CreateSucceeded(capturedAt, 1_000))));

        var secondarySnapshot = new ProfileSnapshot(
            ImmutableArray.Create(new ColumnProfile(
                new SchemaName("dbo"),
                new TableName("Customer"),
                new ColumnName("Email"),
                true,
                false,
                false,
                false,
                null,
                9_500,
                250,
                nullProbe,
                null)),
            ImmutableArray.Create(new UniqueCandidateProfile(
                new SchemaName("dbo"),
                new TableName("Customer"),
                new ColumnName("Email"),
                true,
                nullProbe)),
            ImmutableArray<CompositeUniqueCandidateProfile>.Empty,
            ImmutableArray.Create(new ForeignKeyReality(
                new ForeignKeyReference(
                    new SchemaName("dbo"),
                    new TableName("Order"),
                    new ColumnName("CustomerId"),
                    new SchemaName("dbo"),
                    new TableName("Customer"),
                    new ColumnName("Id"),
                    false),
                true,
                false,
                ProfilingProbeStatus.CreateFallbackTimeout(capturedAt, 100))));

        var report = MultiEnvironmentProfileReport.Create(new[]
        {
            new ProfilingEnvironmentSnapshot(
                "Primary",
                true,
                MultiTargetSqlDataProfiler.EnvironmentLabelOrigin.Provided,
                false,
                primarySnapshot,
                TimeSpan.FromSeconds(15)),
            new ProfilingEnvironmentSnapshot(
                "QA",
                false,
                MultiTargetSqlDataProfiler.EnvironmentLabelOrigin.Provided,
                false,
                secondarySnapshot,
                TimeSpan.FromMinutes(2))
        });

        Assert.False(report.Findings.IsDefaultOrEmpty);
        Assert.True(report.Findings.Length >= 3);

        var nullFinding = Assert.Single(report.Findings.Where(f => f.Code == "profiling.multiEnvironment.nulls"));
        Assert.Contains(nullFinding.AffectedObjects, item => item.Contains("dbo.Customer.Email", StringComparison.Ordinal));

        var uniqueFinding = Assert.Single(report.Findings.Where(f => f.Code == "profiling.multiEnvironment.uniqueness"));
        Assert.Contains(uniqueFinding.AffectedObjects, item => item.Contains("dbo.Customer.Email", StringComparison.Ordinal));

        var foreignKeyFinding = Assert.Single(report.Findings.Where(f => f.Code == "profiling.multiEnvironment.foreignKey"));
        Assert.Contains(
            foreignKeyFinding.AffectedObjects,
            item => item.Contains("dbo.Order.CustomerId", StringComparison.Ordinal));

        var probeFinding = Assert.Single(report.Findings.Where(f => f.Code == "profiling.multiEnvironment.foreignKey.evidence"));
        Assert.Contains(
            probeFinding.AffectedObjects,
            item => item.Contains("Probe", StringComparison.Ordinal));
    }
}
