using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Configuration;

namespace Osm.Validation.Tightening;

internal static class TighteningPolicyMatrix
{
    internal static NullabilityMatrix Nullability { get; } = NullabilityMatrix.Create();

    internal static UniqueIndexMatrix UniqueIndexes { get; } = UniqueIndexMatrix.Create();

    internal static ForeignKeyMatrix ForeignKeys { get; } = ForeignKeyMatrix.Create();

    internal enum NullabilitySignalKey
    {
        PrimaryKey,
        Physical,
        ForeignKey,
        Unique,
        Mandatory
    }

    internal enum NullabilitySignalParticipation
    {
        TelemetryOnly,
        Tighten
    }

    internal sealed record NullabilityMatrix(
        ImmutableDictionary<TighteningMode, NullabilityModeDefinition> Modes,
        ImmutableDictionary<NullabilitySignalKey, NullabilitySignalMetadata> Metadata)
    {
        public ImmutableArray<NullabilityModeDefinition> Definitions => Modes.Values.ToImmutableArray();

        public NullabilityModeDefinition GetMode(TighteningMode mode)
        {
            if (!Modes.TryGetValue(mode, out var definition))
            {
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported tightening mode.");
            }

            return definition;
        }

        public NullabilitySignalMetadata GetMetadata(NullabilitySignalKey key)
        {
            if (!Metadata.TryGetValue(key, out var metadata))
            {
                throw new ArgumentOutOfRangeException(nameof(key), key, "Nullability signal not defined.");
            }

            return metadata;
        }

        public static NullabilityMatrix Create()
        {
            var metadata = ImmutableArray.Create(
                new NullabilitySignalMetadata(
                    NullabilitySignalKey.PrimaryKey,
                    Code: "S1_PK",
                    Description: "Column is OutSystems Identifier (PK)",
                    Rationales: ImmutableArray.Create(TighteningRationales.PrimaryKey)),
                new NullabilitySignalMetadata(
                    NullabilitySignalKey.Physical,
                    Code: "S2_DB_NOT_NULL",
                    Description: "Physical column is marked NOT NULL",
                    Rationales: ImmutableArray.Create(TighteningRationales.PhysicalNotNull)),
                new NullabilitySignalMetadata(
                    NullabilitySignalKey.ForeignKey,
                    Code: "S3_FK_SUPPORT",
                    Description: "Foreign key has enforced relationship or can be created safely",
                    Rationales: ImmutableArray.Create(
                        TighteningRationales.ForeignKeyEnforced,
                        TighteningRationales.DeleteRuleIgnore,
                        TighteningRationales.DataHasOrphans)),
                new NullabilitySignalMetadata(
                    NullabilitySignalKey.Unique,
                    Code: "S4_UNIQUE_CLEAN",
                    Description: "Unique index (single or composite) has no nulls or duplicates",
                    Rationales: ImmutableArray.Create(
                        TighteningRationales.UniqueNoNulls,
                        TighteningRationales.CompositeUniqueNoNulls,
                        TighteningRationales.UniqueDuplicatesPresent,
                        TighteningRationales.CompositeUniqueDuplicatesPresent)),
                new NullabilitySignalMetadata(
                    NullabilitySignalKey.Mandatory,
                    Code: "S5_LOGICAL_MANDATORY",
                    Description: "Logical attribute is mandatory",
                    Rationales: ImmutableArray.Create(
                        TighteningRationales.Mandatory,
                        TighteningRationales.DataHasNulls))
            ).ToImmutableDictionary(static metadata => metadata.Signal);

            var definitions = ImmutableArray.Create(
                new NullabilityModeDefinition(
                    TighteningMode.Cautious,
                    Code: "MODE_CAUTIOUS",
                    Description: "Cautious policy (S1 ∪ S2 ∪ S5)",
                    SignalDefinitions: ImmutableArray.Create(
                        new NullabilitySignalDefinition(
                            NullabilitySignalKey.PrimaryKey,
                            NullabilitySignalParticipation.Tighten,
                            RequiresEvidence: false,
                            AddsRemediationWhenEvidenceMissing: false),
                        new NullabilitySignalDefinition(
                            NullabilitySignalKey.Physical,
                            NullabilitySignalParticipation.Tighten,
                            RequiresEvidence: false,
                            AddsRemediationWhenEvidenceMissing: false),
                        new NullabilitySignalDefinition(
                            NullabilitySignalKey.Mandatory,
                            NullabilitySignalParticipation.Tighten,
                            RequiresEvidence: false,
                            AddsRemediationWhenEvidenceMissing: false),
                        new NullabilitySignalDefinition(
                            NullabilitySignalKey.ForeignKey,
                            NullabilitySignalParticipation.TelemetryOnly,
                            RequiresEvidence: false,
                            AddsRemediationWhenEvidenceMissing: false),
                        new NullabilitySignalDefinition(
                            NullabilitySignalKey.Unique,
                            NullabilitySignalParticipation.TelemetryOnly,
                            RequiresEvidence: false,
                            AddsRemediationWhenEvidenceMissing: false)),
                    EvidenceEmbeddedInRoot: false),
                new NullabilityModeDefinition(
                    TighteningMode.EvidenceGated,
                    Code: "MODE_EVIDENCE_GATED",
                    Description: "Evidence gated policy",
                    SignalDefinitions: ImmutableArray.Create(
                        new NullabilitySignalDefinition(
                            NullabilitySignalKey.PrimaryKey,
                            NullabilitySignalParticipation.Tighten,
                            RequiresEvidence: false,
                            AddsRemediationWhenEvidenceMissing: false),
                        new NullabilitySignalDefinition(
                            NullabilitySignalKey.Physical,
                            NullabilitySignalParticipation.Tighten,
                            RequiresEvidence: false,
                            AddsRemediationWhenEvidenceMissing: false),
                        new NullabilitySignalDefinition(
                            NullabilitySignalKey.Mandatory,
                            NullabilitySignalParticipation.Tighten,
                            RequiresEvidence: false,
                            AddsRemediationWhenEvidenceMissing: false),
                        new NullabilitySignalDefinition(
                            NullabilitySignalKey.ForeignKey,
                            NullabilitySignalParticipation.Tighten,
                            RequiresEvidence: true,
                            AddsRemediationWhenEvidenceMissing: false),
                        new NullabilitySignalDefinition(
                            NullabilitySignalKey.Unique,
                            NullabilitySignalParticipation.Tighten,
                            RequiresEvidence: true,
                            AddsRemediationWhenEvidenceMissing: false)),
                    EvidenceEmbeddedInRoot: true),
                new NullabilityModeDefinition(
                    TighteningMode.Aggressive,
                    Code: "MODE_AGGRESSIVE",
                    Description: "Aggressive policy (S1 ∪ S2 ∪ S3 ∪ S4 ∪ S5)",
                    SignalDefinitions: ImmutableArray.Create(
                        new NullabilitySignalDefinition(
                            NullabilitySignalKey.PrimaryKey,
                            NullabilitySignalParticipation.Tighten,
                            RequiresEvidence: false,
                            AddsRemediationWhenEvidenceMissing: false),
                        new NullabilitySignalDefinition(
                            NullabilitySignalKey.Physical,
                            NullabilitySignalParticipation.Tighten,
                            RequiresEvidence: false,
                            AddsRemediationWhenEvidenceMissing: false),
                        new NullabilitySignalDefinition(
                            NullabilitySignalKey.Mandatory,
                            NullabilitySignalParticipation.Tighten,
                            RequiresEvidence: false,
                            AddsRemediationWhenEvidenceMissing: false),
                        new NullabilitySignalDefinition(
                            NullabilitySignalKey.ForeignKey,
                            NullabilitySignalParticipation.Tighten,
                            RequiresEvidence: false,
                            AddsRemediationWhenEvidenceMissing: true),
                        new NullabilitySignalDefinition(
                            NullabilitySignalKey.Unique,
                            NullabilitySignalParticipation.Tighten,
                            RequiresEvidence: false,
                            AddsRemediationWhenEvidenceMissing: true)),
                    EvidenceEmbeddedInRoot: false)
            );

            var modes = definitions.ToImmutableDictionary(static definition => definition.Mode);
            return new NullabilityMatrix(modes, metadata);
        }
    }

    internal sealed record NullabilityModeDefinition(
        TighteningMode Mode,
        string Code,
        string Description,
        ImmutableArray<NullabilitySignalDefinition> SignalDefinitions,
        bool EvidenceEmbeddedInRoot)
    {
        public NullabilitySignalDefinition GetDefinition(NullabilitySignalKey signal)
        {
            foreach (var definition in SignalDefinitions)
            {
                if (definition.Signal == signal)
                {
                    return definition;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(signal), signal, "Signal not defined for mode.");
        }

        public ImmutableHashSet<NullabilitySignalKey> ConditionalSignals => SignalDefinitions
            .Where(static definition => definition.Participation == NullabilitySignalParticipation.Tighten
                && (definition.RequiresEvidence || definition.AddsRemediationWhenEvidenceMissing))
            .Select(static definition => definition.Signal)
            .ToImmutableHashSet();
    }

    internal sealed record NullabilitySignalDefinition(
        NullabilitySignalKey Signal,
        NullabilitySignalParticipation Participation,
        bool RequiresEvidence,
        bool AddsRemediationWhenEvidenceMissing);

    internal sealed record NullabilitySignalMetadata(
        NullabilitySignalKey Signal,
        string Code,
        string Description,
        ImmutableArray<string> Rationales);

    internal enum UniquePolicyScenario
    {
        PolicyDisabled,
        DuplicatesWithPhysicalReality,
        DuplicatesWithoutPhysicalReality,
        PhysicalReality,
        CleanWithEvidence,
        CleanWithoutEvidence
    }

    internal sealed record UniqueIndexMatrix(
        ImmutableArray<UniqueIndexDefinition> Definitions,
        ImmutableDictionary<(TighteningMode Mode, UniquePolicyScenario Scenario), UniqueIndexDefinition> Lookup)
    {
        public UniqueIndexDefinition GetDefinition(TighteningMode mode, UniquePolicyScenario scenario)
        {
            if (!Lookup.TryGetValue((mode, scenario), out var definition))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(scenario),
                    scenario,
                    $"Scenario '{scenario}' is not defined for tightening mode '{mode}'.");
            }

            return definition;
        }

        public UniqueIndexOutcome Resolve(TighteningMode mode, UniquePolicyScenario scenario)
            => GetDefinition(mode, scenario).Outcome;

        public static UniqueIndexMatrix Create()
        {
            var definitions = ImmutableArray.Create(
                new UniqueIndexDefinition(
                    TighteningMode.Cautious,
                    UniquePolicyScenario.PolicyDisabled,
                    new UniqueIndexOutcome(false, RemediationDirective.None),
                    ImmutableArray.Create(TighteningRationales.UniquePolicyDisabled)),
                new UniqueIndexDefinition(
                    TighteningMode.Cautious,
                    UniquePolicyScenario.PhysicalReality,
                    new UniqueIndexOutcome(true, RemediationDirective.None),
                    ImmutableArray.Create(TighteningRationales.PhysicalUniqueKey)),
                new UniqueIndexDefinition(
                    TighteningMode.Cautious,
                    UniquePolicyScenario.DuplicatesWithPhysicalReality,
                    new UniqueIndexOutcome(true, RemediationDirective.None),
                    ImmutableArray.Create(
                        TighteningRationales.PhysicalUniqueKey,
                        TighteningRationales.UniqueDuplicatesPresent,
                        TighteningRationales.CompositeUniqueDuplicatesPresent)),
                new UniqueIndexDefinition(
                    TighteningMode.Cautious,
                    UniquePolicyScenario.DuplicatesWithoutPhysicalReality,
                    new UniqueIndexOutcome(false, RemediationDirective.None),
                    ImmutableArray.Create(
                        TighteningRationales.UniqueDuplicatesPresent,
                        TighteningRationales.CompositeUniqueDuplicatesPresent)),
                new UniqueIndexDefinition(
                    TighteningMode.Cautious,
                    UniquePolicyScenario.CleanWithEvidence,
                    new UniqueIndexOutcome(false, RemediationDirective.None),
                    ImmutableArray.Create(
                        TighteningRationales.DataNoNulls,
                        TighteningRationales.UniqueNoNulls,
                        TighteningRationales.CompositeUniqueNoNulls)),
                new UniqueIndexDefinition(
                    TighteningMode.Cautious,
                    UniquePolicyScenario.CleanWithoutEvidence,
                    new UniqueIndexOutcome(false, RemediationDirective.None),
                    ImmutableArray.Create(
                        TighteningRationales.ProfileMissing,
                        TighteningRationales.UniqueNoNulls,
                        TighteningRationales.CompositeUniqueNoNulls)),
                new UniqueIndexDefinition(
                    TighteningMode.EvidenceGated,
                    UniquePolicyScenario.PolicyDisabled,
                    new UniqueIndexOutcome(false, RemediationDirective.None),
                    ImmutableArray.Create(TighteningRationales.UniquePolicyDisabled)),
                new UniqueIndexDefinition(
                    TighteningMode.EvidenceGated,
                    UniquePolicyScenario.PhysicalReality,
                    new UniqueIndexOutcome(true, RemediationDirective.None),
                    ImmutableArray.Create(TighteningRationales.PhysicalUniqueKey)),
                new UniqueIndexDefinition(
                    TighteningMode.EvidenceGated,
                    UniquePolicyScenario.DuplicatesWithPhysicalReality,
                    new UniqueIndexOutcome(true, RemediationDirective.None),
                    ImmutableArray.Create(
                        TighteningRationales.PhysicalUniqueKey,
                        TighteningRationales.UniqueDuplicatesPresent,
                        TighteningRationales.CompositeUniqueDuplicatesPresent)),
                new UniqueIndexDefinition(
                    TighteningMode.EvidenceGated,
                    UniquePolicyScenario.DuplicatesWithoutPhysicalReality,
                    new UniqueIndexOutcome(false, RemediationDirective.None),
                    ImmutableArray.Create(
                        TighteningRationales.UniqueDuplicatesPresent,
                        TighteningRationales.CompositeUniqueDuplicatesPresent)),
                new UniqueIndexDefinition(
                    TighteningMode.EvidenceGated,
                    UniquePolicyScenario.CleanWithEvidence,
                    new UniqueIndexOutcome(true, RemediationDirective.None),
                    ImmutableArray.Create(
                        TighteningRationales.DataNoNulls,
                        TighteningRationales.UniqueNoNulls,
                        TighteningRationales.CompositeUniqueNoNulls)),
                new UniqueIndexDefinition(
                    TighteningMode.EvidenceGated,
                    UniquePolicyScenario.CleanWithoutEvidence,
                    new UniqueIndexOutcome(false, RemediationDirective.None),
                    ImmutableArray.Create(
                        TighteningRationales.ProfileMissing,
                        TighteningRationales.UniqueNoNulls,
                        TighteningRationales.CompositeUniqueNoNulls)),
                new UniqueIndexDefinition(
                    TighteningMode.Aggressive,
                    UniquePolicyScenario.PolicyDisabled,
                    new UniqueIndexOutcome(false, RemediationDirective.None),
                    ImmutableArray.Create(TighteningRationales.UniquePolicyDisabled)),
                new UniqueIndexDefinition(
                    TighteningMode.Aggressive,
                    UniquePolicyScenario.PhysicalReality,
                    new UniqueIndexOutcome(true, RemediationDirective.None),
                    ImmutableArray.Create(TighteningRationales.PhysicalUniqueKey)),
                new UniqueIndexDefinition(
                    TighteningMode.Aggressive,
                    UniquePolicyScenario.DuplicatesWithPhysicalReality,
                    new UniqueIndexOutcome(true, RemediationDirective.Always),
                    ImmutableArray.Create(
                        TighteningRationales.PhysicalUniqueKey,
                        TighteningRationales.UniqueDuplicatesPresent,
                        TighteningRationales.CompositeUniqueDuplicatesPresent,
                        TighteningRationales.RemediateBeforeTighten)),
                new UniqueIndexDefinition(
                    TighteningMode.Aggressive,
                    UniquePolicyScenario.DuplicatesWithoutPhysicalReality,
                    new UniqueIndexOutcome(true, RemediationDirective.Always),
                    ImmutableArray.Create(
                        TighteningRationales.UniqueDuplicatesPresent,
                        TighteningRationales.CompositeUniqueDuplicatesPresent,
                        TighteningRationales.ProfileMissing,
                        TighteningRationales.RemediateBeforeTighten)),
                new UniqueIndexDefinition(
                    TighteningMode.Aggressive,
                    UniquePolicyScenario.CleanWithEvidence,
                    new UniqueIndexOutcome(true, RemediationDirective.None),
                    ImmutableArray.Create(
                        TighteningRationales.DataNoNulls,
                        TighteningRationales.UniqueNoNulls,
                        TighteningRationales.CompositeUniqueNoNulls)),
                new UniqueIndexDefinition(
                    TighteningMode.Aggressive,
                    UniquePolicyScenario.CleanWithoutEvidence,
                    new UniqueIndexOutcome(true, RemediationDirective.WhenEvidenceMissing),
                    ImmutableArray.Create(
                        TighteningRationales.ProfileMissing,
                        TighteningRationales.UniqueNoNulls,
                        TighteningRationales.CompositeUniqueNoNulls,
                        TighteningRationales.RemediateBeforeTighten))
            );

            var lookup = definitions.ToImmutableDictionary(static definition => (definition.Mode, definition.Scenario));
            return new UniqueIndexMatrix(definitions, lookup);
        }
    }

    internal sealed record UniqueIndexDefinition(
        TighteningMode Mode,
        UniquePolicyScenario Scenario,
        UniqueIndexOutcome Outcome,
        ImmutableArray<string> Rationales);

    internal sealed record UniqueIndexOutcome(bool EnforceUnique, RemediationDirective Remediation);

    internal enum RemediationDirective
    {
        None,
        Always,
        WhenEvidenceMissing
    }

    internal enum ForeignKeyPolicyScenario
    {
        IgnoreRule,
        HasOrphan,
        ExistingConstraint,
        PolicyDisabled,
        CrossSchemaBlocked,
        CrossCatalogBlocked,
        Eligible
    }

    internal sealed record ForeignKeyMatrix(
        ImmutableArray<ForeignKeyPolicyDefinition> Definitions,
        ImmutableDictionary<ForeignKeyPolicyScenario, ForeignKeyPolicyDefinition> Lookup)
    {
        public ForeignKeyPolicyDefinition Resolve(ForeignKeyPolicyScenario scenario)
        {
            if (!Lookup.TryGetValue(scenario, out var definition))
            {
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Foreign key scenario not defined.");
            }

            return definition;
        }

        public static ForeignKeyMatrix Create()
        {
            var definitions = ImmutableArray.Create(
                new ForeignKeyPolicyDefinition(
                    Scenario: ForeignKeyPolicyScenario.IgnoreRule,
                    Description: "Delete rule configured as Ignore",
                    DeleteRuleIsIgnore: true,
                    HasOrphans: false,
                    HasExistingConstraint: false,
                    CrossSchema: false,
                    CrossCatalog: false,
                    EnableCreation: true,
                    AllowCrossSchema: true,
                    AllowCrossCatalog: true,
                    ExpectCreate: false,
                    Rationales: ImmutableArray.Create(TighteningRationales.DeleteRuleIgnore)),
                new ForeignKeyPolicyDefinition(
                    Scenario: ForeignKeyPolicyScenario.HasOrphan,
                    Description: "Profiling detected orphan rows",
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
                new ForeignKeyPolicyDefinition(
                    Scenario: ForeignKeyPolicyScenario.ExistingConstraint,
                    Description: "Database already enforces the foreign key",
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
                new ForeignKeyPolicyDefinition(
                    Scenario: ForeignKeyPolicyScenario.PolicyDisabled,
                    Description: "Creation disabled via tightening options",
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
                new ForeignKeyPolicyDefinition(
                    Scenario: ForeignKeyPolicyScenario.CrossSchemaBlocked,
                    Description: "Target schema differs and cross-schema creation is disabled",
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
                new ForeignKeyPolicyDefinition(
                    Scenario: ForeignKeyPolicyScenario.CrossCatalogBlocked,
                    Description: "Target catalog differs and cross-catalog creation is disabled",
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
                new ForeignKeyPolicyDefinition(
                    Scenario: ForeignKeyPolicyScenario.Eligible,
                    Description: "No hazards detected; safe to create",
                    DeleteRuleIsIgnore: false,
                    HasOrphans: false,
                    HasExistingConstraint: false,
                    CrossSchema: false,
                    CrossCatalog: false,
                    EnableCreation: true,
                    AllowCrossSchema: true,
                    AllowCrossCatalog: true,
                    ExpectCreate: true,
                    Rationales: ImmutableArray.Create(TighteningRationales.PolicyEnableCreation)))
            ;

            var lookup = definitions.ToImmutableDictionary(static definition => definition.Scenario);
            return new ForeignKeyMatrix(definitions, lookup);
        }
    }

    internal sealed record ForeignKeyPolicyDefinition(
        ForeignKeyPolicyScenario Scenario,
        string Description,
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
