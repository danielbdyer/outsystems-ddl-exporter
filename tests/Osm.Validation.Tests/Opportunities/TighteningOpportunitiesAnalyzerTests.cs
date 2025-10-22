using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Validation.Tightening;
using Osm.Validation.Tightening.Opportunities;
using OpportunitiesChangeRisk = Osm.Validation.Tightening.Opportunities.ChangeRisk;
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

        var decision = NullabilityDecision.Create(coordinate, true, false, ImmutableArray<string>.Empty);
        var decisions = PolicyDecisionSet.Create(
            ImmutableDictionary<ColumnCoordinate, NullabilityDecision>.Empty.Add(coordinate, decision),
            ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision>.Empty,
            ImmutableDictionary<IndexCoordinate, UniqueIndexDecision>.Empty,
            ImmutableArray<TighteningDiagnostic>.Empty,
            ImmutableDictionary<ColumnCoordinate, string>.Empty.Add(coordinate, entity.Module.Value),
            ImmutableDictionary<IndexCoordinate, string>.Empty,
            TighteningOptions.Default);

        var analyzer = new TighteningOpportunitiesAnalyzer();
        var report = analyzer.Analyze(model, snapshot, decisions);

        Assert.Equal(1, report.TotalCount);
        var opportunity = Assert.Single(report.Opportunities);
        Assert.Equal(ConstraintType.NotNull, opportunity.Constraint);
        Assert.Equal(OpportunitiesChangeRisk.SafeToApply, opportunity.Risk);
        Assert.Contains("ALTER TABLE", opportunity.Statements[0]);
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

        var indexCoordinate = new IndexCoordinate(new SchemaName("dbo"), new TableName("OSUSR_PRO_PRODUCT"), new IndexName("IX_PRODUCT_CODE"));
        var decision = UniqueIndexDecision.Create(indexCoordinate, true, true, ImmutableArray<string>.Empty);
        var decisions = PolicyDecisionSet.Create(
            ImmutableDictionary<ColumnCoordinate, NullabilityDecision>.Empty,
            ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision>.Empty,
            ImmutableDictionary<IndexCoordinate, UniqueIndexDecision>.Empty.Add(indexCoordinate, decision),
            ImmutableArray<TighteningDiagnostic>.Empty,
            ImmutableDictionary<ColumnCoordinate, string>.Empty,
            ImmutableDictionary<IndexCoordinate, string>.Empty.Add(indexCoordinate, entity.Module.Value),
            TighteningOptions.Default);

        var analyzer = new TighteningOpportunitiesAnalyzer();
        var report = analyzer.Analyze(model, snapshot, decisions);

        Assert.Equal(1, report.TotalCount);
        var opportunity = Assert.Single(report.Opportunities);
        Assert.Equal(ConstraintType.Unique, opportunity.Constraint);
        Assert.Equal(OpportunitiesChangeRisk.NeedsRemediation, opportunity.Risk);
        Assert.Contains("CREATE UNIQUE", opportunity.Statements[0]);
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

        var decision = ForeignKeyDecision.Create(coordinate, true, ImmutableArray<string>.Empty);
        var decisions = PolicyDecisionSet.Create(
            ImmutableDictionary<ColumnCoordinate, NullabilityDecision>.Empty,
            ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision>.Empty.Add(coordinate, decision),
            ImmutableDictionary<IndexCoordinate, UniqueIndexDecision>.Empty,
            ImmutableArray<TighteningDiagnostic>.Empty,
            ImmutableDictionary<ColumnCoordinate, string>.Empty.Add(coordinate, sourceEntity.Module.Value),
            ImmutableDictionary<IndexCoordinate, string>.Empty,
            TighteningOptions.Default);

        var analyzer = new TighteningOpportunitiesAnalyzer();
        var report = analyzer.Analyze(model, snapshot, decisions);

        Assert.Equal(1, report.TotalCount);
        var opportunity = Assert.Single(report.Opportunities);
        Assert.Equal(ConstraintType.ForeignKey, opportunity.Constraint);
        Assert.Equal(OpportunitiesChangeRisk.SafeToApply, opportunity.Risk);
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
            ImmutableDictionary<ColumnCoordinate, string>.Empty,
            ImmutableDictionary<IndexCoordinate, string>.Empty,
            TighteningOptions.Default);

        var analyzer = new TighteningOpportunitiesAnalyzer();

        var report = analyzer.Analyze(model, snapshot, decisions);

        Assert.Equal(0, report.TotalCount);
    }
}
