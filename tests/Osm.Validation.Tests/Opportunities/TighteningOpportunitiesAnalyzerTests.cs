using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Validation.Tightening;
using Osm.Validation.Tightening.Opportunities;
using Osm.Validation.Tests.Policy;
using Xunit;

namespace Osm.Validation.Tests.Opportunities;

public sealed class TighteningOpportunitiesAnalyzerTests
{
    [Fact]
    public void Analyze_creates_not_null_opportunity_with_safe_risk()
    {
        var attribute = TighteningEvaluatorTestHelper.CreateAttribute("Id", "ID", isMandatory: true);
        var entity = TighteningEvaluatorTestHelper.CreateEntity("Module", "Customer", "OSUSR_CUS_CUSTOMER", new[] { attribute });
        var model = TighteningEvaluatorTestHelper.CreateModel(TighteningEvaluatorTestHelper.CreateModule("Module", entity));

        var coordinate = new ColumnCoordinate(new SchemaName("dbo"), new TableName("OSUSR_CUS_CUSTOMER"), new ColumnName("ID"));
        var profile = TighteningEvaluatorTestHelper.CreateColumnProfile(coordinate, isNullablePhysical: false, rowCount: 100, nullCount: 0);
        var snapshot = ProfileSnapshot.Create(new[] { profile }, Array.Empty<UniqueCandidateProfile>(), Array.Empty<CompositeUniqueCandidateProfile>(), Array.Empty<ForeignKeyReality>()).Value;
        var identity = ColumnIdentity.From(entity, attribute);

        var decision = NullabilityDecision.Create(coordinate, true, false, ImmutableArray<string>.Empty);
        var decisions = PolicyDecisionSet.Create(
            ImmutableDictionary<ColumnCoordinate, NullabilityDecision>.Empty.Add(coordinate, decision),
            ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision>.Empty,
            ImmutableDictionary<IndexCoordinate, UniqueIndexDecision>.Empty,
            ImmutableArray<TighteningDiagnostic>.Empty,
            ImmutableDictionary<ColumnCoordinate, ColumnIdentity>.Empty.Add(coordinate, identity),
            ImmutableDictionary<IndexCoordinate, string>.Empty,
            TighteningOptions.Default);

        var analyzer = new TighteningOpportunitiesAnalyzer();
        var findings = analyzer.Analyze(model, snapshot, decisions);
        var report = findings.Opportunities;

        Assert.Equal(0, report.TotalCount);
        Assert.Empty(report.Opportunities);
    }

    [Fact]
    public void Analyze_marks_unique_with_remediation_when_required()
    {
        var attribute = TighteningEvaluatorTestHelper.CreateAttribute("Code", "CODE");
        var entity = TighteningEvaluatorTestHelper.CreateEntity(
            "Module",
            "Product",
            "OSUSR_PRO_PRODUCT",
            new[] { attribute },
            indexes: new[]
            {
                IndexModel.Create(
                    new IndexName("IX_PRODUCT_CODE"),
                    isUnique: true,
                    isPrimary: false,
                    isPlatformAuto: false,
                    new[] { IndexColumnModel.Create(new AttributeName("Code"), new ColumnName("CODE"), 1, false, IndexColumnDirection.Ascending).Value }).Value
            });
        var model = TighteningEvaluatorTestHelper.CreateModel(TighteningEvaluatorTestHelper.CreateModule("Module", entity));

        var coordinate = new ColumnCoordinate(new SchemaName("dbo"), new TableName("OSUSR_PRO_PRODUCT"), new ColumnName("CODE"));
        var uniqueProfile = TighteningEvaluatorTestHelper.CreateUniqueCandidate(coordinate, hasDuplicate: false);
        var snapshot = ProfileSnapshot.Create(
            Array.Empty<ColumnProfile>(),
            new[] { uniqueProfile },
            Array.Empty<CompositeUniqueCandidateProfile>(),
            Array.Empty<ForeignKeyReality>()).Value;
        var identity = ColumnIdentity.From(entity, attribute);

        var indexCoordinate = new IndexCoordinate(new SchemaName("dbo"), new TableName("OSUSR_PRO_PRODUCT"), new IndexName("IX_PRODUCT_CODE"));
        var decision = UniqueIndexDecision.Create(indexCoordinate, true, true, ImmutableArray<string>.Empty);
        var decisions = PolicyDecisionSet.Create(
            ImmutableDictionary<ColumnCoordinate, NullabilityDecision>.Empty,
            ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision>.Empty,
            ImmutableDictionary<IndexCoordinate, UniqueIndexDecision>.Empty.Add(indexCoordinate, decision),
            ImmutableArray<TighteningDiagnostic>.Empty,
            ImmutableDictionary<ColumnCoordinate, ColumnIdentity>.Empty.Add(coordinate, identity),
            ImmutableDictionary<IndexCoordinate, string>.Empty.Add(indexCoordinate, entity.Module.Value),
            TighteningOptions.Default);

        var analyzer = new TighteningOpportunitiesAnalyzer();
        var findings = analyzer.Analyze(model, snapshot, decisions);
        var report = findings.Opportunities;

        Assert.Equal(1, report.TotalCount);
        var opportunity = Assert.Single(report.Opportunities);
        Assert.Equal(OpportunityType.UniqueIndex, opportunity.Type);
        Assert.Equal(OpportunityDisposition.NeedsRemediation, opportunity.Disposition);
        Assert.Equal(RiskLevel.Moderate, opportunity.Risk.Level);
        Assert.Contains("CREATE UNIQUE", opportunity.Statements[0]);
        Assert.True(opportunity.EvidenceSummary?.RequiresRemediation);
        Assert.Equal(1, report.DispositionCounts[OpportunityDisposition.NeedsRemediation]);
        Assert.Equal(1, report.TypeCounts[OpportunityType.UniqueIndex]);
        Assert.Equal(1, report.RiskCounts[RiskLevel.Moderate]);
    }

    [Fact]
    public void Analyze_collates_composite_unique_evidence()
    {
        var codeAttribute = TighteningEvaluatorTestHelper.CreateAttribute("Code", "CODE");
        var tenantAttribute = TighteningEvaluatorTestHelper.CreateAttribute("TenantId", "TENANTID");
        var entity = TighteningEvaluatorTestHelper.CreateEntity(
            "Module",
            "CustomerTenant",
            "OSUSR_PRO_CUSTOMER_TENANT",
            new[] { codeAttribute, tenantAttribute },
            indexes: new[]
            {
                IndexModel.Create(
                        new IndexName("IX_CUSTOMER_TENANT"),
                        isUnique: true,
                        isPrimary: false,
                        isPlatformAuto: false,
                        new[]
                        {
                            IndexColumnModel.Create(new AttributeName("Code"), new ColumnName("CODE"), 1, false, IndexColumnDirection.Ascending).Value,
                            IndexColumnModel.Create(new AttributeName("TenantId"), new ColumnName("TENANTID"), 2, false, IndexColumnDirection.Ascending).Value
                        })
                    .Value
            });
        var model = TighteningEvaluatorTestHelper.CreateModel(TighteningEvaluatorTestHelper.CreateModule("Module", entity));

        var schema = new SchemaName("dbo");
        var table = new TableName("OSUSR_PRO_CUSTOMER_TENANT");
        var codeCoordinate = new ColumnCoordinate(schema, table, new ColumnName("CODE"));
        var tenantCoordinate = new ColumnCoordinate(schema, table, new ColumnName("TENANTID"));
        var codeProfile = TighteningEvaluatorTestHelper.CreateColumnProfile(codeCoordinate, isNullablePhysical: false, rowCount: 100, nullCount: 0);
        var tenantProfile = TighteningEvaluatorTestHelper.CreateColumnProfile(tenantCoordinate, isNullablePhysical: false, rowCount: 100, nullCount: 0);
        var compositeProfile = CompositeUniqueCandidateProfile.Create(
            schema,
            table,
            new[] { new ColumnName("CODE"), new ColumnName("TENANTID") },
            hasDuplicate: true).Value;

        var snapshot = ProfileSnapshot.Create(
            new[] { codeProfile, tenantProfile },
            Array.Empty<UniqueCandidateProfile>(),
            new[] { compositeProfile },
            Array.Empty<ForeignKeyReality>()).Value;

        var indexCoordinate = new IndexCoordinate(schema, table, new IndexName("IX_CUSTOMER_TENANT"));
        var decision = UniqueIndexDecision.Create(indexCoordinate, enforceUnique: true, requiresRemediation: true, ImmutableArray<string>.Empty);
        var decisions = PolicyDecisionSet.Create(
            ImmutableDictionary<ColumnCoordinate, NullabilityDecision>.Empty,
            ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision>.Empty,
            ImmutableDictionary<IndexCoordinate, UniqueIndexDecision>.Empty.Add(indexCoordinate, decision),
            ImmutableArray<TighteningDiagnostic>.Empty,
            ImmutableDictionary<ColumnCoordinate, ColumnIdentity>.Empty,
            ImmutableDictionary<IndexCoordinate, string>.Empty.Add(indexCoordinate, entity.Module.Value),
            TighteningOptions.Default);

        var analyzer = new TighteningOpportunitiesAnalyzer();
        var findings = analyzer.Analyze(model, snapshot, decisions);
        var report = findings.Opportunities;

        var opportunity = Assert.Single(report.Opportunities);
        Assert.Equal(OpportunityType.UniqueIndex, opportunity.Type);
        Assert.Contains("Composite duplicates=True", opportunity.Evidence);
        Assert.False(opportunity.EvidenceSummary?.DataClean);
        Assert.True(opportunity.EvidenceSummary?.HasDuplicates);
        Assert.Equal(2, opportunity.Columns.Length);
    }

    [Fact]
    public void Analyze_generates_foreign_key_statement()
    {
        var targetAttribute = TighteningEvaluatorTestHelper.CreateAttribute("Id", "ID", isIdentifier: true);
        var targetEntity = TighteningEvaluatorTestHelper.CreateEntity("Module", "Customer", "OSUSR_CUS_CUSTOMER", new[] { targetAttribute });
        var sourceAttribute = TighteningEvaluatorTestHelper.CreateAttribute(
            "CustomerId",
            "CUSTOMERID",
            isReference: true,
            hasDatabaseConstraint: false,
            targetEntity: targetEntity.LogicalName,
            targetPhysical: targetEntity.PhysicalName);
        var sourceEntity = TighteningEvaluatorTestHelper.CreateEntity("Module", "Order", "OSUSR_ORD_ORDER", new[] { sourceAttribute });
        var model = TighteningEvaluatorTestHelper.CreateModel(TighteningEvaluatorTestHelper.CreateModule("Module", targetEntity, sourceEntity));

        var coordinate = new ColumnCoordinate(new SchemaName("dbo"), new TableName("OSUSR_ORD_ORDER"), new ColumnName("CUSTOMERID"));
        var profile = TighteningEvaluatorTestHelper.CreateColumnProfile(coordinate, isNullablePhysical: true, rowCount: 10, nullCount: 0);
        var targetCoordinate = new ColumnCoordinate(new SchemaName("dbo"), new TableName("OSUSR_CUS_CUSTOMER"), new ColumnName("ID"));
        var fkReality = TighteningEvaluatorTestHelper.CreateForeignKeyReality(coordinate, targetCoordinate, hasConstraint: false, hasOrphan: false);
        var snapshot = ProfileSnapshot.Create(new[] { profile }, Array.Empty<UniqueCandidateProfile>(), Array.Empty<CompositeUniqueCandidateProfile>(), new[] { fkReality }).Value;
        var identity = ColumnIdentity.From(sourceEntity, sourceAttribute);

        var decision = ForeignKeyDecision.Create(coordinate, true, ImmutableArray<string>.Empty);
        var decisions = PolicyDecisionSet.Create(
            ImmutableDictionary<ColumnCoordinate, NullabilityDecision>.Empty,
            ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision>.Empty.Add(coordinate, decision),
            ImmutableDictionary<IndexCoordinate, UniqueIndexDecision>.Empty,
            ImmutableArray<TighteningDiagnostic>.Empty,
            ImmutableDictionary<ColumnCoordinate, ColumnIdentity>.Empty.Add(coordinate, identity),
            ImmutableDictionary<IndexCoordinate, string>.Empty,
            TighteningOptions.Default);

        var analyzer = new TighteningOpportunitiesAnalyzer();
        var findings = analyzer.Analyze(model, snapshot, decisions);
        var report = findings.Opportunities;

        Assert.Equal(1, report.TotalCount);
        var opportunity = Assert.Single(report.Opportunities);
        Assert.Equal(OpportunityType.ForeignKey, opportunity.Type);
        Assert.Equal(OpportunityDisposition.ReadyToApply, opportunity.Disposition);
        Assert.Equal(RiskLevel.Low, opportunity.Risk.Level);
        Assert.Contains("FOREIGN KEY", opportunity.Statements[0]);
    }

    [Fact]
    public void Analyze_ignores_duplicate_logical_entity_names_without_throwing()
    {
        var attributeA = TighteningEvaluatorTestHelper.CreateAttribute("Id", "ID");
        var attributeB = TighteningEvaluatorTestHelper.CreateAttribute("Id", "ID");
        var entityA = TighteningEvaluatorTestHelper.CreateEntity("Sales", "EntityType", "OSUSR_SAL_ENTITY", new[] { attributeA });
        var entityB = TighteningEvaluatorTestHelper.CreateEntity("Support", "EntityType", "OSUSR_SUP_ENTITY", new[] { attributeB });
        var moduleA = TighteningEvaluatorTestHelper.CreateModule("Sales", entityA);
        var moduleB = TighteningEvaluatorTestHelper.CreateModule("Support", entityB);
        var model = TighteningEvaluatorTestHelper.CreateModel(moduleA, moduleB);

        var snapshot = ProfileSnapshot.Create(
            Array.Empty<ColumnProfile>(),
            Array.Empty<UniqueCandidateProfile>(),
            Array.Empty<CompositeUniqueCandidateProfile>(),
            Array.Empty<ForeignKeyReality>()).Value;

        var decisions = PolicyDecisionSet.Create(
            ImmutableDictionary<ColumnCoordinate, NullabilityDecision>.Empty,
            ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision>.Empty,
            ImmutableDictionary<IndexCoordinate, UniqueIndexDecision>.Empty,
            ImmutableArray<TighteningDiagnostic>.Empty,
            ImmutableDictionary<ColumnCoordinate, ColumnIdentity>.Empty,
            ImmutableDictionary<IndexCoordinate, string>.Empty,
            TighteningOptions.Default);

        var analyzer = new TighteningOpportunitiesAnalyzer();

        var findings = analyzer.Analyze(model, snapshot, decisions);
        var report = findings.Opportunities;

        Assert.Equal(0, report.TotalCount);
    }
}
