# Tightening Toggle Surface

> Snapshot of feature flags/toggles defined in `TighteningToggleSnapshot`. Use this when documenting overrides, wiring CLI args, or mapping telemetry back to configuration.

## Key Artifacts

- **Definitions** – `src/Osm.Validation/Tightening/TighteningToggleSnapshot.cs` contains `TighteningToggleKeys` constants and the export dictionary.
- **Default values** – `src/Osm.Domain/Configuration/TighteningOptions.cs` exposes `TighteningOptions.Default` used by CLI baselines.
- **CLI resolution** – `src/Osm.Cli/Commands/*CommandFactory.cs` parse flags/env vars into `TighteningOptions`. Search for `WithToggles` helpers when adding new inputs.
- **Telemetry** – `src/Osm.Pipeline/Orchestration/PolicyDecisionLogWriter.cs` and manifest builders consume the exported dictionary for auditing.

## Toggle Reference Table

| Key | Meaning | Default source | Common touchpoints |
| --- | --- | --- | --- |
| `policy.mode` | Chooses `Cautious`, `EvidenceGated`, or `Aggressive` behavior. | `TighteningOptions.Policy.Mode`. | CLI `--policy-mode`, policy docs, telemetry rollups. |
| `policy.nullBudget` | Fractional budget for tolerated nulls before forcing NOT NULL. | `TighteningOptions.Policy.NullBudget`. | Policy engine tests for monotonic behavior. |
| `foreignKeys.enableCreation` | Enables FK emission overall. | `TighteningOptions.ForeignKeys.EnableCreation`. | `SmoModelFactory` decisions + CLI toggles. |
| `foreignKeys.allowCrossSchema` | Allows cross-schema FK creation. | `ForeignKeys.AllowCrossSchema`. | Guardrail §4 enforcement, tests for schema-limited modules. |
| `foreignKeys.allowCrossCatalog` | Allows cross-catalog FKs. | `ForeignKeys.AllowCrossCatalog`. | Evidence gating, manifest docs. |
| `foreignKeys.treatMissingDeleteRuleAsIgnore` | Treat missing delete rule metadata as `Ignore`. | `ForeignKeys.TreatMissingDeleteRuleAsIgnore`. | Policy matrix + telemetry rationale. |
| `foreignKeys.allowNoCheckCreation` | Permits `WITH NOCHECK` creation when evidence incomplete. | `ForeignKeys.AllowNoCheckCreation`. | Emission tests verifying `WITH CHECK`. |
| `uniqueness.enforceSingleColumn` | Turns on unique constraint enforcement for single-column indexes. | `Uniqueness.EnforceSingleColumnUnique`. | SMO builder, decision telemetry. |
| `uniqueness.enforceMultiColumn` | Same for multi-column indexes. | `Uniqueness.EnforceMultiColumnUnique`. | Mutation tests for ordinal shuffling. |
| `remediation.generatePreScripts` | Enables remediation-prep script emission. | `Remediation.GeneratePreScripts`. | CLI `--emit-remediation`, manifest packaging. |
| `remediation.maxRowsDefaultBackfill` | Cap for default-value remediation updates. | `Remediation.MaxRowsDefaultBackfill`. | Policy/perf guardrails, CLI docs. |
| `remediation.sentinels.numeric`/`text`/`date` | Sentinel literals for remediation. | `Remediation.Sentinels.*`. | Telemetry logs + CLI sample outputs. |
| `mocking.useProfileMockFolder` | Force deterministic profiling using fixtures. | `Mocking.UseProfileMockFolder`. | Tests, CLI `--profile-mock-folder`. |
| `mocking.profileMockFolder` | Path to the mock folder. | `Mocking.ProfileMockFolder`. | CLI/test harness configuration. |

## How to Extend

1. Define a new option in `TighteningOptions` (domain layer) with a sane default.
2. Add a matching constant to `TighteningToggleKeys` and wire it inside `TighteningToggleSnapshot.Create` and `ToExportDictionary`.
3. Surface the option through CLI/environment parsing (usually in `BuildSsdtCommandFactory`).
4. Add policy + telemetry coverage referencing the new key so downstream automation can detect overrides.

Keeping this table synchronized avoids spelunking through multiple files whenever we mention a toggle in discussions.
