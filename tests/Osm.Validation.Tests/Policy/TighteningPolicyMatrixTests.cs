using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Validation.Tightening;
using Xunit;

namespace Osm.Validation.Tests.Policy;

public sealed class TighteningPolicyMatrixTests
{
    public static IEnumerable<object[]> NullabilityMatrixData()
    {
        yield return new object[]
        {
            TighteningMode.Cautious,
            "MODE_CAUTIOUS",
            new[]
            {
                TighteningPolicyMatrix.NullabilitySignalKey.PrimaryKey.ToString(),
                TighteningPolicyMatrix.NullabilitySignalKey.Physical.ToString()
            },
            Array.Empty<string>(),
            false
        };

        yield return new object[]
        {
            TighteningMode.EvidenceGated,
            "MODE_EVIDENCE_GATED",
            new[]
            {
                TighteningPolicyMatrix.NullabilitySignalKey.PrimaryKey.ToString(),
                TighteningPolicyMatrix.NullabilitySignalKey.Physical.ToString()
            },
            new[]
            {
                TighteningPolicyMatrix.NullabilitySignalKey.ForeignKey.ToString(),
                TighteningPolicyMatrix.NullabilitySignalKey.Unique.ToString(),
                TighteningPolicyMatrix.NullabilitySignalKey.Mandatory.ToString()
            },
            true
        };

        yield return new object[]
        {
            TighteningMode.Aggressive,
            "MODE_AGGRESSIVE",
            new[]
            {
                TighteningPolicyMatrix.NullabilitySignalKey.PrimaryKey.ToString(),
                TighteningPolicyMatrix.NullabilitySignalKey.Physical.ToString()
            },
            new[]
            {
                TighteningPolicyMatrix.NullabilitySignalKey.ForeignKey.ToString(),
                TighteningPolicyMatrix.NullabilitySignalKey.Unique.ToString(),
                TighteningPolicyMatrix.NullabilitySignalKey.Mandatory.ToString()
            },
            false
        };
    }

    [Theory]
    [MemberData(nameof(NullabilityMatrixData))]
    public void NullabilityMatrix_DefinitionsRemainStable(
        TighteningMode mode,
        string expectedCode,
        string[] expectedCore,
        string[] expectedConditional,
        bool evidenceEmbedded)
    {
        var definition = TighteningPolicyMatrix.Nullability.GetMode(mode);

        Assert.Equal(expectedCode, definition.Code);
        var expectedCoreKeys = expectedCore.Select(value => Enum.Parse<TighteningPolicyMatrix.NullabilitySignalKey>(value));
        Assert.Equal(expectedCoreKeys, definition.CoreSignals);
        Assert.Equal(evidenceEmbedded, definition.EvidenceEmbeddedInRoot);

        if (expectedConditional.Length == 0)
        {
            Assert.Null(definition.ConditionalGroup);
        }
        else
        {
            Assert.NotNull(definition.ConditionalGroup);
            var conditionalKeys = expectedConditional.Select(value => Enum.Parse<TighteningPolicyMatrix.NullabilitySignalKey>(value));
            Assert.Equal(conditionalKeys, definition.ConditionalGroup!.Signals);
            var requiresEvidence = mode == TighteningMode.EvidenceGated;
            Assert.Equal(requiresEvidence, definition.ConditionalGroup.RequiresEvidence);
        }
    }

    [Fact]
    public void NullabilityMatrix_ConditionalSignalsCoverStrongSignals()
    {
        var signals = TighteningPolicyMatrix.Nullability.ConditionalSignals;
        Assert.Contains(TighteningPolicyMatrix.NullabilitySignalKey.ForeignKey, signals);
        Assert.Contains(TighteningPolicyMatrix.NullabilitySignalKey.Unique, signals);
        Assert.Contains(TighteningPolicyMatrix.NullabilitySignalKey.Mandatory, signals);
    }

    public static IEnumerable<object[]> UniqueMatrixData()
    {
        foreach (var mode in new[]
        {
            TighteningMode.Cautious,
            TighteningMode.EvidenceGated,
            TighteningMode.Aggressive
        })
        {
            yield return new object[]
            {
                mode,
                TighteningPolicyMatrix.UniquePolicyScenario.PolicyDisabled.ToString(),
                false,
                TighteningPolicyMatrix.RemediationDirective.None.ToString()
            };

            yield return new object[]
            {
                mode,
                TighteningPolicyMatrix.UniquePolicyScenario.PhysicalReality.ToString(),
                true,
                TighteningPolicyMatrix.RemediationDirective.None.ToString()
            };
        }

        yield return new object[]
        {
            TighteningMode.Cautious,
            TighteningPolicyMatrix.UniquePolicyScenario.DuplicatesWithPhysicalReality.ToString(),
            true,
            TighteningPolicyMatrix.RemediationDirective.None.ToString()
        };

        yield return new object[]
        {
            TighteningMode.Cautious,
            TighteningPolicyMatrix.UniquePolicyScenario.DuplicatesWithoutPhysicalReality.ToString(),
            false,
            TighteningPolicyMatrix.RemediationDirective.None.ToString()
        };

        yield return new object[]
        {
            TighteningMode.Cautious,
            TighteningPolicyMatrix.UniquePolicyScenario.CleanWithEvidence.ToString(),
            false,
            TighteningPolicyMatrix.RemediationDirective.None.ToString()
        };

        yield return new object[]
        {
            TighteningMode.Cautious,
            TighteningPolicyMatrix.UniquePolicyScenario.CleanWithoutEvidence.ToString(),
            false,
            TighteningPolicyMatrix.RemediationDirective.None.ToString()
        };

        yield return new object[]
        {
            TighteningMode.EvidenceGated,
            TighteningPolicyMatrix.UniquePolicyScenario.DuplicatesWithPhysicalReality.ToString(),
            true,
            TighteningPolicyMatrix.RemediationDirective.None.ToString()
        };

        yield return new object[]
        {
            TighteningMode.EvidenceGated,
            TighteningPolicyMatrix.UniquePolicyScenario.DuplicatesWithoutPhysicalReality.ToString(),
            false,
            TighteningPolicyMatrix.RemediationDirective.None.ToString()
        };

        yield return new object[]
        {
            TighteningMode.EvidenceGated,
            TighteningPolicyMatrix.UniquePolicyScenario.CleanWithEvidence.ToString(),
            true,
            TighteningPolicyMatrix.RemediationDirective.None.ToString()
        };

        yield return new object[]
        {
            TighteningMode.EvidenceGated,
            TighteningPolicyMatrix.UniquePolicyScenario.CleanWithoutEvidence.ToString(),
            false,
            TighteningPolicyMatrix.RemediationDirective.None.ToString()
        };

        yield return new object[]
        {
            TighteningMode.Aggressive,
            TighteningPolicyMatrix.UniquePolicyScenario.DuplicatesWithPhysicalReality.ToString(),
            true,
            TighteningPolicyMatrix.RemediationDirective.Always.ToString()
        };

        yield return new object[]
        {
            TighteningMode.Aggressive,
            TighteningPolicyMatrix.UniquePolicyScenario.DuplicatesWithoutPhysicalReality.ToString(),
            true,
            TighteningPolicyMatrix.RemediationDirective.Always.ToString()
        };

        yield return new object[]
        {
            TighteningMode.Aggressive,
            TighteningPolicyMatrix.UniquePolicyScenario.CleanWithEvidence.ToString(),
            true,
            TighteningPolicyMatrix.RemediationDirective.None.ToString()
        };

        yield return new object[]
        {
            TighteningMode.Aggressive,
            TighteningPolicyMatrix.UniquePolicyScenario.CleanWithoutEvidence.ToString(),
            true,
            TighteningPolicyMatrix.RemediationDirective.WhenEvidenceMissing.ToString()
        };
    }

    [Theory]
    [MemberData(nameof(UniqueMatrixData))]
    public void UniqueMatrix_DefinitionsRemainStable(
        TighteningMode mode,
        string scenarioName,
        bool enforce,
        string remediationName)
    {
        var scenario = Enum.Parse<TighteningPolicyMatrix.UniquePolicyScenario>(scenarioName);
        var remediation = Enum.Parse<TighteningPolicyMatrix.RemediationDirective>(remediationName);
        var outcome = TighteningPolicyMatrix.UniqueIndexes.Resolve(mode, scenario);
        Assert.Equal(enforce, outcome.EnforceUnique);
        Assert.Equal(remediation, outcome.Remediation);
    }

    public static IEnumerable<object[]> ForeignKeyMatrixData()
    {
        yield return new object[] { TighteningPolicyMatrix.ForeignKeyPolicyScenario.IgnoreRule.ToString(), false };
        yield return new object[] { TighteningPolicyMatrix.ForeignKeyPolicyScenario.HasOrphan.ToString(), false };
        yield return new object[] { TighteningPolicyMatrix.ForeignKeyPolicyScenario.ExistingConstraint.ToString(), true };
        yield return new object[] { TighteningPolicyMatrix.ForeignKeyPolicyScenario.PolicyDisabled.ToString(), false };
        yield return new object[] { TighteningPolicyMatrix.ForeignKeyPolicyScenario.CrossSchemaBlocked.ToString(), false };
        yield return new object[] { TighteningPolicyMatrix.ForeignKeyPolicyScenario.CrossCatalogBlocked.ToString(), false };
        yield return new object[] { TighteningPolicyMatrix.ForeignKeyPolicyScenario.Eligible.ToString(), true };
    }

    [Theory]
    [MemberData(nameof(ForeignKeyMatrixData))]
    public void ForeignKeyMatrix_EvaluatorAlignsWithDefinitions(
        string scenarioName,
        bool expectCreate)
    {
        var scenario = Enum.Parse<TighteningPolicyMatrix.ForeignKeyPolicyScenario>(scenarioName);
        var definition = TighteningPolicyMatrix.ForeignKeys.Resolve(scenario);

        var setup = BuildForeignKeyScenario(definition);

        var evaluator = new ForeignKeyEvaluator(
            setup.Options,
            setup.RealityMap,
            setup.TargetIndex);

        var decision = evaluator.Evaluate(setup.Source, setup.Attribute, setup.Coordinate);

        Assert.Equal(expectCreate, decision.CreateConstraint);
        foreach (var rationale in definition.Rationales)
        {
            Assert.Contains(rationale, decision.Rationales);
        }
    }

    private static ForeignKeyScenarioContext BuildForeignKeyScenario(TighteningPolicyMatrix.ForeignKeyPolicyDefinition definition)
    {
        var targetSchema = definition.CrossSchema ? new SchemaName("other") : new SchemaName("dbo");
        var targetCatalog = definition.CrossCatalog ? "OtherDb" : null;

        var targetAttribute = TighteningEvaluatorTestHelper.CreateAttribute(
            "TargetId",
            "TARGETID",
            isIdentifier: true,
            isMandatory: true);

        var targetEntity = TighteningEvaluatorTestHelper.CreateEntity(
            "Module",
            "Target",
            "OSUSR_TARGET",
            new[] { targetAttribute },
            schema: targetSchema,
            catalog: targetCatalog);

        var deleteRule = definition.DeleteRuleIsIgnore ? "Ignore" : "Cascade";

        var sourceAttribute = TighteningEvaluatorTestHelper.CreateAttribute(
            "Target",
            "TARGETID",
            isReference: true,
            deleteRule: deleteRule,
            hasDatabaseConstraint: definition.HasExistingConstraint,
            targetEntity: targetEntity.LogicalName,
            targetPhysical: targetEntity.PhysicalName);

        var sourceEntity = TighteningEvaluatorTestHelper.CreateEntity(
            "Module",
            "Source",
            "OSUSR_SOURCE",
            new[] { sourceAttribute });

        var module = TighteningEvaluatorTestHelper.CreateModule("Module", sourceEntity, targetEntity);
        var model = TighteningEvaluatorTestHelper.CreateModel(module);

        var coordinate = new ColumnCoordinate(sourceEntity.Schema, sourceEntity.PhysicalName, sourceAttribute.ColumnName);
        var targetCoordinate = new ColumnCoordinate(targetEntity.Schema, targetEntity.PhysicalName, targetAttribute.ColumnName);

        var options = ForeignKeyOptions.Create(
            definition.EnableCreation,
            definition.AllowCrossSchema,
            definition.AllowCrossCatalog).Value;

        var reality = TighteningEvaluatorTestHelper.CreateForeignKeyReality(
            coordinate,
            targetCoordinate,
            definition.HasExistingConstraint,
            definition.HasOrphans);

        var realityMap = new Dictionary<ColumnCoordinate, ForeignKeyReality>
        {
            [coordinate] = reality
        };

        var entityLookup = new Dictionary<EntityName, EntityModel>
        {
            [sourceEntity.LogicalName] = sourceEntity,
            [targetEntity.LogicalName] = targetEntity
        };

        var targetIndex = TighteningEvaluatorTestHelper.CreateForeignKeyTargetIndex(model, entityLookup);

        return new ForeignKeyScenarioContext(sourceEntity, sourceAttribute, coordinate, options, realityMap, targetIndex);
    }

    private sealed record ForeignKeyScenarioContext(
        EntityModel Source,
        AttributeModel Attribute,
        ColumnCoordinate Coordinate,
        ForeignKeyOptions Options,
        IReadOnlyDictionary<ColumnCoordinate, ForeignKeyReality> RealityMap,
        ForeignKeyTargetIndex TargetIndex);
}
