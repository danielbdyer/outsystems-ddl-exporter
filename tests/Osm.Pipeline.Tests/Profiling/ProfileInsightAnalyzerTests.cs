using System;
using System.Collections.Generic;
using System.Linq;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.Profiling.Insights;
using Osm.Domain.ValueObjects;
using Osm.Pipeline.Profiling;
using Xunit;

namespace Osm.Pipeline.Tests.Profiling;

public class ProfileInsightAnalyzerTests
{
    private static readonly ModuleName Module = ModuleName.Create("AppCore").Value;
    private static readonly EntityName Entity = EntityName.Create("Customer").Value;
    private static readonly TableName Table = TableName.Create("OSUSR_123_CUSTOMER").Value;
    private static readonly SchemaName Schema = SchemaName.Create("dbo").Value;

    [Fact]
    public void Analyze_WhenMandatoryAttributeContainsNulls_ProducesRiskInsight()
    {
        var attributes = new[]
        {
            CreateAttribute("Id", "ID", isMandatory: true, isIdentifier: true),
            CreateAttribute("Name", "NAME", isMandatory: true)
        };
        var model = CreateModel(attributes);
        var snapshot = CreateSnapshot(new[]
        {
            CreateColumnProfile("NAME", rowCount: 100, nullCount: 5)
        });

        var analyzer = new ProfileInsightAnalyzer();
        var report = analyzer.Analyze(model, snapshot);

        var insight = Assert.Single(report.Insights);
        Assert.Equal("profiling.nulls.mandatoryMismatch", insight.Code);
        Assert.Equal(ProfileInsightSeverity.Risk, insight.Severity);
        Assert.Equal("NAME", insight.Anchor.Column?.Value);
    }

    [Fact]
    public void Analyze_WhenOptionalAttributeHasHighNullRatio_ProducesWarning()
    {
        var attributes = new[]
        {
            CreateAttribute("Id", "ID", isMandatory: true, isIdentifier: true),
            CreateAttribute("Notes", "NOTES", isMandatory: false)
        };
        var model = CreateModel(attributes);
        var snapshot = CreateSnapshot(new[]
        {
            CreateColumnProfile("NOTES", rowCount: 100, nullCount: 70)
        });

        var analyzer = new ProfileInsightAnalyzer();
        var report = analyzer.Analyze(model, snapshot);

        var insight = Assert.Single(report.Insights);
        Assert.Equal("profiling.nulls.optional.high", insight.Code);
        Assert.Equal(ProfileInsightSeverity.Warning, insight.Severity);
    }

    [Fact]
    public void Analyze_WhenOptionalAttributeHasLowNullRatio_SuggestsTightening()
    {
        var attributes = new[]
        {
            CreateAttribute("Id", "ID", isMandatory: true, isIdentifier: true),
            CreateAttribute("Email", "EMAIL", isMandatory: false)
        };
        var model = CreateModel(attributes);
        var snapshot = CreateSnapshot(new[]
        {
            CreateColumnProfile("EMAIL", rowCount: 200, nullCount: 1)
        });

        var analyzer = new ProfileInsightAnalyzer();
        var report = analyzer.Analyze(model, snapshot);

        var insight = Assert.Single(report.Insights);
        Assert.Equal("profiling.nulls.optional.tighten", insight.Code);
        Assert.Equal(ProfileInsightSeverity.Recommendation, insight.Severity);
    }

    [Fact]
    public void Analyze_WhenUniqueCandidatesContainDuplicates_ReportsEachCandidate()
    {
        var attributes = new[]
        {
            CreateAttribute("Id", "ID", isMandatory: true, isIdentifier: true),
            CreateAttribute("Email", "EMAIL", isMandatory: false),
            CreateAttribute("NationalId", "NATIONAL_ID", isMandatory: false)
        };
        var model = CreateModel(attributes);

        var uniqueCandidates = new[]
        {
            UniqueCandidateProfile.Create(Schema, Table, Column("EMAIL"), hasDuplicate: true).Value
        };
        var compositeCandidates = new[]
        {
            CompositeUniqueCandidateProfile.Create(
                Schema,
                Table,
                new[] { Column("NATIONAL_ID"), Column("EMAIL") },
                hasDuplicate: true).Value
        };
        var snapshot = CreateSnapshot(
            new[]
            {
                CreateColumnProfile("EMAIL", rowCount: 100, nullCount: 0),
                CreateColumnProfile("NATIONAL_ID", rowCount: 100, nullCount: 0)
            },
            uniqueCandidates,
            compositeCandidates);

        var analyzer = new ProfileInsightAnalyzer();
        var report = analyzer.Analyze(model, snapshot);

        Assert.Equal(2, report.Insights.Length);
        Assert.Contains(report.Insights, insight => insight.Code == "profiling.unique.duplicate.single");
        Assert.Contains(report.Insights, insight => insight.Code == "profiling.unique.duplicate.composite");
    }

    [Fact]
    public void Analyze_WhenForeignKeysAreUntrustedOrOrphaned_ProducesInsights()
    {
        var attributes = new[]
        {
            CreateAttribute("Id", "ID", isMandatory: true, isIdentifier: true),
            CreateAttribute("CityId", "CITY_ID", isMandatory: false)
        };
        var model = CreateModel(attributes);
        var foreignKey = ForeignKeyReality.Create(
            ForeignKeyReference.Create(
                Schema,
                Table,
                Column("CITY_ID"),
                Schema,
                TableName.Create("OSUSR_123_CITY").Value,
                ColumnName.Create("ID").Value,
                hasDatabaseConstraint: false).Value,
            hasOrphan: true,
            isNoCheck: true).Value;

        var snapshot = CreateSnapshot(
            new[] { CreateColumnProfile("CITY_ID", rowCount: 150, nullCount: 10) },
            foreignKeys: new[] { foreignKey });

        var analyzer = new ProfileInsightAnalyzer();
        var report = analyzer.Analyze(model, snapshot);

        Assert.Contains(report.Insights, insight => insight.Code == "profiling.foreignKey.missingConstraint");
        Assert.Contains(report.Insights, insight => insight.Code == "profiling.foreignKey.noCheck");
        Assert.Contains(report.Insights, insight => insight.Code == "profiling.foreignKey.orphans");
    }

    private static AttributeModel CreateAttribute(string logical, string column, bool isMandatory, bool isIdentifier = false)
    {
        var result = AttributeModel.Create(
            AttributeName.Create(logical).Value,
            Column(column),
            dataType: "Text",
            isMandatory: isMandatory,
            isIdentifier: isIdentifier,
            isAutoNumber: isIdentifier,
            isActive: true);
        return result.Value;
    }

    private static ColumnName Column(string name) => ColumnName.Create(name).Value;

    private static OsmModel CreateModel(IEnumerable<AttributeModel> attributes)
    {
        var entity = EntityModel.Create(
            Module,
            Entity,
            Table,
            Schema,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            attributes: attributes,
            allowMissingPrimaryKey: false);

        if (entity.IsFailure)
        {
            throw new InvalidOperationException(string.Join(",", entity.Errors.Select(error => error.Code)));
        }

        var module = ModuleModel.Create(
            Module,
            isSystemModule: false,
            isActive: true,
            entities: new[] { entity.Value });

        if (module.IsFailure)
        {
            throw new InvalidOperationException(string.Join(",", module.Errors.Select(error => error.Code)));
        }

        var model = OsmModel.Create(DateTime.UtcNow, new[] { module.Value });
        if (model.IsFailure)
        {
            throw new InvalidOperationException(string.Join(",", model.Errors.Select(error => error.Code)));
        }

        return model.Value;
    }

    private static ProfileSnapshot CreateSnapshot(
        IEnumerable<ColumnProfile> columns,
        IEnumerable<UniqueCandidateProfile>? uniqueCandidates = null,
        IEnumerable<CompositeUniqueCandidateProfile>? compositeCandidates = null,
        IEnumerable<ForeignKeyReality>? foreignKeys = null)
    {
        var result = ProfileSnapshot.Create(
            columns,
            uniqueCandidates ?? Enumerable.Empty<UniqueCandidateProfile>(),
            compositeCandidates ?? Enumerable.Empty<CompositeUniqueCandidateProfile>(),
            foreignKeys ?? Enumerable.Empty<ForeignKeyReality>());

        if (result.IsFailure)
        {
            throw new InvalidOperationException(string.Join(",", result.Errors.Select(error => error.Code)));
        }

        return result.Value;
    }

    private static ColumnProfile CreateColumnProfile(string column, long rowCount, long nullCount)
    {
        var result = ColumnProfile.Create(
            Schema,
            Table,
            Column(column),
            isNullablePhysical: true,
            isComputed: false,
            isPrimaryKey: string.Equals(column, "ID", StringComparison.OrdinalIgnoreCase),
            isUniqueKey: false,
            defaultDefinition: null,
            rowCount,
            nullCount);

        if (result.IsFailure)
        {
            throw new InvalidOperationException(string.Join(",", result.Errors.Select(error => error.Code)));
        }

        return result.Value;
    }
}
