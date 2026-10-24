using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Validation.Profiling;
using Xunit;

namespace Osm.Validation.Tests.Profiling;

public sealed class ProfilingInsightGeneratorTests
{
    private static readonly SchemaName DefaultSchema = SchemaName.Create("dbo").Value;
    private static readonly TableName DefaultTable = TableName.Create("Sample").Value;
    private static readonly ColumnName DefaultColumn = ColumnName.Create("CustomerId").Value;

    [Fact]
    public void Generate_emits_null_tightening_recommendation_for_nullable_column_without_nulls()
    {
        var column = ColumnProfile
            .Create(
                DefaultSchema,
                DefaultTable,
                DefaultColumn,
                isNullablePhysical: true,
                isComputed: false,
                isPrimaryKey: false,
                isUniqueKey: false,
                defaultDefinition: null,
                rowCount: 128,
                nullCount: 0,
                ProfilingProbeStatus.Unknown)
            .Value;

        var snapshot = ProfileSnapshot
            .Create(new[] { column }, Enumerable.Empty<UniqueCandidateProfile>(), Enumerable.Empty<CompositeUniqueCandidateProfile>(), Enumerable.Empty<ForeignKeyReality>())
            .Value;

        var generator = new ProfilingInsightGenerator();

        var insight = Assert.Single(generator.Generate(snapshot), i => i.Category == ProfilingInsightCategory.Nullability);
        Assert.Equal(ProfilingInsightSeverity.Recommendation, insight.Severity);
        Assert.Contains("NOT NULL", insight.Message);
        Assert.Equal(DefaultColumn.Value, insight.Coordinate!.Column!.Value.Value);
    }

    [Fact]
    public void Generate_rolls_up_nullability_recommendations_by_table()
    {
        var firstColumn = ColumnProfile
            .Create(
                DefaultSchema,
                DefaultTable,
                ColumnName.Create("FirstName").Value,
                isNullablePhysical: true,
                isComputed: false,
                isPrimaryKey: false,
                isUniqueKey: false,
                defaultDefinition: null,
                rowCount: 256,
                nullCount: 0,
                ProfilingProbeStatus.Unknown)
            .Value;

        var secondColumn = ColumnProfile
            .Create(
                DefaultSchema,
                DefaultTable,
                ColumnName.Create("LastName").Value,
                isNullablePhysical: true,
                isComputed: false,
                isPrimaryKey: false,
                isUniqueKey: false,
                defaultDefinition: null,
                rowCount: 256,
                nullCount: 0,
                ProfilingProbeStatus.Unknown)
            .Value;

        var snapshot = ProfileSnapshot
            .Create(new[] { firstColumn, secondColumn }, Enumerable.Empty<UniqueCandidateProfile>(), Enumerable.Empty<CompositeUniqueCandidateProfile>(), Enumerable.Empty<ForeignKeyReality>())
            .Value;

        var generator = new ProfilingInsightGenerator();

        var insight = Assert.Single(generator.Generate(snapshot), i => i.Category == ProfilingInsightCategory.Nullability);
        Assert.Equal(ProfilingInsightSeverity.Recommendation, insight.Severity);
        Assert.Null(insight.Coordinate!.Column);
        Assert.Contains("2 columns", insight.Message);
        Assert.Contains(firstColumn.Column.Value, insight.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(secondColumn.Column.Value, insight.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_emits_duplicate_warning_for_unique_candidate_with_duplicates()
    {
        var candidate = UniqueCandidateProfile
            .Create(DefaultSchema, DefaultTable, DefaultColumn, hasDuplicate: true, ProfilingProbeStatus.Unknown)
            .Value;

        var snapshot = ProfileSnapshot
            .Create(Enumerable.Empty<ColumnProfile>(), new[] { candidate }, Enumerable.Empty<CompositeUniqueCandidateProfile>(), Enumerable.Empty<ForeignKeyReality>())
            .Value;

        var generator = new ProfilingInsightGenerator();

        var insight = Assert.Single(generator.Generate(snapshot), i => i.Category == ProfilingInsightCategory.Uniqueness);
        Assert.Equal(ProfilingInsightSeverity.Warning, insight.Severity);
        Assert.Contains("duplicates", insight.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_emits_orphan_warning_for_foreign_key()
    {
        var reference = ForeignKeyReference
            .Create(
                DefaultSchema,
                DefaultTable,
                DefaultColumn,
                SchemaName.Create("dbo").Value,
                TableName.Create("Parent").Value,
                ColumnName.Create("Id").Value,
                hasDatabaseConstraint: false)
            .Value;

        var reality = ForeignKeyReality
            .Create(reference, hasOrphan: true, orphanCount: 0, isNoCheck: false, ProfilingProbeStatus.Unknown)
            .Value;

        var snapshot = ProfileSnapshot
            .Create(Enumerable.Empty<ColumnProfile>(), Enumerable.Empty<UniqueCandidateProfile>(), Enumerable.Empty<CompositeUniqueCandidateProfile>(), new[] { reality })
            .Value;

        var generator = new ProfilingInsightGenerator();

        var insight = Assert.Single(generator.Generate(snapshot), i => i.Category == ProfilingInsightCategory.ForeignKey);
        Assert.Equal(ProfilingInsightSeverity.Warning, insight.Severity);
        Assert.Contains("Orphaned", insight.Message, System.StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(insight.Coordinate);
        Assert.Equal(DefaultTable.Value, insight.Coordinate!.Table.Value);
    }

    [Fact]
    public void Generate_includes_orphan_sample_details_in_message()
    {
        var reference = ForeignKeyReference
            .Create(
                DefaultSchema,
                DefaultTable,
                DefaultColumn,
                SchemaName.Create("dbo").Value,
                TableName.Create("Parent").Value,
                ColumnName.Create("Id").Value,
                hasDatabaseConstraint: false)
            .Value;

        var sample = ForeignKeyOrphanSample.Create(
            ImmutableArray.Create("Id"),
            "CustomerId",
            ImmutableArray.Create(
                new ForeignKeyOrphanIdentifier(ImmutableArray.Create<object?>(101), "Missing"),
                new ForeignKeyOrphanIdentifier(ImmutableArray.Create<object?>(202), "Legacy")),
            totalOrphans: 5);

        var reality = ForeignKeyReality
            .Create(
                reference,
                hasOrphan: true,
                orphanCount: 5,
                isNoCheck: false,
                ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UnixEpoch, 50),
                sample)
            .Value;

        var snapshot = ProfileSnapshot
            .Create(
                Enumerable.Empty<ColumnProfile>(),
                Enumerable.Empty<UniqueCandidateProfile>(),
                Enumerable.Empty<CompositeUniqueCandidateProfile>(),
                new[] { reality })
            .Value;

        var generator = new ProfilingInsightGenerator();

        var insight = Assert.Single(generator.Generate(snapshot), i => i.Category == ProfilingInsightCategory.ForeignKey);
        Assert.Equal(ProfilingInsightSeverity.Warning, insight.Severity);
        Assert.Contains("showing 2 of 5 orphan rows", insight.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("(101)", insight.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("(202)", insight.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(ProfilingProbeOutcome.FallbackTimeout, "timed out")]
    [InlineData(ProfilingProbeOutcome.Cancelled, "cancelled")]
    public void Generate_emits_evidence_warning_for_null_count_probe_outcome(
        ProfilingProbeOutcome outcome,
        string expectedPhrase)
    {
        var status = CreateProbeStatus(outcome, sampleSize: 1_024);
        var column = ColumnProfile
            .Create(
                DefaultSchema,
                DefaultTable,
                DefaultColumn,
                isNullablePhysical: false,
                isComputed: false,
                isPrimaryKey: false,
                isUniqueKey: false,
                defaultDefinition: null,
                rowCount: 128,
                nullCount: 5,
                status)
            .Value;

        var snapshot = ProfileSnapshot
            .Create(new[] { column }, Enumerable.Empty<UniqueCandidateProfile>(), Enumerable.Empty<CompositeUniqueCandidateProfile>(), Enumerable.Empty<ForeignKeyReality>())
            .Value;

        var generator = new ProfilingInsightGenerator();

        var insight = Assert.Single(generator.Generate(snapshot), i => i.Category == ProfilingInsightCategory.Evidence);
        Assert.Equal(ProfilingInsightSeverity.Warning, insight.Severity);
        Assert.Contains(expectedPhrase, insight.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(status.SampleSize.ToString("N0", CultureInfo.InvariantCulture), insight.Message);
        Assert.Contains(status.CapturedAtUtc.ToString("O", CultureInfo.InvariantCulture), insight.Message);
        Assert.NotNull(insight.Coordinate);
        Assert.Equal(DefaultColumn.Value, insight.Coordinate!.Column!.Value.Value);
    }

    [Theory]
    [InlineData(ProfilingProbeOutcome.FallbackTimeout)]
    [InlineData(ProfilingProbeOutcome.Cancelled)]
    public void Generate_emits_evidence_warning_for_unique_candidate_probe_outcome(ProfilingProbeOutcome outcome)
    {
        var status = CreateProbeStatus(outcome, sampleSize: 2_048);
        var candidate = UniqueCandidateProfile
            .Create(DefaultSchema, DefaultTable, DefaultColumn, hasDuplicate: false, status)
            .Value;

        var snapshot = ProfileSnapshot
            .Create(Enumerable.Empty<ColumnProfile>(), new[] { candidate }, Enumerable.Empty<CompositeUniqueCandidateProfile>(), Enumerable.Empty<ForeignKeyReality>())
            .Value;

        var generator = new ProfilingInsightGenerator();

        var insight = Assert.Single(generator.Generate(snapshot), i => i.Category == ProfilingInsightCategory.Evidence);
        Assert.Equal(ProfilingInsightSeverity.Warning, insight.Severity);
        Assert.Contains("Uniqueness probe", insight.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(status.SampleSize.ToString("N0", CultureInfo.InvariantCulture), insight.Message);
        Assert.NotNull(insight.Coordinate);
        Assert.Equal(DefaultColumn.Value, insight.Coordinate!.Column!.Value.Value);
    }

    [Theory]
    [InlineData(ProfilingProbeOutcome.FallbackTimeout)]
    [InlineData(ProfilingProbeOutcome.Cancelled)]
    public void Generate_emits_evidence_warning_for_foreign_key_probe_outcome(ProfilingProbeOutcome outcome)
    {
        var status = CreateProbeStatus(outcome, sampleSize: 3_072);
        var reference = ForeignKeyReference
            .Create(
                DefaultSchema,
                DefaultTable,
                DefaultColumn,
                SchemaName.Create("dbo").Value,
                TableName.Create("Parent").Value,
                ColumnName.Create("Id").Value,
                hasDatabaseConstraint: false)
            .Value;

        var reality = ForeignKeyReality
            .Create(reference, hasOrphan: false, orphanCount: 0, isNoCheck: false, status)
            .Value;

        var snapshot = ProfileSnapshot
            .Create(Enumerable.Empty<ColumnProfile>(), Enumerable.Empty<UniqueCandidateProfile>(), Enumerable.Empty<CompositeUniqueCandidateProfile>(), new[] { reality })
            .Value;

        var generator = new ProfilingInsightGenerator();

        var insight = Assert.Single(generator.Generate(snapshot), i => i.Category == ProfilingInsightCategory.Evidence);
        Assert.Equal(ProfilingInsightSeverity.Warning, insight.Severity);
        Assert.Contains("Foreign key probe", insight.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(status.SampleSize.ToString("N0", CultureInfo.InvariantCulture), insight.Message);
        Assert.NotNull(insight.Coordinate);
        Assert.Equal(DefaultTable.Value, insight.Coordinate!.Table.Value);
        Assert.Equal("Parent", insight.Coordinate!.RelatedTable!.Value.Value);
    }

    private static ProfilingProbeStatus CreateProbeStatus(ProfilingProbeOutcome outcome, long sampleSize)
    {
        var capturedAt = new DateTimeOffset(2024, 5, 1, 12, 30, 45, TimeSpan.Zero);

        return outcome switch
        {
            ProfilingProbeOutcome.FallbackTimeout => ProfilingProbeStatus.CreateFallbackTimeout(capturedAt, sampleSize),
            ProfilingProbeOutcome.Cancelled => ProfilingProbeStatus.CreateCancelled(capturedAt, sampleSize),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null)
        };
    }
}
