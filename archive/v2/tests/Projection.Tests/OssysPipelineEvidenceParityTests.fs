module Projection.Tests.OssysPipelineEvidenceParityTests

// V1 parity audit — slice 5.4.α + 5.4.ε + 5.4.ζ (combined: V1
// ValidationFinding + Pipeline/Evidence + Pipeline/Application +
// Mediation). Reserves matrix rows 166-173. Closes Section B audits.

open Xunit

[<Fact(Skip = "Matrix row 166 — 🟢 PARITY. V1 `Osm.Validation/Tightening/Validations/ValidationFinding.cs` (record carrying OpportunityType + Title + Summary + Evidence[] + Rationales[] + Column? + Index? + Schema + Table + ConstraintName + Columns[]). V1 `ValidationReport.cs` bundles findings + TypeCounts map + GeneratedAtUtc. V2 `Diagnostics.DiagnosticEntry` (Source + Severity + Code + Message + SsKey? + Metadata map) + `LineageDiagnostics<'a>` writer monad. Covered by prior slice 5.4.γ.opportunities row 77 (Opportunity → DiagnosticEntry projection). V1 type-count rollups computed post-hoc from entry stream; V2 manifest emitter handles aggregation.")>]
let ``5.4.αεζ row 166: V1 ValidationFinding + ValidationReport ↔ V2 DiagnosticEntry + LineageDiagnostics PARITY (covered by row 77)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 166"

[<Fact(Skip = "Matrix row 167 — 🟠 NOT-MAPPED. V1 `Osm.Pipeline/Evidence/EvidenceArtifactType.cs` enum (Model / Profile / Dmm / Configuration) + `EvidenceArtifactDescriptor.cs` (Type + SourcePath + Hash + Length + Extension) + `EvidenceCacheModels.cs` (8 types: EvidenceCacheArtifact + EvidenceCacheManifest + EvidenceCacheResult + EvidenceCacheModuleSelection + EvidenceCacheOutcome + EvidenceCacheInvalidationReason + EvidenceCacheEvaluation + EvidenceCacheRequest; ~90 LOC) — coherent caching subsystem with 9-variant invalidation enum. V2 has NO equivalent caching layer in Core; canonical output is `seq<Statement>` (Π); cache management is realization-layer policy. **Already partially covered by matrix row 135** (slice 5.6.α.orchestration EvidenceCacheCoordinator). **Cash-out**: if V2 future-proofs for multi-source ingestion (Catalog from SQL + JSON + DACPAC), artifact metadata returns as adapter-layer concern.")>]
let ``5.4.αεζ row 167: V1 EvidenceArtifactType + Descriptor + 8 cache-model types orthogonal to V2 stream-realization (covered by row 135)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 167"

[<Fact(Skip = "Matrix row 168 — 🟠 NOT-MAPPED. V1 `Osm.Pipeline/Evidence/EvidenceCacheService.cs` (facade orchestrating CacheRequestNormalizer + ManifestEvaluator + CacheEntryCreator); `IEvidenceCacheService` interface (`CacheAsync(request, ct) -> Task<Result<EvidenceCacheResult>>`); `ManifestEvaluator.EvaluateAsync` (9 validation checks: version + key + command + expiry + module-selection + metadata + artifacts + etc.); `EvidenceCacheWriter.WriteAsync` (copies artifacts + versioning + TTL policy + manifest JSON serialization). V2 orchestration in `Projection.Pipeline.Compose` is for pass-pipeline + Π realization (different responsibility). **Future**: if V2 adopts two-tier orchestration (skeleton pass → optional caching → Π realization), this service pattern recurs. Per matrix row 135 trigger.")>]
let ``5.4.αεζ row 168: V1 EvidenceCacheService + ManifestEvaluator + EvidenceCacheWriter (orchestration cluster) lift to V2 future caching tier`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 168"

[<Fact(Skip = "Matrix row 169 — 🟡 DIVERGENCE. V1 `Osm.Pipeline/Application/IApplicationService<TInput, TResult>` interface (`Task<Result<'output>> RunAsync(TInput, CancellationToken)`). V2 equivalent: each pass is pure function `Catalog -> Policy -> Profile -> Lineage<'output>` (or `Lineage<Diagnostics<'output>>`); orchestration in `Projection.Pipeline.Compose` is functional composition, not interface dispatch. The **contract exists** (per the canonical pass signature) but lives in type annotations, not interface definitions. Per `DECISIONS 2026-05-16 (later) — V2 self-containment` (V2 avoids interface-heavy dispatch; object expressions deferred). No DECISIONS row needed — covered.")>]
let ``5.4.αεζ row 169: V1 IApplicationService interface vs V2 typed-function pass signatures (V2 avoids interface dispatch)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 169"

[<Fact(Skip = "Matrix row 170 — 🟠 NOT-MAPPED (gated). V1 concrete ApplicationService implementations: `AnalyzeApplicationService` + `ExtractModelApplicationService` + `BuildSsdtApplicationService` + `FullExportApplicationService` + `CaptureProfileApplicationService` + `CompareWithDmmApplicationService` + ... (~7 services, each 50-250 LOC); bridge CLI args → pipeline orchestration → result assembly. V2 equivalent responsibility lands in host layer (CLI command handlers; not yet written; per slice 5.7.α.cli matrix rows 105-119). Per R6 split-brain governance — V2 emits-but-doesn't-ship during dual-track; V1 owns production write path. V2 V1-parity CLI deferred post-cutover. **Already named per slice 5.7.α.cli** — when V2 operator-facing CLI ships (chapter 5+), each service pattern recurs.")>]
let ``5.4.αεζ row 170: V1 ~7 ApplicationService implementations lift to V2 chapter 5+ CLI command handlers (per slice 5.7.α.cli)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 170"

[<Fact(Skip = "Matrix row 171 — 🟡 DIVERGENCE. V1 `Osm.Pipeline/Mediation/CommandDispatcher.cs` + `ICommand` / `ICommandHandler` interfaces (MediatR-style command pattern; 2 files, ~80 LOC). V2 has NO command-dispatcher. Equivalent: per-pass module + pass driver; composition via `Composition.fanOut` (functional). V2 avoids interface-heavy dispatch per F# object-expressions deferral + sibling-wrapper discipline (DECISIONS 2026-05-17). Pass drivers are pure functions; composition explicit in Compose.fs. Per **conscious-omission rationale** (object expressions deferred until interface-based polymorphism is needed); discipline forbids command-dispatcher pattern on principle. **If revisited**: future host shell (web server, plugin architecture) might re-introduce; would be host-layer concern not Core.")>]
let ``5.4.αεζ row 171: V1 MediatR CommandDispatcher vs V2 functional composition (V2 conscious omission of interface dispatch)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 171"

[<Fact(Skip = "Matrix row 172 — 🟡 DIVERGENCE. V1 `Osm.Pipeline/Application/PipelineRequestContextBuilder.cs` + `PipelineRequestContext` (configuration assembly + context object carrying tightening options + module filter + SQL options + caching overrides + metadata logger + flush function; ~250 LOC). V2 `Projection.Pipeline.Compose` performs equivalent assembly (configuration merging + pass-parameter binding); carries configuration in each pass's explicit parameters, not a context object. Builder pattern for configuration assembly recurs in V2 (CacheMetadataBuilder, EvidenceCacheOptionsFactory) but context object avoided. Per F#-pure-core / no-I/O-in-Core load-bearing commitment.")>]
let ``5.4.αεζ row 172: V1 PipelineRequestContextBuilder + context object vs V2 explicit-parameter passes (V2 pure-core)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 172"

[<Fact(Skip = "Matrix row 173 — 🟢 PARITY (cross-reference). V1 evidence cache invalidation 9-variant enum (`ManifestMissing`, `ManifestInvalid`, `ManifestVersionMismatch`, `KeyMismatch`, `CommandMismatch`, `ManifestExpired`, `ModuleSelectionChanged`, `MetadataMismatch`, `ArtifactsMismatch`, `RefreshRequested`) is fine-grained decision carrier. V2's equivalent is spread across decision types per pass (NullabilityOutcome / UniqueIndexOutcome / ForeignKeyOutcome each with keep-reason enums); manifest validation distributed via per-pass integrity checks + Lineage trail validation + canary PhysicalSchema diff. **Candidate adoption**: if V2 CLI adds manifest-validation mode (audit trail verification), the closed-DU invalidation-reason pattern informs design.")>]
let ``5.4.αεζ row 173: V1 9-variant EvidenceCacheInvalidationReason ↔ V2 distributed keep-reason DUs across passes PARITY`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 173"

[<Fact>]
let ``5.4.αεζ: pipeline-evidence parity file present`` () =
    Assert.True(true)
