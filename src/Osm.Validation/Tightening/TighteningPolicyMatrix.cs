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

    internal sealed record NullabilityMatrix(
        ImmutableDictionary<TighteningMode, NullabilityModeDefinition> Modes,
        ImmutableHashSet<NullabilitySignalKey> ConditionalSignals)
    {
        public NullabilityModeDefinition GetMode(TighteningMode mode)
        {
            if (!Modes.TryGetValue(mode, out var definition))
            {
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported tightening mode.");
            }

            return definition;
        }

        public static NullabilityMatrix Create()
        {
            var definitions = new[]
            {
                new NullabilityModeDefinition(
                    TighteningMode.Cautious,
                    Code: "MODE_CAUTIOUS",
                    Description: "Cautious policy (S1 ∪ S2)",
                    CoreSignals: ImmutableArray.Create(
                        NullabilitySignalKey.PrimaryKey,
                        NullabilitySignalKey.Physical,
                        NullabilitySignalKey.Mandatory),
                    ConditionalGroup: null,
                    EvidenceEmbeddedInRoot: false),
                new NullabilityModeDefinition(
                    TighteningMode.EvidenceGated,
                    Code: "MODE_EVIDENCE_GATED",
                    Description: "Evidence gated policy",
                    CoreSignals: ImmutableArray.Create(
                        NullabilitySignalKey.PrimaryKey,
                        NullabilitySignalKey.Physical,
                        NullabilitySignalKey.Mandatory),
                    ConditionalGroup: new NullabilityConditionalGroup(
                        Code: "EVIDENCE_STRONG_SIGNALS",
                        Description: "Strong signals requiring evidence",
                        RequiresEvidence: true,
                        Signals: ImmutableArray.Create(
                            NullabilitySignalKey.ForeignKey,
                            NullabilitySignalKey.Unique)),
                    EvidenceEmbeddedInRoot: true),
                new NullabilityModeDefinition(
                    TighteningMode.Aggressive,
                    Code: "MODE_AGGRESSIVE",
                    Description: "Aggressive policy (S1 ∪ S2 ∪ S3 ∪ S4 ∪ S5)",
                    CoreSignals: ImmutableArray.Create(
                        NullabilitySignalKey.PrimaryKey,
                        NullabilitySignalKey.Physical,
                        NullabilitySignalKey.Mandatory),
                    ConditionalGroup: new NullabilityConditionalGroup(
                        Code: "AGGRESSIVE_STRONG_SIGNALS",
                        Description: "Strong signals tightened without profiling evidence",
                        RequiresEvidence: false,
                        Signals: ImmutableArray.Create(
                            NullabilitySignalKey.ForeignKey,
                            NullabilitySignalKey.Unique)),
                    EvidenceEmbeddedInRoot: false)
            };

            var modes = definitions.ToImmutableDictionary(static definition => definition.Mode);
            var conditionalSignals = definitions
                .Where(static definition => definition.ConditionalGroup is not null)
                .SelectMany(static definition => definition.ConditionalGroup!.Signals)
                .ToImmutableHashSet();

            return new NullabilityMatrix(modes, conditionalSignals);
        }
    }

    internal sealed record NullabilityModeDefinition(
        TighteningMode Mode,
        string Code,
        string Description,
        ImmutableArray<NullabilitySignalKey> CoreSignals,
        NullabilityConditionalGroup? ConditionalGroup,
        bool EvidenceEmbeddedInRoot);

    internal sealed record NullabilityConditionalGroup(
        string Code,
        string Description,
        bool RequiresEvidence,
        ImmutableArray<NullabilitySignalKey> Signals);

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
        ImmutableDictionary<(TighteningMode Mode, UniquePolicyScenario Scenario), UniqueIndexOutcome> Outcomes)
    {
        public UniqueIndexOutcome Resolve(TighteningMode mode, UniquePolicyScenario scenario)
        {
            if (!Outcomes.TryGetValue((mode, scenario), out var outcome))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(scenario),
                    scenario,
                    $"Scenario '{scenario}' is not defined for tightening mode '{mode}'.");
            }

            return outcome;
        }

        public static UniqueIndexMatrix Create()
        {
            var builder = ImmutableDictionary.CreateBuilder<(TighteningMode, UniquePolicyScenario), UniqueIndexOutcome>();

            void Add(TighteningMode mode, UniquePolicyScenario scenario, bool enforce, RemediationDirective remediation)
                => builder.Add((mode, scenario), new UniqueIndexOutcome(enforce, remediation));

            foreach (var mode in Enum.GetValues<TighteningMode>())
            {
                Add(mode, UniquePolicyScenario.PolicyDisabled, enforce: false, RemediationDirective.None);
                Add(mode, UniquePolicyScenario.PhysicalReality, enforce: true, RemediationDirective.None);
            }

            Add(TighteningMode.Cautious, UniquePolicyScenario.DuplicatesWithPhysicalReality, enforce: true, RemediationDirective.None);
            Add(TighteningMode.Cautious, UniquePolicyScenario.DuplicatesWithoutPhysicalReality, enforce: false, RemediationDirective.None);
            Add(TighteningMode.Cautious, UniquePolicyScenario.CleanWithEvidence, enforce: false, RemediationDirective.None);
            Add(TighteningMode.Cautious, UniquePolicyScenario.CleanWithoutEvidence, enforce: false, RemediationDirective.None);

            Add(TighteningMode.EvidenceGated, UniquePolicyScenario.DuplicatesWithPhysicalReality, enforce: true, RemediationDirective.None);
            Add(TighteningMode.EvidenceGated, UniquePolicyScenario.DuplicatesWithoutPhysicalReality, enforce: false, RemediationDirective.None);
            Add(TighteningMode.EvidenceGated, UniquePolicyScenario.CleanWithEvidence, enforce: true, RemediationDirective.None);
            Add(TighteningMode.EvidenceGated, UniquePolicyScenario.CleanWithoutEvidence, enforce: false, RemediationDirective.None);

            Add(TighteningMode.Aggressive, UniquePolicyScenario.DuplicatesWithPhysicalReality, enforce: true, RemediationDirective.Always);
            Add(TighteningMode.Aggressive, UniquePolicyScenario.DuplicatesWithoutPhysicalReality, enforce: true, RemediationDirective.Always);
            Add(TighteningMode.Aggressive, UniquePolicyScenario.CleanWithEvidence, enforce: true, RemediationDirective.None);
            Add(TighteningMode.Aggressive, UniquePolicyScenario.CleanWithoutEvidence, enforce: true, RemediationDirective.WhenEvidenceMissing);

            return new UniqueIndexMatrix(builder.ToImmutable());
        }
    }

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
        ImmutableDictionary<ForeignKeyPolicyScenario, ForeignKeyPolicyDefinition> Scenarios)
    {
        public ForeignKeyPolicyDefinition Resolve(ForeignKeyPolicyScenario scenario)
        {
            if (!Scenarios.TryGetValue(scenario, out var definition))
            {
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Foreign key scenario not defined.");
            }

            return definition;
        }

        public static ForeignKeyMatrix Create()
        {
            var builder = ImmutableDictionary.CreateBuilder<ForeignKeyPolicyScenario, ForeignKeyPolicyDefinition>();

            builder[ForeignKeyPolicyScenario.IgnoreRule] = new ForeignKeyPolicyDefinition(
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
                Rationales: ImmutableArray.Create(TighteningRationales.DeleteRuleIgnore));

            builder[ForeignKeyPolicyScenario.HasOrphan] = new ForeignKeyPolicyDefinition(
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
                Rationales: ImmutableArray.Create(TighteningRationales.DataHasOrphans));

            builder[ForeignKeyPolicyScenario.ExistingConstraint] = new ForeignKeyPolicyDefinition(
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
                Rationales: ImmutableArray.Create(TighteningRationales.DatabaseConstraintPresent));

            builder[ForeignKeyPolicyScenario.PolicyDisabled] = new ForeignKeyPolicyDefinition(
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
                Rationales: ImmutableArray.Create(TighteningRationales.ForeignKeyCreationDisabled));

            builder[ForeignKeyPolicyScenario.CrossSchemaBlocked] = new ForeignKeyPolicyDefinition(
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
                Rationales: ImmutableArray.Create(TighteningRationales.CrossSchema));

            builder[ForeignKeyPolicyScenario.CrossCatalogBlocked] = new ForeignKeyPolicyDefinition(
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
                Rationales: ImmutableArray.Create(TighteningRationales.CrossCatalog));

            builder[ForeignKeyPolicyScenario.Eligible] = new ForeignKeyPolicyDefinition(
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
                Rationales: ImmutableArray.Create(TighteningRationales.PolicyEnableCreation));

            return new ForeignKeyMatrix(builder.ToImmutable());
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
