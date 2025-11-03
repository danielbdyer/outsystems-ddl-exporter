using System.Collections.Generic;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Validation.Tightening;
using Xunit;

namespace Osm.Validation.Tests.Policy;

public sealed class ForeignKeyEvaluatorTests
{
    [Fact]
    public void Should_Block_CrossSchema_Constraint_When_Overrides_Disallow()
    {
        var options = TighteningPolicyTestHelper.CreateOptions(TighteningMode.Cautious);
        var coordinate = new ColumnCoordinate(new SchemaName("dbo"), new TableName("OSUSR_SRC"), new ColumnName("TARGET_ID"));
        var targetCoordinate = new ColumnCoordinate(new SchemaName("other"), new TableName("OSUSR_TARGET"), new ColumnName("ID"));

        var targetEntity = TighteningEvaluatorTestHelper.CreateEntity(
            "TargetModule",
            "TargetEntity",
            "OSUSR_TARGET",
            new[]
            {
                TighteningEvaluatorTestHelper.CreateAttribute("Id", "ID", isIdentifier: true)
            },
            schema: targetCoordinate.Schema);

        var referenceAttribute = TighteningEvaluatorTestHelper.CreateAttribute(
            "TargetId",
            "TARGET_ID",
            isReference: true,
            deleteRule: "Cascade",
            targetEntity: targetEntity.LogicalName,
            targetPhysical: targetEntity.PhysicalName);

        var sourceEntity = TighteningEvaluatorTestHelper.CreateEntity(
            "SourceModule",
            "SourceEntity",
            "OSUSR_SRC",
            new[]
            {
                TighteningEvaluatorTestHelper.CreateAttribute("Id", "ID", isIdentifier: true),
                referenceAttribute
            });

        var sourceModule = TighteningEvaluatorTestHelper.CreateModule("SourceModule", sourceEntity);
        var targetModule = TighteningEvaluatorTestHelper.CreateModule("TargetModule", targetEntity);
        var model = TighteningEvaluatorTestHelper.CreateModel(sourceModule, targetModule);

        var foreignKeyTargets = TighteningEvaluatorTestHelper.CreateForeignKeyTargetIndex(
            model,
            new Dictionary<EntityName, EntityModel>
            {
                [sourceEntity.LogicalName] = sourceEntity,
                [targetEntity.LogicalName] = targetEntity
            });

        var fkReality = TighteningEvaluatorTestHelper.CreateForeignKeyReality(coordinate, targetCoordinate, hasConstraint: false, hasOrphan: false);

        var evaluator = new ForeignKeyEvaluator(
            options.ForeignKeys,
            new Dictionary<ColumnCoordinate, ForeignKeyReality> { [coordinate] = fkReality },
            foreignKeyTargets);

        var decision = evaluator.Evaluate(sourceEntity, referenceAttribute, coordinate);

        Assert.False(decision.CreateConstraint);
        Assert.Contains(TighteningRationales.CrossSchema, decision.Rationales);
    }

    [Fact]
    public void Should_Create_Constraint_When_Eligible_And_Creation_Enabled()
    {
        var options = TighteningPolicyTestHelper.CreateOptions(TighteningMode.Cautious);
        var coordinate = new ColumnCoordinate(new SchemaName("dbo"), new TableName("OSUSR_SRC"), new ColumnName("TARGET_ID"));
        var targetCoordinate = new ColumnCoordinate(new SchemaName("dbo"), new TableName("OSUSR_TARGET"), new ColumnName("ID"));

        var targetEntity = TighteningEvaluatorTestHelper.CreateEntity(
            "TargetModule",
            "TargetEntity",
            "OSUSR_TARGET",
            new[]
            {
                TighteningEvaluatorTestHelper.CreateAttribute("Id", "ID", isIdentifier: true)
            });

        var referenceAttribute = TighteningEvaluatorTestHelper.CreateAttribute(
            "TargetId",
            "TARGET_ID",
            isReference: true,
            deleteRule: "Cascade",
            targetEntity: targetEntity.LogicalName,
            targetPhysical: targetEntity.PhysicalName);

        var sourceEntity = TighteningEvaluatorTestHelper.CreateEntity(
            "SourceModule",
            "SourceEntity",
            "OSUSR_SRC",
            new[]
            {
                TighteningEvaluatorTestHelper.CreateAttribute("Id", "ID", isIdentifier: true),
                referenceAttribute
            });

        var sourceModule = TighteningEvaluatorTestHelper.CreateModule("SourceModule", sourceEntity);
        var targetModule = TighteningEvaluatorTestHelper.CreateModule("TargetModule", targetEntity);
        var model = TighteningEvaluatorTestHelper.CreateModel(sourceModule, targetModule);

        var foreignKeyTargets = TighteningEvaluatorTestHelper.CreateForeignKeyTargetIndex(
            model,
            new Dictionary<EntityName, EntityModel>
            {
                [sourceEntity.LogicalName] = sourceEntity,
                [targetEntity.LogicalName] = targetEntity
            });

        var fkReality = TighteningEvaluatorTestHelper.CreateForeignKeyReality(coordinate, targetCoordinate, hasConstraint: false, hasOrphan: false);

        var creationEnabled = ForeignKeyOptions.Create(
            enableCreation: true,
            allowCrossSchema: options.ForeignKeys.AllowCrossSchema,
            allowCrossCatalog: options.ForeignKeys.AllowCrossCatalog).Value;

        var evaluator = new ForeignKeyEvaluator(
            creationEnabled,
            new Dictionary<ColumnCoordinate, ForeignKeyReality> { [coordinate] = fkReality },
            foreignKeyTargets);

        var decision = evaluator.Evaluate(sourceEntity, referenceAttribute, coordinate);

        Assert.True(decision.CreateConstraint);
        Assert.Contains(TighteningRationales.PolicyEnableCreation, decision.Rationales);
    }

    [Fact]
    public void TreatMissingDeleteRuleAsIgnore_AllowsCreation()
    {
        var options = TighteningPolicyTestHelper.CreateOptions(TighteningMode.Cautious);
        var coordinate = new ColumnCoordinate(new SchemaName("dbo"), new TableName("OSUSR_SRC"), new ColumnName("TARGET_ID"));
        var targetCoordinate = new ColumnCoordinate(new SchemaName("dbo"), new TableName("OSUSR_TARGET"), new ColumnName("ID"));

        var targetEntity = TighteningEvaluatorTestHelper.CreateEntity(
            "TargetModule",
            "TargetEntity",
            "OSUSR_TARGET",
            new[]
            {
                TighteningEvaluatorTestHelper.CreateAttribute("Id", "ID", isIdentifier: true)
            });

        var referenceAttribute = TighteningEvaluatorTestHelper.CreateAttribute(
            "TargetId",
            "TARGET_ID",
            isReference: true,
            deleteRule: null,
            targetEntity: targetEntity.LogicalName,
            targetPhysical: targetEntity.PhysicalName);

        var sourceEntity = TighteningEvaluatorTestHelper.CreateEntity(
            "SourceModule",
            "SourceEntity",
            "OSUSR_SRC",
            new[]
            {
                TighteningEvaluatorTestHelper.CreateAttribute("Id", "ID", isIdentifier: true),
                referenceAttribute
            });

        var sourceModule = TighteningEvaluatorTestHelper.CreateModule("SourceModule", sourceEntity);
        var targetModule = TighteningEvaluatorTestHelper.CreateModule("TargetModule", targetEntity);
        var model = TighteningEvaluatorTestHelper.CreateModel(sourceModule, targetModule);

        var foreignKeyTargets = TighteningEvaluatorTestHelper.CreateForeignKeyTargetIndex(
            model,
            new Dictionary<EntityName, EntityModel>
            {
                [sourceEntity.LogicalName] = sourceEntity,
                [targetEntity.LogicalName] = targetEntity
            });

        var fkReality = TighteningEvaluatorTestHelper.CreateForeignKeyReality(coordinate, targetCoordinate, hasConstraint: false, hasOrphan: false);

        var treatMissingAsIgnore = ForeignKeyOptions.Create(
            enableCreation: true,
            allowCrossSchema: options.ForeignKeys.AllowCrossSchema,
            allowCrossCatalog: options.ForeignKeys.AllowCrossCatalog,
            treatMissingDeleteRuleAsIgnore: true).Value;

        var evaluator = new ForeignKeyEvaluator(
            treatMissingAsIgnore,
            new Dictionary<ColumnCoordinate, ForeignKeyReality> { [coordinate] = fkReality },
            foreignKeyTargets);

        var decision = evaluator.Evaluate(sourceEntity, referenceAttribute, coordinate);

        Assert.True(decision.CreateConstraint);
        Assert.Contains(TighteningRationales.DeleteRuleIgnore, decision.Rationales);
        Assert.Contains(TighteningRationales.PolicyEnableCreation, decision.Rationales);
    }
}
