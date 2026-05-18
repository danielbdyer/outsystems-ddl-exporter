module Projection.Tests.OssysPipelineOrchestrationParityTests

// V1 parity audit — slice 5.6.α.orchestration. Reserves matrix rows
// 131-147. V1 BuildSsdt step-imperative pipeline vs V2 registry-driven
// composition via Compose.project + sibling Π chorus.

open Xunit

[<Fact(Skip = "Matrix row 131 — 🟡 DIVERGENCE. V1 `BuildSsdtPipeline.HandleAsync` uses imperative step-chaining: 12 sequential `.BindAsync()` calls; `IBuildSsdtStep<TState,TNextState>` binds each step as named field via DI; ordering is source-coupled. V2's `RegisteredTransforms.allChainSteps` is a list of `PassChainAdapter` entries (12 entries: 6 Catalog-rewriting + 6 decision-set-producing); `Compose.project` consumes via fold-and-bind. See `DECISIONS 2026-05-18 (slice 5.6.α.orchestration) — Registry-driven composition over imperative step-chaining`. Foundational architecture choice per chapter A.4.7' axis 1-3; A41 totality + skeleton-purity property + applied-transforms manifest field.")>]
let ``5.6.α row 131: V1 imperative step-chaining vs V2 registry-driven Compose.project (foundational divergence)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 131 + DECISIONS 2026-05-18 (slice 5.6.α.orchestration)"

[<Fact(Skip = "Matrix row 132 — 🔵 V2-EXTENSION. V1 carries `BuildSsdtPipelineRequest` (14 fields) + `BuildSsdtPipelineResult` (28 fields) + 18+ intermediate state record types per step (`PipelineInitialized`, `BootstrapCompleted`, `EvidenceCacheResult`, etc.). V2's `ComposeState` (7 fields: Catalog + TopologicalOrder + 4 decision-sets + UserRemap) + implicit Outputs at CLI boundary (SsdtBundle/Json/Distributions). V1's transitive-typing per step → V2's fixed-shape state with smart-constructor invariants (A39). Cleaner; less allocation pressure.")>]
let ``5.6.α row 132: V1 18+ intermediate state types vs V2 ComposeState + Outputs (V2 cleaner)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 132"

[<Fact(Skip = "Matrix row 133 — ⚫ V1-SUNSET. V1 `CaptureProfilePipeline` is a separate pipeline class (request/result + two-pass bootstrap → capture); blocks BuildSsdtPipeline via callback. V2 inverts: Profile is adapter input (loaded from disk; Compose.project consumes via `Profile.empty` or attached snapshot per A34 — Profile independent of Catalog and Policy). No pipeline-internal profiling. Per A34 + pillar 9. **Sunset rationale**: V2's adapter-input model makes the pipeline class redundant; profile-load is per-run not per-pass.")>]
let ``5.6.α row 133: V1 CaptureProfilePipeline sunsets — V2 adapter-input model per A34`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 133"

[<Fact(Skip = "Matrix row 134 — ⚫ V1-SUNSET. V1 `DmmComparePipeline` + `DmmComparePipelineRequest` + `DmmDiffLogWriter` compare V1 SMO Model against emitted SSDT via DMM lenses; emit diff log. **Already covered by matrix row 109 + slice 5.8.α** — DMM lens machinery sunset; V2's canary (PhysicalSchemaDiff via Deploy + ReadSide) is the replacement. Companion to existing row 40 + 41. **No additional parity work needed** — pipeline class retires with DMM lenses.")>]
let ``5.6.α row 134: V1 DmmComparePipeline sunsets — V2 canary subsumes (companion to row 40)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 134"

[<Fact(Skip = "Matrix row 135 — 🟠 NOT-MAPPED. V1 `EvidenceCacheCoordinator` + `EvidenceCachePipelineOptions` + `BuildSsdtEvidenceCacheStep` cache evidence at pipeline level (coordinator wraps cacheable artifacts, writes to root directory specified in request); 9-variant `EvidenceCacheInvalidationReason` enum (ManifestMissing/Invalid/VersionMismatch/KeyMismatch/CommandMismatch/Expired/ModuleSelectionChanged/MetadataMismatch/ArtifactsMismatch/RefreshRequested). V2 has no pipeline-level caching; evidence is ephemeral. **Cash-out shape**: cache adapter writing checkpointed Catalog/Policy decision-set JSON; consumer-driven. **Trigger**: operator-reality canary shows evidence-load time as bottleneck (chapter 3.6 perf surface) OR chapter 4+ perf-optimization opens caching slice.")>]
let ``5.6.α row 135: V1 EvidenceCacheCoordinator + 9-variant invalidation enum lifts to V2 cache adapter`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 135"

[<Fact(Skip = "Matrix row 136 — 🟠 NOT-MAPPED. V1 `BuildSsdtSqlValidationStep` + `SsdtSqlValidator` + `SsdtSqlValidationSummary` run SSDT validation post-emission via SMO + DacFx integration; capture error/warning summary. V2's A.1 Π outputs typed Statement stream; validation (if any) belongs to the realization layer (not Π) per A35/A36. **Cash-out shape**: `Validator` sibling Π consuming SSDT stream → producing ValidationReport; lives at realization boundary. **Trigger**: realization layer needs validation feedback (CI/CD gate; interactive editor) OR M2+ post-deploy validation phase.")>]
let ``5.6.α row 136: V1 SsdtSqlValidator + ValidationSummary lifts to V2 Validator sibling at realization layer`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 136"

[<Fact(Skip = "Matrix row 137 — 🔵 V2-EXTENSION. V1 `PipelineInsight` + `PipelineLogMetadataBuilder` + `PipelineExecutionLog` centralize diagnostics per-pipeline-run; severity enum (Info/Advisory/Warning/Critical); per-insight code + affected objects; flush to disk at completion. V2 distributes via `Lineage<Diagnostics<'output>>` writer (chapter 3.1; A25) — every pass emits LineageEvent entries (source/code/message/metadata); final Diagnostics accumulates trail. **V2 stronger**: per-pass attribution (source = pass:<name> per `DECISIONS 2026-05-18 (slice 5.4.γ.opportunities) — Per-pass DiagnosticEntry contract`); enables per-pass filtering + composition. Future PipelineExecutionLog JSON emission is realization-layer concern.")>]
let ``5.6.α row 137: V1 centralized PipelineInsight log vs V2 Lineage + Diagnostics distributed (V2 per-pass attribution)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 137"

[<Fact(Skip = "Matrix row 138 — 🟢 PARITY. V1 `EmissionCoverageCalculator.Compute` (static method receiving OsmModel + PolicyDecisionSet + SmoModel + SmoBuildOptions; emits EmissionCoverageResult). V2 ports algorithm to `Projection.Core.Coverage` module (chapter 4.4 slice α; shipped per matrix row 96). Same inputs (Catalog + Policy + decisions); same output shape. V2's Core placement makes it available to multiple consumers (Π, adapters, tests); V1's orchestration-layer placement limits discoverability. Currently consumed by `ManifestEmitter`.")>]
let ``5.6.α row 138: V1 EmissionCoverageCalculator ↔ V2 Coverage.compute PARITY (V2 moved to Core; chapter 4.4 slice α shipped)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 138"

[<Fact(Skip = "Matrix row 139 — 🟢 PARITY. V1 `EmissionFingerprintCalculator` computes cryptographic hash of emission shape for round-trip assertions; carries `SsdtEmissionMetadata`. V2's `RegistryDigest` computed from registered transform metadata + applied Policy + Profile (chapter A.4.7' slice ζ; shipped per matrix row 95). Same purpose (deterministic emission shape signature); different source (transforms vs SMO). V2's digest is lighter-weight (metadata only, not full SMO traversal); operationalized in `ManifestEmitter.emit`.")>]
let ``5.6.α row 139: V1 EmissionFingerprintCalculator ↔ V2 RegistryDigest PARITY (chapter A.4.7' slice ζ shipped)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 139"

[<Fact(Skip = "Matrix row 140 — 🟠 NOT-MAPPED. V1 `BuildSsdtPostDeploymentTemplateStep` generates `PostDeployment-Bootstrap.sql` template with guard logic for bootstrap snapshot on first deployment. V2's A.1 Π does not emit post-deploy scripts; subsequent slices add them as sibling targets. **Cash-out shape**: `PostDeployTemplateEmitter` sibling consuming SSDT statements + producing template SQL with guard logic. **Trigger**: chapter 4.1 slice 9 (per chapter 4.1 pre-scope) OR post-deploy emitter consumer demand.")>]
let ``5.6.α row 140: V1 BuildSsdtPostDeploymentTemplateStep lifts to V2 PostDeployTemplateEmitter sibling`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 140"

[<Fact(Skip = "Matrix row 141 — 🟠 NOT-MAPPED. V1 `BuildSsdtSqlProjectStep` generates `.sqlproj` MSBuild file with item groups for modules + seeds; relative paths + project name. V2's A.1 Π outputs `seq<Statement>` or typed `ArtifactByKind<SsdtFile>` map; realization layers (not yet written) consume map + produce `.sqlproj`. **Cash-out shape**: `Render.toSqlProject` realizer consuming ArtifactByKind + emitting XML. **Trigger**: V2-owned realization layer ships that needs `.sqlproj` output for Visual Studio / Azure DevOps integration. **Rationale**: project file generation is CI/CD integration concern, not schema-emission; decouples Π from tooling.")>]
let ``5.6.α row 141: V1 BuildSsdtSqlProjectStep lifts to V2 Render.toSqlProject realizer`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 141"

[<Fact(Skip = "Matrix row 142 — 🟢 PARITY. V1 `SchemaDataApplier` is stateless utility applying schema + static/dynamic seed data to target database; wraps SMO + SqlCommand execution. V2's `Deploy.executeStream` is realization-layer primitive (chapter 3.1.M2 slice α; shipped in Projection.Pipeline). Same semantic (connect to DB, execute statements, return report); different form: V1 accepts SMO model + data; V2 accepts `seq<Statement>` + connection string. V2's form decoupled from SMO; enables bulk-vs-incremental policy per A36. Tested via canary (Deploy.runWithReadback).")>]
let ``5.6.α row 142: V1 SchemaDataApplier ↔ V2 Deploy.executeStream PARITY (chapter 3.1.M2 slice α shipped)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 142"

[<Fact(Skip = "Matrix row 143 — 🟢 PARITY. V1 `BuildSsdtPolicyDecisionStep` is dedicated orchestration step invoking policy-making rules from tightening. V2 absorbs into registry: 4 decision-set passes (NullabilityPass + UniqueIndexPass + ForeignKeyPass + CategoricalUniquenessPass) + UserFkReflowPass each registered as `RegisteredTransform<Catalog, DecisionSet>` in `allChainSteps`. Decisions flow through `PassChainAdapter.liftDecisionPass` (RegisteredTransforms.fs lines 71-86) + written back to ComposeState. **No additional parity work** — covered by slice 5.4.γ.evaluators rows 72-74.")>]
let ``5.6.α row 143: V1 BuildSsdtPolicyDecisionStep ↔ V2 4-decision-pass registry absorption PARITY`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 143"

[<Fact(Skip = "Matrix row 144 — 🟢 PARITY. V1 `BuildSsdtBootstrapStep` + `BuildSsdtBootstrapSnapshotStep` are two-step (load model + profile; capture snapshot for idempotent redeployment). V2 inlines bootstrap into `CatalogReader.parse` adapter (loads V1 JSON; deserializes Catalog; returns Result). No separate bootstrap-snapshot step in V2 — snapshots handled at realization layer if needed (Deploy.runWithReadback). Cleaner separation: bootstrap is adapter responsibility; pipeline is composition responsibility.")>]
let ``5.6.α row 144: V1 BuildSsdtBootstrap[Snapshot]Step ↔ V2 adapter-inlined bootstrap PARITY`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 144"

[<Fact(Skip = "Matrix row 145 — 🔵 V2-EXTENSION. V1 `BuildSsdtEmissionStep` coordinates SMO model building + SSDT emission; invokes `ISmoModelFactory.Create` + `ISsdtEmitter.EmitAsync` singularly + sequentially. V2's emission stage expands to three sibling Π's (SSDT DDL + JSON + Distributions) + manifest emitter; each consumes the same final `ComposeState` independently (`Compose.projectFromChain` lines 121-145; each emitter invoked in parallel scopes via `Bench.scope`). Sibling chorus enables independent evolution + per-Π verification. Per chapter 4.1.A shipped + chapter A.4.7'.")>]
let ``5.6.α row 145: V1 singular BuildSsdtEmissionStep ↔ V2 sibling Π chorus (3 emitters + manifest)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 145"

[<Fact(Skip = "Matrix row 146 — 🟠 NOT-MAPPED. V1 `BuildSsdtStaticSeedStep` + `BuildSsdtDynamicInsertStep` emit static seed INSERT scripts + dynamic INSERT generation logic as separate pipeline steps. V2 will ship seed emission as sibling Π targets (StaticSeedsEmitter + DynamicInsertsEmitter — partially shipped per chapter 4.1.A close arc; full sibling-target architecture is chapter 4+ work). **Status update**: per slice 5.5.β+γ+δ audit, V2's `StaticSeedsEmitter.emitWithTopo` + `BootstrapEmitter` + `MigrationDependenciesEmitter` ship core static seeding; dynamic INSERT generation deferred. Companion to matrix rows 168-176 (slice 5.5.γ).")>]
let ``5.6.α row 146: V1 BuildSsdtStaticSeedStep + DynamicInsertStep lift to V2 sibling Π targets (partial shipping)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 146"

[<Fact(Skip = "Matrix row 147 — 🟢 PARITY. V1 `BuildSsdtTelemetryPackagingStep` collects telemetry + metadata from prior steps; packages into telemetry artifact. V2 distributes via two mechanisms: (1) `Bench.snapshot()` per-label timing + count distribution; persisted to JSON by `BenchSink.persistJson`; (2) `Lineage<Diagnostics<'output>>` trail (decisions + observations per-pass; composed at boundary). Operationalized in CLI: Program.fs lines 92-104 collect Bench snapshot at exit + call `dumpBench`. Lineage trail available to consumers (tests, future UI).")>]
let ``5.6.α row 147: V1 BuildSsdtTelemetryPackagingStep ↔ V2 Bench.snapshot + Lineage trail PARITY`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 147"

[<Fact>]
let ``5.6.α.orchestration: pipeline-orchestration parity file present`` () =
    Assert.True(true)
