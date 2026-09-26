module Projection.Tests.OssysSsdtDataEmissionParityTests

// V1 parity audit — slice 5.5.β + 5.5.γ + 5.5.δ (combined: SSDT
// orchestration + static/dynamic data emission). Reserves matrix
// rows 156-165. Closes Section C audits.

open Xunit

[<Fact(Skip = "Matrix row 156 — 🟡 REDESIGN. V1 `Osm.Emission/{TableEmissionPlan,TableEmissionPlanner,TablePlanWriter,ITablePlanWriter}.cs` 3-phase pipeline: Planner → PlanWriter → Manifest (per-table emission plan + parallelism dispatch via semaphore-bounded writer). V2 emits `ArtifactByKind<SsdtFile>` (per-kind typed artifact map) directly from `SsdtDdlEmitter.emit` — no intermediate plan object; the 'plan' IS the Kind→SsdtFile mapping. Realization layer (`Render.toSsdtDirectory` + `Deploy.executeStream`) writes per-kind. Per A35/A36 — bulk-vs-incremental + parallelism are realization-layer policy.")>]
let ``5.5.βγδ row 156: V1 TableEmissionPlanner 3-phase pipeline vs V2 ArtifactByKind direct emission (V2 redesigned)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 156"

[<Fact(Skip = "Matrix row 157 — 🟢 PARITY. V1 `Osm.Emission/SsdtEmitter.cs` (~145 LOC) is monolithic orchestrator (planner → writer → manifest builder; directory setup + error handling). V2 splits into sibling Π's: `SsdtDdlEmitter` (schema DDL); `StaticSeedsEmitter` + `MigrationDependenciesEmitter` + `BootstrapEmitter` (data per A18 amended); `ManifestEmitter` (manifest). Per chapter 4.1.A close arc + matrix row 145 (slice 5.6.α.orchestration).")>]
let ``5.5.βγδ row 157: V1 monolithic SsdtEmitter ↔ V2 sibling Π chorus PARITY`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 157"

[<Fact(Skip = "Matrix row 158 — 🟢 PARITY. V1 `Osm.Emission/Seeds/EntityDependencySorter.cs` (~200+ LOC) is Kahn's algorithm + cycle detection (alphabetical fallback). V1 `EntityDependencyOrderingModeExtensions.cs` (~15 LOC) provides Alphabetical/Topological/JunctionDeferred mode utilities. V2 `Projection.Core/Passes/TopologicalOrderPass.fs` (~300 LOC) — Kahn at v1; Tarjan SCC at v2+; asymmetric-2-cycle resolver at v3+; self-loop detection at v4 (chapter 4.1.B slice δ); SelfLoopPolicy parameterization per A40 harmonization. V2's pass produces TopologicalOrder value per A32; emitters consume it.")>]
let ``5.5.βγδ row 158: V1 EntityDependencySorter (Kahn + alpha fallback) ↔ V2 TopologicalOrderPass (Kahn + Tarjan + SelfLoopPolicy) PARITY`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 158"

[<Fact(Skip = "Matrix row 159 — 🟢 PARITY. V1 `Osm.Emission/DynamicEntityInsertGenerator.cs` (~790 LOC) generates dynamic INSERT/MERGE for non-static runtime data with batch-size control + determinism. V2 dispatches via `DataEmissionComposer` → `StaticSeedsEmitter.emitWithTopo` (MERGE construction via ScriptDom typed AST per chapter 4.1.B slice α) → `Deploy.executeStream` realization. V2's shape: typed `MergeStatement` (not raw INSERT). Batch sizing is realization-layer concern per A36.")>]
let ``5.5.βγδ row 159: V1 DynamicEntityInsertGenerator (~790 LOC raw INSERT) ↔ V2 StaticSeedsEmitter typed MERGE PARITY (chapter 4.1.B slice α)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 159"

[<Fact(Skip = "Matrix row 160 — 🟢 PARITY (partial — global ordering deferred). V1 `Osm.Emission/PhasedDynamicEntityInsertGenerator.cs` (~150 LOC) — 2-phase cycle-breaking: Phase-1 INSERT with nullable FKs NULLed, Phase-2 UPDATE to populate. V2 `StaticSeedsEmitter` slice δ (chapter 4.1.B) carries the same logic: `deferredColumns` predicate + per-kind `Phase1Merges` + `Phase2Updates` in `DataInsertScript`. TopologicalOrderPass v4 supplies cycle membership. **Open item per slice η**: cross-emitter global phase ordering (Phase-1-ALL across StaticSeeds + Migration + Bootstrap, then Phase-2-ALL) NOT YET REIFIED — per-kind rendering current. **Trigger**: chapter 4.2+ migration-dependency emission at scale.")>]
let ``5.5.βγδ row 160: V1 PhasedDynamicEntityInsertGenerator ↔ V2 StaticSeedsEmitter Phase1/Phase2 split PARITY (global ordering deferred per slice η)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 160"

[<Fact(Skip = "Matrix row 161 — 🟢 PARITY. V1 `Osm.Emission/Seeds/EntitySeedDeterminizer.cs` (~40 LOC) sorts rows deterministically by PK columns. V2's contract: every emitter-consumable row source (static-population Modality, migration context, bootstrap profile attachment) lands pre-sorted by SsKey or explicit row order. No post-hoc `Normalize` step; determinism is construction property per CLAUDE.md `Determinism is constructed, not validated`. V1 `Osm.Emission/Seeds/StaticEntitySeedScriptGenerator.cs` (~95 LOC) + `StaticEntitySeedTemplateService.cs` (~80 LOC) orchestration fully fused into V2 emitter + composer pipeline.")>]
let ``5.5.βγδ row 161: V1 EntitySeedDeterminizer + StaticEntitySeedScriptGenerator + TemplateService ↔ V2 by-construction determinism + fused emission PARITY`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 161"

[<Fact(Skip = "Matrix row 162 — 🟠 NOT-MAPPED (partial — TopologicalOrderPass.MissingEdges shipped). V1 `Osm.Emission/Seeds/StaticSeedForeignKeyPreflight.cs` (~80 LOC) does full preflight: ordering correctness + orphan-row detection + cross-module references audit + remediation guidance. V2 partially addresses: `TopologicalOrderPass.MissingEdges` field reports FK targets not in catalog; cycle detection surfaces SCC members for deferred-column selection. **Full preflight NOT YET IMPLEMENTED**: orphan-row detection across modules + remediation guidance. **Cash-out**: chapter 4.2 slices γ+δ (UserFkReflowPass discovery phase) surfaces full cross-module FK audit + remediation paths.")>]
let ``5.5.βγδ row 162: V1 StaticSeedForeignKeyPreflight (full audit) lifts to V2 chapter 4.2 UserFkReflowPass discovery`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 162"

[<Fact(Skip = "Matrix row 163 — 🟢 PARITY. V1 `Osm.Emission/Seeds/StaticSeedSqlBuilder.cs` (~200+ LOC) MERGE statement construction: ON-clause + WHEN-NOT-MATCHED INSERT + WHEN-MATCHED UPDATE + drift detection. V2 `ScriptDomBuild.buildMergeStatement` (typed AST) + `StaticSeedsEmitter.renderMerge` (logic layer). Same MERGE shape; V2 uses typed ScriptDom AST per pillar 1 + 7. ScriptDomGenerate.generateOne renders byte-deterministic SQL. Drift-detection predicate preserved. Chapter 4.1.B slice α shipped.")>]
let ``5.5.βγδ row 163: V1 StaticSeedSqlBuilder ↔ V2 ScriptDomBuild.buildMergeStatement + StaticSeedsEmitter.renderMerge PARITY (typed AST)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 163"

[<Fact(Skip = "Matrix row 164 — 🟢 PARITY. V1 `Osm.Emission/Formatting/SqlIdentifierFormatter.cs` (~30 LOC) + `SqlLiteralFormatter.cs` (~150 LOC) — SQL identifier square-bracket quoting + ]] escape; SQL literal escaping (strings '' → '''', nulls, numeric formats, type-specific quoting). V2 `ScriptDomBuild.bracketed` (delegates escaping to ScriptDom's `Identifier(QuoteType.SquareBracket)`) + `ScriptDomBuild.buildSqlLiteral` + `Projection.Core/SqlLiteral.fs` typed IR (chapter 4.1.B slice κ pillar 1 lift; smart constructor `SqlLiteral.ofRaw : PrimitiveType -> string -> SqlLiteral` escapes on construction). No hand-rolled escaping in V2 emitters.")>]
let ``5.5.βγδ row 164: V1 SqlIdentifierFormatter + SqlLiteralFormatter ↔ V2 ScriptDomBuild.bracketed + SqlLiteral typed IR PARITY (pillar 1)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 164"

[<Fact(Skip = "Matrix row 165 — 🟠 NOT-MAPPED (partial — read-side shipped). V1 `src/Osm.Pipeline/StaticData/StaticEntityDataProviders.cs` (~400 LOC) — static-data source abstraction (fixture loader for JSON + SQL extractors). V1 `src/Osm.Pipeline/DynamicData/SqlDynamicEntityDataProvider.cs` (~500 LOC) — dynamic-data extraction (per-module SQL queries, row batching, telemetry). V2 partially ships: `Projection.Adapters.Sql/ReadSide.fs` (async streaming rows via AsyncStream<(Name * string) list>); `Projection.Pipeline/Bulk.fs` (bulk copier). **Fixture provider pattern (JSON → in-memory population) NOT YET INTEGRATED**; per-module orchestration + filtering happens at Pipeline level (chapter 4.1.B boundaries). **Cash-out trigger**: test-harness fixture loading for chapter 4.2+ isolation tests.")>]
let ``5.5.βγδ row 165: V1 Pipeline StaticData + DynamicData providers ↔ V2 ReadSide + Bulk PARTIAL (fixture provider deferred)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 165"

[<Fact>]
let ``5.5.βγδ: ssdt-data-emission parity file present`` () =
    Assert.True(true)
