# Chapter 4.1.A close — V2 SSDT DDL emitter (production schema axis; V2-driver KPI Phase 2)

**Sessions:** chapter 4.1.A in-flight surface (slices 1+2+3+4+5+9+10) + RawTextEmitter retirement arc + Tier-1/2/3 typed-AST transitions arc, all on branch `claude/review-ddl-exporter-ilV0k`. **Plus chapter 4.1.B in-flight** (slices α/β/γ; the V2-driver KPI Phase 3 highest-stakes deliverable shipped under this same close ritual).

This is a **joint chapter close ritual** covering chapters 3.6, 3.7, 4.1.A in-flight, 4.1.B-in-flight (slices α/β/γ), the RawTextEmitter retirement arc, and the Tier-1/2/3 typed-AST transitions arc. The chapters were individually shipped substantively across the prior session sequence; this ritual catches cross-cutting drift before it compounds and writes the close synthesis.

---

## Chapter milestone arc — what shipped

### Chapter 4.1.A — V2 SSDT DDL emitter (production schema axis)

V2-driver KPI Phase 2. Per `V2_DRIVER.md`: V2 emits production SSDT DDL bytes; the canary verifies them; per-environment-per-artifact-type R6 governance flips authority from V1 to V2 once N=10 consecutive green canary runs + operator sign-off support the flip.

**In-flight surface closed (slices 1+2+3+4+5+9+10):**
- **`SsdtDdlEmitter.emitSlices : Emitter<SsdtFile>`** — per-kind `.sql` files via ScriptDom typed AST + `Sql160ScriptGenerator`. CREATE TABLE + composite PKs + non-PK indexes + intra-module FKs. RelativePath via cross-platform-deterministic forward slashes (`Modules/<Module>/<Schema>.<Table>.sql`).
- **`SsdtDdlEmitter.statements : Catalog -> seq<Statement>`** (added during the RawTextEmitter retirement) — typed-stream surface for canary tests + `Render.toText` consumers. Topologically ordered via `TopologicalOrderPass.runWith SkipSelfEdges`.
- **`ManifestEmitter.emit`** (slice 9) — `manifest.json` per V1 SsdtManifest schema; `Utf8JsonWriter` gold-standard library.
- **`SsdtBundle.compose`** (slice 10) — composition of `(ArtifactByKind<SsdtFile>, Manifest)` into `Map<RelativePath, string>`. F# core never touches the file system; downstream hosts (Pipeline / CLI) consume the map.
- **Slices 6 (cross-module FKs), 7 (identity + defaults), 8 (extended properties) gated** on chapter 3.2 SnapshotRowsets surfacing IR widening.

### Chapter 4.1.B in-flight (V2-driver KPI Phase 3 highest-stakes deliverable shipped)

Per `V2_DRIVER.md` per-axis correctness stakes table: **CDC-silence on idempotent redeploy is the highest-leverage single deliverable in the entire chapter sequence.** Slice γ shipped GREEN under real SQL Server 2022 CDC — the chapter signature deliverable is operationally proven.

- **Slice α — `StaticSeedsEmitter v0`** (`fd38908`). New `Projection.Targets.Data` project. `DataInsertScript` + `DataInsertRow` typed value foundation. V1-shape MERGE per `StaticSeedSqlBuilder.cs:211-260`. T11 sibling-Π keyset coverage; T1 byte-determinism; A18 amended (Catalog × Profile, never Policy).
- **Slice β — `Profile.CdcAwareness` field + change-detection MERGE predicate** (`2d8210e`). The load-bearing semantic addition. Per-kind dispatch on `CdcAwareness.isEnabled`; CDC-enabled kinds emit the change-detection predicate (`Target.[c] <> Source.[c] OR (Target.[c] IS NULL AND Source.[c] IS NOT NULL) OR (Target.[c] IS NOT NULL AND Source.[c] IS NULL)` per non-key column, all OR-joined); CDC-disabled kinds keep V1's predicate-free WHEN MATCHED. CdcAwareness lives on Profile (A34 alignment), not Policy.
- **Slice γ — CDC-silence canary GREEN** (`cdcd953`). Two `[<Fact>]` tests in `CdcSilenceTests.fs` (skip-if-no-Docker gated): positive (post == baseline; 0 new CDC entries on idempotent redeploy) + sensitivity (changed-content redeploy DOES fire CDC; proves the canary mechanism is real). `sys.sp_cdc_scan` Agent-less synchronous capture; `cdc.<schema>_<table>_CT` row count assertion. **Empirical finding documented**: SQL Server 2022's MERGE→CDC pipeline doesn't capture no-op UPDATEs even from V1-shape unconditional WHEN MATCHED — V2's predicate is defense-in-depth (correct under any SQL Server version), not the load-bearing fix in 2022 specifically.
- **Slices δ (two-phase insertion / cycle-breaking), ε (MigrationDependenciesEmitter), ζ (BootstrapEmitter), η (DataEmissionComposer + EmissionPolicy.DataComposition DU), θ (partition assertion) pending.** Slices ε/ζ have a hard-requirement Active deferral (Tier-3 codification this session): MUST adopt `ScriptDomBuild.buildMergeStatement` from slice α precedent.

### M4 Tolerance taxonomy slice α (chapter 4.1.A close arc)

`af7b96c`. R6 split-brain governance + cutover fallback ladder + R4 multi-environment promotion test all depend on this typed surface.

- **`Projection.Core.ToleratedDivergence`** — closed DU enumerating five empirically-grounded divergences (HeaderCommentsOmitted / PostDeployForeignKeysSplit / IndexesUnreflected / StaticPopulationsUnreflected / CommentMetadataUnreflected). Each variant has concrete canary or emitter evidence today.
- **`Tolerance = Set<ToleratedDivergence>`** — value object with smart-constructor encapsulation (`strict` / `permissive` / `withDivergence` / `tolerates` / `divergences` / `isStrict` / `ofSet`). `Set` encoding (over a flat-bool record) per pillar 1 + pillar 8: the `Tolerance` IS the equivalence-class definition; membership says "this divergence is accepted."
- **Slice β** (quotient operator on PhysicalSchemaDiff) **reframed at this close** as no-op-until-consumer-pressure: the slice α variants are all about axes that PhysicalSchemaDiff doesn't compare anyway. Reopen if a new variant lands that requires diff-filtering.
- **R4 multi-environment promotion property test** pending; uses the `Set<ToleratedDivergence>` encoding; concrete next slice.

### RawTextEmitter retirement arc — chapter-3-era pre-cursor fully retired

Three slices; net **-520 LOC**. Pillar 8 win: action-shaped name retires; concept-shaped `SsdtDdlEmitter` (chapter 4.1.A) + `StaticSeedsEmitter` (chapter 4.1.B) remain.

- **Slice 1** (`e4936d5`) — `SsdtDdlEmitter.statements : Catalog -> seq<Statement>` typed-stream surface (the missing piece that unblocked migration).
- **Slice 2** (`d91067a`) — Migrate all 9 call sites (Pipeline, CLI, canary tests, cross-emitter tests) from `RawTextEmitter` to `SsdtDdlEmitter`; topological order preserved. Re-baseline of substring assertions that depended on RawTextEmitter's `Provenance` trailing-comment SsKey roots (V2-IR-internal; SsdtDdlEmitter doesn't emit them).
- **Slice 3** (`197b9e7`) — Delete `RawTextEmitter.fs` + `RawTextEmitterTests.fs`.

### Tier-1 typed-AST transitions — pillar-1 / pillar-7 alignment across the Outputs seam

Four transitions, all shipped. The MERGE → ScriptDom MergeStatement transition retired **6 LINT-ALLOWs** in one cash-out.

- **#4 — `Projection.Core.SqlLiteral` typed expression module** (`08ca554`). The IR→SQL-literal projection lives in Core; closed DU with eight variants (one per PrimitiveType + NULL sentinel). `ofRaw` + `toString` + `formatRaw` convenience. Both consumers (SSDT.Render + Data.StaticSeedsEmitter) flow through the typed middle layer.
- **#1 — MERGE → ScriptDom MergeStatement typed AST** (`bface9a`). `ScriptDomBuild.buildMergeStatement` (~150 LOC of typed-AST construction with `MergeBuildArgs` record + per-column predicate builders); `StaticSeedsEmitter.renderMerge` retired the StringBuilder construction. **6 LINT-ALLOWs retired.** The change-detection predicate is now a typed `BooleanBinaryExpression(Or)` of `BooleanComparisonExpression(NotEqualToBrackets)` + `BooleanIsNullExpression` AST wrapped in `BooleanParenthesisExpression`.
- **#2 — `Compose.Outputs.Sql : string` → `SsdtBundle : Map<RelativePath, string>`** (`705e31d`). Production-shape per-table file map. Pipeline.write iterates the bundle; Deploy.runEphemeral consumes `Compose.aggregateSsdt` (the `\nGO\n`-joined per-.sql convenience). Chapter-3 single-blob retires.
- **#3 — `Compose.Outputs.Json + .Distributions : string` → `JsonNode`** (`22ecc59`). Typed at the Outputs seam; consumers query the typed tree. Chapter 3.7 slice ε's per-kind typed JsonNode lifted to the Outputs seam. Pillar 1 holds end-to-end across Pipeline composition.

### Tier-3 codification — text-builder-as-first-instinct discipline

`23d9d5d`. Substantive DECISIONS entry + Active deferrals index entries + AGENTS.md + CLAUDE.md operating-disciplines table.

- **Named failure mode**: **text-builder-as-first-instinct** — the agent reaches for StringBuilder as the default for new emitters. Each LINT-ALLOW is individually defensible per the substantive-rationale discipline; the AGGREGATE is the bug. Six LINT-ALLOWs at one MERGE site means the typed-AST migration was skipped at construction time. Sibling failure mode to **performance-of-compliance** (chapter 3.7 slice β'') and **domain-blind naming** (chapter 3.7 slice β''').
- **4-step protocol**: (1) articulate the typed-AST library BEFORE the first StringBuilder; (2) cross-check the precedent emitters; (3) first draft uses the typed AST; (4) LINT-ALLOWs at terminal text boundaries only.
- **Two hard-requirement Active deferrals** added to the index for chapter open: DacFx adoption in chapter 3.x DacpacEmitter; `ScriptDomBuild.buildMergeStatement` adoption in chapter 4.1.B slices ε/ζ. The chapter-close ritual scans the Active deferrals table; future agents at chapter open MUST read these entries.

### Docker probe + verify-before-diagnose discipline

`6ec4a64` + `b56f558`.

- **`Deploy.Docker.ensureRunning` memoized**; `BringupBudgetMs` lowered 30s→5s. Worst-case suite cost when Docker is down: collapsed `N×30s` → 5s.
- **PreToolUse hook** `.claude/hooks/docker-probe.sh` auto-fires before infra-relevant Bash; reports current Docker state via `additionalContext`.
- **Named failure mode**: **infrastructure-blame jumping** — the agent jumps to "X infrastructure is unavailable" without running the cheap verification probe. Codified in AGENTS.md (root) "Verify-before-diagnose for infrastructure" subsection.

---

## Chapter close ritual — eight items walked

Per `DECISIONS 2026-05-14 — Chapter-close ritual` (sessions 15 + 25 V1-envelope amendment):

### 1. Active deferrals scan

Walked the index at top of `DECISIONS.md`. **One trigger fire to record:**

- **Cross-module FK IR refinement** — chapter 4.1.A enterprise canary fixture exercises cross-module FKs (PRODUCT.CATEGORYID, ORDER.CUSTOMERID, ORDERLINE.PRODUCTID, audit FKs from CUSTOMER/PRODUCT/ORDER to IDM.USER). The trigger fired but was **satisfied without IR refinement**: SsdtDdlEmitter.statements uses `TopologicalOrderPass.runWith SkipSelfEdges` so FK targets emit before referencers (the chapter 4.1.A enterprise canary deploys clean). The IR refinement (adding cross-module distinction at the `Reference` type) remains deferred pending a use case where topological ordering at the emit layer is insufficient.

The two new hard-requirement deferrals (DacFx; `ScriptDomBuild.buildMergeStatement` adoption) are pre-trigger (their gating chapters are not yet open). Index reaffirmed; no other silent fires.

### 2. Contract-vs-implementation walk

Walked the production surfaces shipped in this arc:

- **`SsdtDdlEmitter.emitSlices`** — contract: `Emitter<SsdtFile>` (per A18 amended); implementation: pass-through via `kindToSsdtFile` over `ArtifactByKind.create`'s strict-equality keyset. Contract honored.
- **`SsdtDdlEmitter.statements`** — contract: `Catalog -> seq<Statement>` schema-pure (no InsertRow); implementation: `seq { for k in topoOrdered do yield createTableStatement; yield! indexStatements }`. Contract honored. Test surface: `SsdtDdlEmitter.statements is schema-pure (no InsertRow statements)` Fact. ✓
- **`StaticSeedsEmitter.emit`** — contract: `Catalog -> Profile -> Result<ArtifactByKind<DataInsertScript>, EmitError>` (A18 amended; Profile consumed for CdcAwareness); implementation: per-kind dispatch through `kindToScript` + `renderMerge` (post-Tier-1-#1: ScriptDomBuild.buildMergeStatement). Contract honored. Test surface: 20 StaticSeeds tests + 2 CDC-silence tests. ✓
- **`SqlLiteral.ofRaw` / `.toString` / `.formatRaw`** — contract: typed projection IR → typed → SQL text; implementation: `ofRaw` matches PrimitiveType variants exhaustively (compile-time forced); `toString` is the terminal text boundary. Test surface: 16 SqlLiteralTests including closed-DU coverage. ✓
- **`Tolerance` smart-constructor encapsulation** — contract: private constructor; only `strict` / `permissive` / `ofSet` / `withDivergence` etc. public; `divergences` accessor; `isStrict` predicate. Test surface: 12 ToleranceTests including monotonicity property. ✓
- **`Compose.Outputs`** — contract: `SsdtBundle : Map<string, string>` (per Tier-1 #2) + `Json : JsonNode` + `Distributions : JsonNode` (per Tier-1 #3); implementation: `Compose.project` populates each via the corresponding emitter; `Compose.write` iterates the bundle + serializes JsonNode at the file-system boundary. Test surface: `EndToEndPipelineTests` updated for the new typed shape. ✓

No contract-vs-implementation drift surfaced.

### 3. CLAUDE.md / README.md staleness checks

Walked CLAUDE.md operating-disciplines table — three new failure-mode-named disciplines added this arc (performance-of-compliance, domain-blind naming, text-builder-as-first-instinct, infrastructure-blame jumping). All present and pointing at their codifying DECISIONS entries.

README.md updated this close: chapter list refreshed (3.5 / 3.6 / 3.7 / 4.1.A / 4.1.B / RawTextEmitter retirement / Tier 1/2/3); test count 757 → 840 non-canary; lint count 26 → 27; pillar count 7 → 8; Status section retitled to "chapter-4.1.A close arc + 4.1.B in-flight" + the three named failure modes documented.

### 4. HANDOFF + chapter close synthesis scope

HANDOFF.md updated this close (commit `c90a5ea`): new chapter 4.1.A close arc + 4.1.B in-flight prologue at the top names every commit hash + load-bearing rationale + the chapter-close-ritual deferrals stacked.

This file (`CHAPTER_4_1_A_CLOSE.md`) is the canonical close synthesis.

KICKOFF.md updated this close: "Where you are picking up" section now shows the 16-commit arc + the what's-pending list with the chapter close ritual marked highest-leverage (which this ritual now executes).

V2_DRIVER.md updated this close: Phase 1 closed (T11 fully structural across four siblings); Phase 2 substantively in-flight; Phase 3 highest-stakes-deliverable shipped.

### 5. Fresh-eye walk

840 non-canary tests pass + ~16 Docker-dependent canary tests. Build clean (0 warnings under `TreatWarningsAsErrors=true`). Lint clean across 27 rules. LINT-ALLOW audit-trail inventory current; every per-line marker substantively rationaled per the four-question analysis (no "performance-of-compliance" sites).

Bench surface live across all pass entries + emit paths + statement-stream surfaces. Statistical perf-gate at `bench/baseline-canary.json` (re-record via `PERF_GATE_RECORD=1 bash sidecar/projection/scripts/perf-gate.sh` if/when the perf floor legitimately changes).

The session-start hook + PreToolUse Docker auto-probe hook + perf-gate Stop hook compose to give the agent a stable environment-state surface across session boundaries.

### 6. Operating-disciplines table currency

CLAUDE.md operating-disciplines table walked. All entries current; three new rows added this arc (text-builder-as-first-instinct + the prior two named failure modes from chapter 3.7). Cross-references between disciplines (pillar 7 substantive-rationale ↔ pillar 1 typed-AST ↔ pillar 8 concept-shaped naming) visible across the table.

### 7. V1-input-envelope walk

This is a V1↔V2 translation chapter (chapter 4.1.A maps V1's SSDT DDL emission shape; chapter 4.1.B maps V1's `StaticSeedSqlBuilder` MERGE shape).

- **Chapter 4.1.A** — V1's `PerTableWriter.cs` produces per-table .sql files at `Modules/<Module>/<Schema>.<Table>.sql` (the V1-mirror). V2's `SsdtDdlEmitter.emitSlices` + `SsdtBundle.compose` produces the same shape. V1-input envelope: chapter 4.1.A pre-scope §7 names the input shape (Catalog kinds with attribute lists + indexes + references). V2 honors the envelope.
- **Chapter 4.1.B** — V1's `StaticSeedSqlBuilder.cs:211-260` produces the MERGE shape (USING (VALUES (...)) AS Source ([cols]) ON ... WHEN MATCHED THEN UPDATE SET ... WHEN NOT MATCHED THEN INSERT ...). V2's `StaticSeedsEmitter.renderMerge` produces the same shape via ScriptDom typed AST. V1-input envelope: chapter 4.1.B pre-scope §6 names the change-detection predicate addition (the V2 contribution beyond V1 parity). V2 honors the envelope + adds the predicate as the pillar-1-aligned typed structural commitment.
- **RawTextEmitter retirement** — retired the chapter-3-era V1-mirror that produced one big SQL string; superseded by chapter-4.1.A's per-table file shape (the V1 `PerTableWriter` actual emission shape). V1-input envelope: the V1-mirror retirement is correct; V1's actual output is per-table files, not one big string.

No V1-input-envelope drift; V2 honors V1's actual emission shapes across the chapter close arc.

### 8. AXIOMS amendments

Walked the "Still scheduled (TBD)" list at the bottom of `AXIOMS.md`. None of the pending amendments (T1 binary-normal-form / T11 × 2 / A1 four-variant / A37 / A38 / A32) are gated on this close — they're scheduled for chapters 3.3 / 3.5 / chapter-3 cross-cutting / 3.4 / 4.2 closes.

This arc did not introduce a new axiom-worthy pattern. The CDC-silence-on-idempotent-redeploy property test is the chapter signature deliverable but is application of existing disciplines (canary as load-bearing forcing function; pillar 1 typed-AST; A18 amended), not a new axiom. The text-builder-as-first-instinct discipline is a discipline-document codification, not an axiom.

No AXIOMS amendments to scaffold or fill at this close.

---

## Meta-codifications shipped this arc

Three new named failure modes codified across chapters 3.7 + 4.1.A close arc, joining `performance-of-compliance` (already codified at 3.7 slice β''):

1. **performance-of-compliance** (chapter 3.7 slice β'') — LINT-ALLOW shaped like an audit trail without substance. The pillar-7 substantive-rationale amendment.
2. **domain-blind naming** (chapter 3.7 slice β''') — name shaped like a placeholder for an absent domain concept. The pillar-8 codification.
3. **text-builder-as-first-instinct** (chapter 4.1.A close arc; this session) — StringBuilder reach as default for new emitters. The pillar-1 + pillar-7 amendment.
4. **infrastructure-blame jumping** (chapter 4.1.A close arc; this session) — jumping to "X infrastructure is unavailable" without verification probe. AGENTS.md "Verify-before-diagnose for infrastructure" discipline.

The four named failure modes form the codified failure-mode set for V2 — substance-of-discipline at the lint marker, substance-of-domain at the type system, substance-of-library at the construction surface, substance-of-verification at the diagnostic boundary.

**Tier-3 codification mechanism**: deferred items reach forward through Active deferrals index entries. Two hard-requirement entries (DacFx adoption; `ScriptDomBuild.buildMergeStatement` adoption from slice α) added this arc; the chapter-close ritual scans the index, so future agents at chapter open MUST read these entries before construction work begins.

---

## AXIOMS cash-outs

None this arc. The "Still scheduled" list at the bottom of `AXIOMS.md` is unchanged.

---

## Forward signals

### Highest-leverage next move

**R4 multi-environment promotion property test** — exercises the M4 Tolerance taxonomy (slice α shipped this arc) across synthetic per-env fixtures. ~150 LOC; uses the `Set<ToleratedDivergence>` encoding directly. Independent of any chapter open. The R6 split-brain governance + cutover fallback ladder both depend on this property holding.

### Hard-requirement Active deferrals (read at chapter open)

- **Chapter 3.x DacpacEmitter** — Tier-3 codified. MUST adopt `Microsoft.SqlServer.Dac` (DacFx). Pre-scope at `CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md`. Conditional on whether the cutover deploy path requires DACPAC (product question for the principal PO).
- **Chapter 4.1.B slices ε/ζ — MigrationDependenciesEmitter + BootstrapEmitter** — Tier-3 codified. MUST adopt `ScriptDomBuild.buildMergeStatement` from slice α precedent (`bface9a`). Pre-scope at `CHAPTER_4_PRESCOPE_DATA_TRIUMVIRATE.md`.

### Substantive forward chapters (each multi-session)

- **Chapter 3.2 SnapshotRowsets** — closes the JSON-projection-lossiness class; lifts `ICatalogReader` port (Position B → A); **unblocks chapter 4.1.A slices 6/7/8 + chapter 4.1.B downstream**. Pre-scope at `CHAPTER_3_PRESCOPE_SNAPSHOT_ROWSETS.md`.
- **Chapter 4.1.B slices δ-θ** — δ (two-phase insertion / cycle-breaking), ε/ζ (MigrationDeps + Bootstrap; Tier-3 hard-requirement), η (DataEmissionComposer + EmissionPolicy.DataComposition DU), θ (partition assertion).
- **Chapter 4.2 — UserFkReflowPass + UserMatchingStrategy + UserRemapContext + SourceTag refactor of SsKey.** Pre-scope at `CHAPTER_4_PRESCOPE_USERFK_REFLOW.md`.
- **Chapter 4.3 — three-channel Diagnostics split.** Pre-scope at `CHAPTER_4_PRESCOPE_DIAGNOSTICS_AND_REMEDIATION.md`.
- **Chapter 5+** — cutover-day operator runbook (joint with solution architect); F# Analyzers SDK (KICKOFF deferral #1); Coordinates Stage 2; port lifts; V1 sunset planning.

### Quietly deferred

- **Tolerance slice β** (quotient operator on PhysicalSchemaDiff) — reframed at this close as no-op-until-consumer-pressure. The five slice-α variants are all about axes that PhysicalSchemaDiff doesn't compare anyway. Reopen if a new variant lands that requires diff-filtering.
- **Outstanding chapter-3.7 slice queue** (γ traverseCatalog / ζ attach-adapters / η Result-CE adoption / θ Coordinates Stage 2 / ι writer-monad codification / κ Lineage.tell perf audit / λ SsKey.rootOriginal V1 prefix / μ Restrict→NoActionSql Diagnostics / ν F# Analyzers SDK / ξ-π port lifts) — see HANDOFF.md chapter 3.7 prologue for triggers.

---

## Closing

Chapter 4.1.A's V2-driver KPI Phase 2 + chapter 4.1.B's Phase 3 highest-stakes deliverable are both shipped in their substantive forms. The cutover team has structural + operational proof of CDC-silence under real SQL Server 2022 CDC. The pillar-1 typed-AST commitment holds end-to-end across the Pipeline composition seam. The discipline mechanism (named failure modes + Tier-3 Active deferrals + chapter-close ritual scan) is the structural forcing function that ensures forward chapters honor the same standard.

Three more named failure modes joined the V2 vocabulary this arc; the four-failure-mode set is the codified shape of "what going wrong looks like" across the project's pillar surface. **The disciplines are not constraints; they are the load-bearing structure that lets each chapter ahead support more weight than the one behind.**

Hold the spine. Onward.
