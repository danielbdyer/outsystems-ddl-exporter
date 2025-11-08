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
            ImmutableArray.Create(ForeignKeyReality.Create(
                new ForeignKeyReference(
                    new SchemaName("dbo"),
                    new TableName("Order"),
                    new ColumnName("CustomerId"),
                    new SchemaName("dbo"),
                    new TableName("Customer"),
                    new ColumnName("Id"),
                    true),
                hasOrphan: false,
                orphanCount: 0,
                isNoCheck: false,
                probeStatus: ProfilingProbeStatus.CreateSucceeded(capturedAt, 1_000)).Value));

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
            ImmutableArray.Create(ForeignKeyReality.Create(
                new ForeignKeyReference(
                    new SchemaName("dbo"),
                    new TableName("Order"),
                    new ColumnName("CustomerId"),
                    new SchemaName("dbo"),
                    new TableName("Customer"),
                    new ColumnName("Id"),
                    false),
                hasOrphan: true,
                orphanCount: 25,
                isNoCheck: false,
                probeStatus: ProfilingProbeStatus.CreateFallbackTimeout(capturedAt, 100)).Value));

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

        Assert.DoesNotContain(report.Findings, finding => finding.Code == "profiling.multiEnvironment.nulls");

        var nullVarianceFinding = Assert.Single(
            report.Findings.Where(f => f.Code == "profiling.validation.dataQuality.nullVariance"));
        Assert.Equal(MultiEnvironmentFindingSeverity.Advisory, nullVarianceFinding.Severity);

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

    [Fact]
    public void Create_surfaces_cross_environment_standardization_gaps()
    {
        var capturedAt = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var successProbe = ProfilingProbeStatus.CreateSucceeded(capturedAt, 1_000);

        var primarySnapshot = new ProfileSnapshot(
            ImmutableArray.Create(new ColumnProfile(
                new SchemaName("dbo"),
                new TableName("Orders"),
                new ColumnName("Id"),
                true,
                false,
                false,
                false,
                null,
                10_000,
                0,
                successProbe,
                null)),
            ImmutableArray<UniqueCandidateProfile>.Empty,
            ImmutableArray<CompositeUniqueCandidateProfile>.Empty,
            ImmutableArray<ForeignKeyReality>.Empty);

        var secondarySnapshot = new ProfileSnapshot(
            ImmutableArray<ColumnProfile>.Empty,
            ImmutableArray<UniqueCandidateProfile>.Empty,
            ImmutableArray<CompositeUniqueCandidateProfile>.Empty,
            ImmutableArray<ForeignKeyReality>.Empty);

        var report = MultiEnvironmentProfileReport.Create(new[]
        {
            new ProfilingEnvironmentSnapshot(
                "Production",
                true,
                MultiTargetSqlDataProfiler.EnvironmentLabelOrigin.Provided,
                false,
                primarySnapshot,
                TimeSpan.FromSeconds(30)),
            new ProfilingEnvironmentSnapshot(
                "QA",
                false,
                MultiTargetSqlDataProfiler.EnvironmentLabelOrigin.Provided,
                false,
                secondarySnapshot,
                TimeSpan.FromSeconds(15))
        });

        var missingTableFinding = Assert.Single(
            report.Findings.Where(f => f.Code == "profiling.validation.schema.tableMissing"));

        Assert.Equal(MultiEnvironmentFindingSeverity.Warning, missingTableFinding.Severity);
        Assert.Contains("dbo.Orders", missingTableFinding.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("table exists", missingTableFinding.SuggestedAction, StringComparison.OrdinalIgnoreCase);
    }
}
