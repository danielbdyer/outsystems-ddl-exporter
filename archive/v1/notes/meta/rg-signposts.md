# Ripgrep Signposts

> Reusable `rg` snippets that jump straight to high-signal files. Paste these into the shell instead of crafting ad-hoc searches during each task.

## Policy & Toggle Surface

| Goal | Command | Notes |
| --- | --- | --- |
| Inspect tightening toggle names & exports | `rg -n "TighteningToggleKeys" -n src/Osm.Validation` | Lands on `TighteningToggleSnapshot.cs`, which contains every key plus the `ToExportDictionary` logic. |
| Find policy mode usage inside CLI | `rg -n "PolicyMode" src/Osm.Cli src/Osm.Pipeline tests/Osm.Cli.Tests` | Shows where CLI arguments, pipeline logging, and tests reference the current policy mode. |
| Review `TighteningOptions` defaults | `rg -n "class TighteningOptions" -n src/Osm.Domain/Configuration` | Jump to the canonical default values used in toggle snapshots and CLI baselines. |

## SMO / Emission

| Goal | Command | Notes |
| --- | --- | --- |
| Locate SMO factory entry points | `rg -n "SmoModelFactory" -n` | Surfaces the factory implementation plus the SMO tests that validate propagation. |
| Track constraint naming changes | `rg -n "ConstraintNameNormalizer" -n` | Helpful when adjusting FK naming strategy; pairs nicely with failing tests such as `ConstraintNameNormalizerTests`. |
| Check ScriptDom consumers | `rg -n "ScriptDom" src/Osm.Dmm src/Osm.Smo` | Finds all ScriptDom-related helpers to avoid accidental raw SQL concatenation. |

## Evidence Extraction & Caching

| Goal | Command | Notes |
| --- | --- | --- |
| Audit evidence cache behavior | `rg -n "EvidenceCache" src/Osm.Pipeline -g '*.cs'` | Targets orchestration steps plus manifest writers that need cache metadata updates. |
| Follow extraction path | `rg -n "ExtractModelPipeline" -n` | Reveals request/response DTOs and orchestrators for the extraction stage. |
| Discover profiling mocks | `rg -n "profileMock" -g '*.cs'` | Useful when toggling between live profilers and deterministic fixtures. |

## CLI & Operator Experience

| Goal | Command | Notes |
| --- | --- | --- |
| List available verbs | `rg -n "CommandFactory" src/Osm.Cli/Commands` | Each factory wires a verb (e.g., `BuildSsdt`, `Policy`) and shows option parsing. |
| Inspect manifest emission | `rg -n "FullExportRunManifest" -n` | Maps CLI outputs to pipeline metadata, ensuring docs/tests stay aligned. |
| Find telemetry writers | `rg -n "LogWriter" src/Osm.Pipeline/Orchestration` | Helpful for hooking new observability data without missing mandatory interfaces. |

Keep this file close whenever you need a precise grep incantation; copy/paste beats guessing filenames.
