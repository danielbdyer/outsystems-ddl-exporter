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
    private static readonly ImmutableDictionary<TighteningMode, NullabilityModeExpectation> NullabilityExpectations
        = new Dictionary<TighteningMode, NullabilityModeExpectation>
        {
            [TighteningMode.Cautious] = new(
                Code: "MODE_CAUTIOUS",
                EvidenceEmbeddedInRoot: false,
                Signals: new Dictionary<TighteningPolicyMatrix.NullabilitySignalKey, NullabilitySignalExpectation>
                {
                    [TighteningPolicyMatrix.NullabilitySignalKey.PrimaryKey] = new(
                        TighteningPolicyMatrix.NullabilitySignalParticipation.Tighten,
                        RequiresEvidence: false,
                        AddsRemediationWhenEvidenceMissing: false),
                    [TighteningPolicyMatrix.NullabilitySignalKey.Physical] = new(
                        TighteningPolicyMatrix.NullabilitySignalParticipation.Tighten,
                        RequiresEvidence: false,
                        AddsRemediationWhenEvidenceMissing: false),
                    [TighteningPolicyMatrix.NullabilitySignalKey.Mandatory] = new(
                        TighteningPolicyMatrix.NullabilitySignalParticipation.Tighten,
                        RequiresEvidence: false,
                        AddsRemediationWhenEvidenceMissing: false),
                    [TighteningPolicyMatrix.NullabilitySignalKey.ForeignKey] = new(
                        TighteningPolicyMatrix.NullabilitySignalParticipation.TelemetryOnly,
                        RequiresEvidence: false,
                        AddsRemediationWhenEvidenceMissing: false),
                    [TighteningPolicyMatrix.NullabilitySignalKey.Unique] = new(
                        TighteningPolicyMatrix.NullabilitySignalParticipation.TelemetryOnly,
                        RequiresEvidence: false,
                        AddsRemediationWhenEvidenceMissing: false)
                }),
            [TighteningMode.EvidenceGated] = new(
                Code: "MODE_EVIDENCE_GATED",
                EvidenceEmbeddedInRoot: true,
                Signals: new Dictionary<TighteningPolicyMatrix.NullabilitySignalKey, NullabilitySignalExpectation>
                {
                    [TighteningPolicyMatrix.NullabilitySignalKey.PrimaryKey] = new(
                        TighteningPolicyMatrix.NullabilitySignalParticipation.Tighten,
                        RequiresEvidence: false,
                        AddsRemediationWhenEvidenceMissing: false),
                    [TighteningPolicyMatrix.NullabilitySignalKey.Physical] = new(
                        TighteningPolicyMatrix.NullabilitySignalParticipation.Tighten,
                        RequiresEvidence: false,
                        AddsRemediationWhenEvidenceMissing: false),
                    [TighteningPolicyMatrix.NullabilitySignalKey.Mandatory] = new(
                        TighteningPolicyMatrix.NullabilitySignalParticipation.Tighten,
                        RequiresEvidence: false,
                        AddsRemediationWhenEvidenceMissing: false),
                    [TighteningPolicyMatrix.NullabilitySignalKey.ForeignKey] = new(
                        TighteningPolicyMatrix.NullabilitySignalParticipation.Tighten,
                        RequiresEvidence: true,
                        AddsRemediationWhenEvidenceMissing: false),
                    [TighteningPolicyMatrix.NullabilitySignalKey.Unique] = new(
                        TighteningPolicyMatrix.NullabilitySignalParticipation.Tighten,
                        RequiresEvidence: true,
                        AddsRemediationWhenEvidenceMissing: false)
                }),
            [TighteningMode.Aggressive] = new(
                Code: "MODE_AGGRESSIVE",
                EvidenceEmbeddedInRoot: false,
                Signals: new Dictionary<TighteningPolicyMatrix.NullabilitySignalKey, NullabilitySignalExpectation>
                {
                    [TighteningPolicyMatrix.NullabilitySignalKey.PrimaryKey] = new(
                        TighteningPolicyMatrix.NullabilitySignalParticipation.Tighten,
                        RequiresEvidence: false,
                        AddsRemediationWhenEvidenceMissing: false),
                    [TighteningPolicyMatrix.NullabilitySignalKey.Physical] = new(
                        TighteningPolicyMatrix.NullabilitySignalParticipation.Tighten,
                        RequiresEvidence: false,
                        AddsRemediationWhenEvidenceMissing: false),
                    [TighteningPolicyMatrix.NullabilitySignalKey.Mandatory] = new(
                        TighteningPolicyMatrix.NullabilitySignalParticipation.Tighten,
                        RequiresEvidence: false,
                        AddsRemediationWhenEvidenceMissing: false),
                    [TighteningPolicyMatrix.NullabilitySignalKey.ForeignKey] = new(
                        TighteningPolicyMatrix.NullabilitySignalParticipation.Tighten,
                        RequiresEvidence: false,
                        AddsRemediationWhenEvidenceMissing: true),
                    [TighteningPolicyMatrix.NullabilitySignalKey.Unique] = new(
                        TighteningPolicyMatrix.NullabilitySignalParticipation.Tighten,
                        RequiresEvidence: false,
                        AddsRemediationWhenEvidenceMissing: true)
                })
        }.ToImmutableDictionary();

    private static readonly ImmutableDictionary<TighteningPolicyMatrix.NullabilitySignalKey, ImmutableArray<string>> NullabilityMetadataExpectations
        = new Dictionary<TighteningPolicyMatrix.NullabilitySignalKey, ImmutableArray<string>>
        {
            [TighteningPolicyMatrix.NullabilitySignalKey.PrimaryKey] = ImmutableArray.Create(TighteningRationales.PrimaryKey),
            [TighteningPolicyMatrix.NullabilitySignalKey.Physical] = ImmutableArray.Create(TighteningRationales.PhysicalNotNull),
            [TighteningPolicyMatrix.NullabilitySignalKey.ForeignKey] = ImmutableArray.Create(
                TighteningRationales.ForeignKeyEnforced,
                TighteningRationales.DeleteRuleIgnore,
                TighteningRationales.DataHasOrphans),
            [TighteningPolicyMatrix.NullabilitySignalKey.Unique] = ImmutableArray.Create(
                TighteningRationales.UniqueNoNulls,
                TighteningRationales.CompositeUniqueNoNulls,
                TighteningRationales.UniqueDuplicatesPresent,
                TighteningRationales.CompositeUniqueDuplicatesPresent),
            [TighteningPolicyMatrix.NullabilitySignalKey.Mandatory] = ImmutableArray.Create(
                TighteningRationales.Mandatory,
                TighteningRationales.DataHasNulls)
        }.ToImmutableDictionary();

    private static readonly ImmutableDictionary<(TighteningMode Mode, TighteningPolicyMatrix.UniquePolicyScenario Scenario), UniqueExpectation> UniqueExpectations
        = new Dictionary<(TighteningMode, TighteningPolicyMatrix.UniquePolicyScenario), UniqueExpectation>
        {
            [(TighteningMode.Cautious, TighteningPolicyMatrix.UniquePolicyScenario.PolicyDisabled)] = new(
                EnforceUnique: false,
                Remediation: TighteningPolicyMatrix.RemediationDirective.None,
                ImmutableArray.Create(TighteningRationales.UniquePolicyDisabled)),
            [(TighteningMode.Cautious, TighteningPolicyMatrix.UniquePolicyScenario.PhysicalReality)] = new(
                EnforceUnique: true,
                Remediation: TighteningPolicyMatrix.RemediationDirective.None,
                ImmutableArray.Create(TighteningRationales.PhysicalUniqueKey)),
            [(TighteningMode.Cautious, TighteningPolicyMatrix.UniquePolicyScenario.DuplicatesWithPhysicalReality)] = new(
                EnforceUnique: true,
                Remediation: TighteningPolicyMatrix.RemediationDirective.None,
                ImmutableArray.Create(
                    TighteningRationales.PhysicalUniqueKey,
                    TighteningRationales.UniqueDuplicatesPresent,
                    TighteningRationales.CompositeUniqueDuplicatesPresent)),
            [(TighteningMode.Cautious, TighteningPolicyMatrix.UniquePolicyScenario.DuplicatesWithoutPhysicalReality)] = new(
                EnforceUnique: false,
                Remediation: TighteningPolicyMatrix.RemediationDirective.None,
                ImmutableArray.Create(
                    TighteningRationales.UniqueDuplicatesPresent,
                    TighteningRationales.CompositeUniqueDuplicatesPresent)),
            [(TighteningMode.Cautious, TighteningPolicyMatrix.UniquePolicyScenario.CleanWithEvidence)] = new(
                EnforceUnique: false,
                Remediation: TighteningPolicyMatrix.RemediationDirective.None,
                ImmutableArray.Create(
                    TighteningRationales.DataNoNulls,
                    TighteningRationales.UniqueNoNulls,
                    TighteningRationales.CompositeUniqueNoNulls)),
            [(TighteningMode.Cautious, TighteningPolicyMatrix.UniquePolicyScenario.CleanWithoutEvidence)] = new(
                EnforceUnique: false,
                Remediation: TighteningPolicyMatrix.RemediationDirective.None,
                ImmutableArray.Create(
                    TighteningRationales.ProfileMissing,
                    TighteningRationales.UniqueNoNulls,
                    TighteningRationales.CompositeUniqueNoNulls)),
            [(TighteningMode.EvidenceGated, TighteningPolicyMatrix.UniquePolicyScenario.PolicyDisabled)] = new(
                EnforceUnique: false,
                Remediation: TighteningPolicyMatrix.RemediationDirective.None,
                ImmutableArray.Create(TighteningRationales.UniquePolicyDisabled)),
            [(TighteningMode.EvidenceGated, TighteningPolicyMatrix.UniquePolicyScenario.PhysicalReality)] = new(
                EnforceUnique: true,
                Remediation: TighteningPolicyMatrix.RemediationDirective.None,
                ImmutableArray.Create(TighteningRationales.PhysicalUniqueKey)),
            [(TighteningMode.EvidenceGated, TighteningPolicyMatrix.UniquePolicyScenario.DuplicatesWithPhysicalReality)] = new(
                EnforceUnique: true,
                Remediation: TighteningPolicyMatrix.RemediationDirective.None,
                ImmutableArray.Create(
                    TighteningRationales.PhysicalUniqueKey,
                    TighteningRationales.UniqueDuplicatesPresent,
                    TighteningRationales.CompositeUniqueDuplicatesPresent)),
            [(TighteningMode.EvidenceGated, TighteningPolicyMatrix.UniquePolicyScenario.DuplicatesWithoutPhysicalReality)] = new(
                EnforceUnique: false,
                Remediation: TighteningPolicyMatrix.RemediationDirective.None,
                ImmutableArray.Create(
                    TighteningRationales.UniqueDuplicatesPresent,
                    TighteningRationales.CompositeUniqueDuplicatesPresent)),
            [(TighteningMode.EvidenceGated, TighteningPolicyMatrix.UniquePolicyScenario.CleanWithEvidence)] = new(
                EnforceUnique: true,
                Remediation: TighteningPolicyMatrix.RemediationDirective.None,
                ImmutableArray.Create(
                    TighteningRationales.DataNoNulls,
                    TighteningRationales.UniqueNoNulls,
                    TighteningRationales.CompositeUniqueNoNulls)),
            [(TighteningMode.EvidenceGated, TighteningPolicyMatrix.UniquePolicyScenario.CleanWithoutEvidence)] = new(
                EnforceUnique: false,
                Remediation: TighteningPolicyMatrix.RemediationDirective.None,
                ImmutableArray.Create(
                    TighteningRationales.ProfileMissing,
                    TighteningRationales.UniqueNoNulls,
                    TighteningRationales.CompositeUniqueNoNulls)),
            [(TighteningMode.Aggressive, TighteningPolicyMatrix.UniquePolicyScenario.PolicyDisabled)] = new(
                EnforceUnique: false,
                Remediation: TighteningPolicyMatrix.RemediationDirective.None,
                ImmutableArray.Create(TighteningRationales.UniquePolicyDisabled)),
            [(TighteningMode.Aggressive, TighteningPolicyMatrix.UniquePolicyScenario.PhysicalReality)] = new(
                EnforceUnique: true,
                Remediation: TighteningPolicyMatrix.RemediationDirective.None,
                ImmutableArray.Create(TighteningRationales.PhysicalUniqueKey)),
            [(TighteningMode.Aggressive, TighteningPolicyMatrix.UniquePolicyScenario.DuplicatesWithPhysicalReality)] = new(
                EnforceUnique: true,
                Remediation: TighteningPolicyMatrix.RemediationDirective.Always,
                ImmutableArray.Create(
                    TighteningRationales.PhysicalUniqueKey,
                    TighteningRationales.UniqueDuplicatesPresent,
                    TighteningRationales.CompositeUniqueDuplicatesPresent,
                    TighteningRationales.RemediateBeforeTighten)),
            [(TighteningMode.Aggressive, TighteningPolicyMatrix.UniquePolicyScenario.DuplicatesWithoutPhysicalReality)] = new(
                EnforceUnique: true,
                Remediation: TighteningPolicyMatrix.RemediationDirective.Always,
                ImmutableArray.Create(
                    TighteningRationales.UniqueDuplicatesPresent,
                    TighteningRationales.CompositeUniqueDuplicatesPresent,
                    TighteningRationales.ProfileMissing,
                    TighteningRationales.RemediateBeforeTighten)),
            [(TighteningMode.Aggressive, TighteningPolicyMatrix.UniquePolicyScenario.CleanWithEvidence)] = new(
                EnforceUnique: true,
                Remediation: TighteningPolicyMatrix.RemediationDirective.None,
                ImmutableArray.Create(
                    TighteningRationales.DataNoNulls,
                    TighteningRationales.UniqueNoNulls,
                    TighteningRationales.CompositeUniqueNoNulls)),
            [(TighteningMode.Aggressive, TighteningPolicyMatrix.UniquePolicyScenario.CleanWithoutEvidence)] = new(
                EnforceUnique: true,
                Remediation: TighteningPolicyMatrix.RemediationDirective.WhenEvidenceMissing,
                ImmutableArray.Create(
                    TighteningRationales.ProfileMissing,
                    TighteningRationales.UniqueNoNulls,
                    TighteningRationales.CompositeUniqueNoNulls,
                    TighteningRationales.RemediateBeforeTighten))
        }.ToImmutableDictionary();

    private static readonly ImmutableDictionary<TighteningPolicyMatrix.ForeignKeyPolicyScenario, ForeignKeyExpectation> ForeignKeyExpectations
        = new Dictionary<TighteningPolicyMatrix.ForeignKeyPolicyScenario, ForeignKeyExpectation>
        {
            [TighteningPolicyMatrix.ForeignKeyPolicyScenario.IgnoreRule] = new(
                DeleteRuleIsIgnore: true,
                HasOrphans: false,
                HasExistingConstraint: false,
                CrossSchema: false,
                CrossCatalog: false,
                EnableCreation: true,
                AllowCrossSchema: true,
                AllowCrossCatalog: true,
                ExpectCreate: true,
                Rationales: ImmutableArray.Create(
                    TighteningRationales.DeleteRuleIgnore,
                    TighteningRationales.PolicyEnableCreation)),
            [TighteningPolicyMatrix.ForeignKeyPolicyScenario.HasOrphan] = new(
                DeleteRuleIsIgnore: false,
                HasOrphans: true,
                HasExistingConstraint: false,
                CrossSchema: false,
                CrossCatalog: false,
                EnableCreation: true,
                AllowCrossSchema: true,
                AllowCrossCatalog: true,
                ExpectCreate: false,
                Rationales: ImmutableArray.Create(TighteningRationales.DataHasOrphans)),
            [TighteningPolicyMatrix.ForeignKeyPolicyScenario.ExistingConstraint] = new(
                DeleteRuleIsIgnore: false,
                HasOrphans: false,
                HasExistingConstraint: true,
                CrossSchema: false,
                CrossCatalog: false,
                EnableCreation: true,
                AllowCrossSchema: false,
                AllowCrossCatalog: false,
                ExpectCreate: true,
                Rationales: ImmutableArray.Create(TighteningRationales.DatabaseConstraintPresent)),
            [TighteningPolicyMatrix.ForeignKeyPolicyScenario.PolicyDisabled] = new(
                DeleteRuleIsIgnore: false,
                HasOrphans: false,
                HasExistingConstraint: false,
                CrossSchema: false,
                CrossCatalog: false,
                EnableCreation: false,
                AllowCrossSchema: true,
                AllowCrossCatalog: true,
                ExpectCreate: false,
                Rationales: ImmutableArray.Create(TighteningRationales.ForeignKeyCreationDisabled)),
            [TighteningPolicyMatrix.ForeignKeyPolicyScenario.CrossSchemaBlocked] = new(
                DeleteRuleIsIgnore: false,
                HasOrphans: false,
                HasExistingConstraint: false,
                CrossSchema: true,
                CrossCatalog: false,
                EnableCreation: true,
                AllowCrossSchema: false,
                AllowCrossCatalog: true,
                ExpectCreate: false,
                Rationales: ImmutableArray.Create(TighteningRationales.CrossSchema)),
            [TighteningPolicyMatrix.ForeignKeyPolicyScenario.CrossCatalogBlocked] = new(
                DeleteRuleIsIgnore: false,
                HasOrphans: false,
                HasExistingConstraint: false,
                CrossSchema: false,
                CrossCatalog: true,
                EnableCreation: true,
                AllowCrossSchema: true,
                AllowCrossCatalog: false,
                ExpectCreate: false,
                Rationales: ImmutableArray.Create(TighteningRationales.CrossCatalog)),
            [TighteningPolicyMatrix.ForeignKeyPolicyScenario.Eligible] = new(
                DeleteRuleIsIgnore: false,
                HasOrphans: false,
                HasExistingConstraint: false,
                CrossSchema: false,
                CrossCatalog: false,
                EnableCreation: true,
                AllowCrossSchema: true,
                AllowCrossCatalog: true,
                ExpectCreate: true,
                Rationales: ImmutableArray.Create(TighteningRationales.PolicyEnableCreation))
        }.ToImmutableDictionary();

    [Fact]
    public void NullabilityMatrix_DefinitionsMatchExpectations()
    {
        var definitions = TighteningPolicyMatrix.Nullability.Definitions;
        Assert.Equal(NullabilityExpectations.Count, definitions.Length);

        foreach (var (mode, expectation) in NullabilityExpectations)
        {
            var definition = TighteningPolicyMatrix.Nullability.GetMode(mode);

            Assert.Equal(expectation.Code, definition.Code);
            Assert.Equal(expectation.EvidenceEmbeddedInRoot, definition.EvidenceEmbeddedInRoot);
            Assert.Equal(expectation.Signals.Count, definition.SignalDefinitions.Length);

            foreach (var (signal, signalExpectation) in expectation.Signals)
            {
                var actual = definition.GetDefinition(signal);
                Assert.Equal(signalExpectation.Participation, actual.Participation);
                Assert.Equal(signalExpectation.RequiresEvidence, actual.RequiresEvidence);
                Assert.Equal(signalExpectation.AddsRemediationWhenEvidenceMissing, actual.AddsRemediationWhenEvidenceMissing);
            }

            var expectedConditional = expectation.Signals
                .Where(pair => pair.Value.Participation == TighteningPolicyMatrix.NullabilitySignalParticipation.Tighten
                    && (pair.Value.RequiresEvidence || pair.Value.AddsRemediationWhenEvidenceMissing))
                .Select(pair => pair.Key)
                .OrderBy(static key => key)
                .ToArray();

            var actualConditional = definition.ConditionalSignals
                .OrderBy(static key => key)
                .ToArray();

            Assert.Equal(expectedConditional, actualConditional);
        }
    }

    [Fact]
    public void NullabilityMatrix_MetadataMatchesExpectations()
    {
        foreach (var (signal, expectedRationales) in NullabilityMetadataExpectations)
        {
            var metadata = TighteningPolicyMatrix.Nullability.GetMetadata(signal);
            Assert.Equal(expectedRationales.OrderBy(static rationale => rationale), metadata.Rationales.OrderBy(static rationale => rationale));
        }
    }

    [Fact]
    public void UniqueMatrix_RowCountMatchesExpectations()
    {
        Assert.Equal(UniqueExpectations.Count, TighteningPolicyMatrix.UniqueIndexes.Definitions.Length);
    }

    public static IEnumerable<object[]> UniqueMatrixData()
    {
        foreach (var ((mode, scenario), expectation) in UniqueExpectations)
        {
            yield return new object[]
            {
                mode,
                scenario.ToString(),
                expectation.EnforceUnique,
                expectation.Remediation.ToString(),
                expectation.Rationales.ToArray()
            };
        }
    }

    [Theory]
    [MemberData(nameof(UniqueMatrixData))]
    public void UniqueMatrix_DefinitionsRemainStable(
        TighteningMode mode,
        string scenarioName,
        bool enforce,
        string remediationName,
        string[] expectedRationales)
    {
        var scenario = Enum.Parse<TighteningPolicyMatrix.UniquePolicyScenario>(scenarioName);
        var remediation = Enum.Parse<TighteningPolicyMatrix.RemediationDirective>(remediationName);
        var definition = TighteningPolicyMatrix.UniqueIndexes.GetDefinition(mode, scenario);
        Assert.Equal(enforce, definition.Outcome.EnforceUnique);
        Assert.Equal(remediation, definition.Outcome.Remediation);
        Assert.Equal(expectedRationales.OrderBy(static rationale => rationale), definition.Rationales.OrderBy(static rationale => rationale));
    }

    [Fact]
    public void ForeignKeyMatrix_DefinitionsMatchExpectations()
    {
        Assert.Equal(ForeignKeyExpectations.Count, TighteningPolicyMatrix.ForeignKeys.Definitions.Length);

        foreach (var (scenario, expectation) in ForeignKeyExpectations)
        {
            var definition = TighteningPolicyMatrix.ForeignKeys.Resolve(scenario);

            Assert.Equal(expectation.DeleteRuleIsIgnore, definition.DeleteRuleIsIgnore);
            Assert.Equal(expectation.HasOrphans, definition.HasOrphans);
            Assert.Equal(expectation.HasExistingConstraint, definition.HasExistingConstraint);
            Assert.Equal(expectation.CrossSchema, definition.CrossSchema);
            Assert.Equal(expectation.CrossCatalog, definition.CrossCatalog);
            Assert.Equal(expectation.EnableCreation, definition.EnableCreation);
            Assert.Equal(expectation.AllowCrossSchema, definition.AllowCrossSchema);
            Assert.Equal(expectation.AllowCrossCatalog, definition.AllowCrossCatalog);
            Assert.Equal(expectation.ExpectCreate, definition.ExpectCreate);
            Assert.Equal(expectation.Rationales.OrderBy(static rationale => rationale), definition.Rationales.OrderBy(static rationale => rationale));
        }
    }

    public static IEnumerable<object[]> ForeignKeyMatrixData()
    {
        foreach (var (scenario, expectation) in ForeignKeyExpectations)
        {
            yield return new object[] { scenario.ToString(), expectation.ExpectCreate };
        }
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
            setup.TargetIndex,
            TighteningMode.Aggressive);

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
            definition.AllowCrossCatalog,
            allowNoCheckCreation: false).Value;

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
    private sealed record NullabilitySignalExpectation(
        TighteningPolicyMatrix.NullabilitySignalParticipation Participation,
        bool RequiresEvidence,
        bool AddsRemediationWhenEvidenceMissing);

    private sealed record NullabilityModeExpectation(
        string Code,
        bool EvidenceEmbeddedInRoot,
        IReadOnlyDictionary<TighteningPolicyMatrix.NullabilitySignalKey, NullabilitySignalExpectation> Signals);

    private sealed record UniqueExpectation(
        bool EnforceUnique,
        TighteningPolicyMatrix.RemediationDirective Remediation,
        ImmutableArray<string> Rationales);

    private sealed record ForeignKeyExpectation(
        bool DeleteRuleIsIgnore,
        bool HasOrphans,
        bool HasExistingConstraint,
        bool CrossSchema,
        bool CrossCatalog,
        bool EnableCreation,
        bool AllowCrossSchema,
        bool AllowCrossCatalog,
        bool ExpectCreate,
        ImmutableArray<string> Rationales);
}
