using System;
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
                ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UtcNow, 128))
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
    public void Generate_emits_duplicate_warning_for_unique_candidate_with_duplicates()
    {
        var candidate = UniqueCandidateProfile
            .Create(DefaultSchema, DefaultTable, DefaultColumn, hasDuplicate: true, ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UtcNow, 128))
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
            .Create(reference, hasOrphan: true, isNoCheck: false, ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UtcNow, 128))
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
}
