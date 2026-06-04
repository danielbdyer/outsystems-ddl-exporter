# V2 — Kickoff Brief for Fresh Agents

You're picking up V2 work mid-stream. This brief gets you oriented in 5 minutes; the canonical surfaces it points to brief you in another hour. **Read this first.** Then read the strategic surfaces in the prescribed order. Then write code.

---

## ⭐ Read this before you do anything

**The user codified a supreme operating discipline at chapter 3.5 (2026-05-09)** that **supersedes most other intents for each and every session that follows.** It lives at the top of `DECISIONS.md` as five pillars:

1. **Data-structure-oriented over string-parsing** — typed values flow through; strings emerge ONLY at the absolute terminal BCL writer boundary (`Sql160ScriptGenerator`, `XmlWriter`, `Utf8JsonWriter`).
2. **Avoid string concatenation aggressively** — `sprintf`, `+`, `String.Format`, `String.Concat`, `String.Join`, interpolated strings, and even the V2-internal `StructuredString` builder are all flagged. Every site requires deep per-site analysis: built-in BCL method first, vendor SDK second, typed data structure third, `StructuredString` last (and only with rationale).
3. **Built-in obligation** — when a BCL or vendor SDK emits the structure, agents are *obliged* to use it. `Sql160ScriptGenerator` for T-SQL (production target SQL Server 2022 = compat 160; **not** Sql170ScriptGenerator, which is vNext preview). `XmlWriter` for XML. `Utf8JsonWriter` for JSON. `UuidV5` for RFC 4122 §4.3 GUIDs.
4. **Promised land of FP** — ≥95% pure functions; ≤5% isolated and tested exhaustively (property tests + parse-roundtrip + byte-determinism). Mutation reified at file level via `LINT-ALLOW-FILE-MUTATION`.
5. **Coding-style commitments preserved across sessions** — deep DDD, point-free composition, hexagonal architecture, hardcore FP (closed DUs, smart constructors, no `null`, monadic composition), OOP only at boundary code where BCL forces it, deep separation of concerns, verifiable + observable to the nth degree.

**Default to explicit acknowledgement of deviance.** The lint guardrail (`scripts/lint-discipline.sh`, 27 rules) is the structural enforcement. Every legitimate exception carries a `LINT-ALLOW: <rationale>` (per-line) or `LINT-ALLOW-FILE` / `LINT-ALLOW-FILE-MUTATION` (top-of-file) marker. Pre-commit hook + CI workflow are defense-in-depth.

**⭐ The apex vision is `NORTH_STAR.md` — read it first of the strategic surfaces.** The bullseye is the **Total Projection**: the adjunction `Ingest ∘ Project = id` made *total* (schema / data / identity / time / decision), *executable*, and *self-describing* — fidelity as a theorem the engine proves about itself. It supersedes `VISION.md`'s strategic frame. Where we are on the bullseye is machine-reported in `NORTH_STAR.matrix.generated.md` (run `scripts/matrix-status.sh`); the verifiability gate `scripts/verifiability-gate.sh` keeps the axiom coverage honest. **V2-driver mode (next paragraph) is the bullseye's first ring**, not the whole target.

**V2-driver as destination KPI (codified 2026-05-10 chapter 3.7 sidebar; principal-PO discussion).** The project's north star: V2 reaches V2-driver mode for the cutover (V2's emitted artifacts ARE what deploys to production environments) by being **provably correct on every axis V2 owns** — schema, data, identity, diagnostics, and any future sibling. Provable correctness = structural-type-level enforcement plus per-axis property tests, not aspirational discipline plus selective coverage. V2-augmented (V1 drives, V2 verifies in PR) is the gate; V2-driver is the destination. V1 stays warm through cutover+30 as fallback; V1 sunset begins after one full schema-evolution cycle on V2 emissions. Every chapter, every slice, every primitive design biases toward V2-driver. **The CDC-silence-on-idempotent-redeploy property test (chapter 4.1.B) is the highest-leverage single deliverable in the entire chapter sequence.** Read `V2_DRIVER.md` (the standalone codification; supersedes the implicit "V2-augmented as floor" framing in `DECISIONS 2026-05-22 — R6`) before opening any chapter or pre-scope. The `V2_DRIVER.md` "Executive backlog summary" is the operative chapter-by-chapter sequencing under this KPI; per-chapter operational detail lives in `CHAPTER_*_PRESCOPE_*.md`.

**Pillar 8 — Domain-first naming and ubiquitous-language consistency** (codified 2026-05-10 chapter 3.7 sidebar). Every named type / function / file / module / test in V2 MUST embody the four-question domain-naming analysis BEFORE the name is committed:

1. **What domain concept does this represent?** Articulate it in cutover-business terms (Entity, Espace, External Entity, RefactorLog, DACPAC, SsKey provenance, lineage event, schema-fidelity diff…). If you cannot articulate what the concept IS, you do not have a name yet.
2. **Does V2 already name this concept somewhere?** If yes — use the same name (ubiquitous-language consistency: same concept = same name across Core / Adapters / Targets / Pipeline / CLI). If no — pick a name that aligns with how domain experts (operators, DBAs, OutSystems platform docs, CDC documentation, SQL Server admin guides) name the concept.
3. **Is the proposed name concept-shaped or action-shaped?** Concept-shaped names ("what this IS") default for types, modules, files. Action-shaped names ("what this DOES") only when the verb names a *domain* operation — NOT a generic CS operation (process, handle, manage, run).
4. **Generic-suffix smell test.** Helper / Util / Manager / Service / Handler / Processor / Wrapper / Builder / Factory / Provider (when not BCL-mandated) stop the agent. Either find the concept (rename) or restructure.

**Domain-blind naming is the named failure mode**: a name shaped like a placeholder for the absent domain concept. The agent feels productive (a name exists; the code compiles; tests pass) without doing the domain-modeling work. The cutover stakes (300-table migration; four environments; active CDC dependencies; R6 governance; T-30 / T-15 fallback ladder) are the forcing function. **Verifiability rests on the V2 vocabulary mirroring the cutover vocabulary.** Operators reading V2 source must recognize their concepts. **No lint enforcement** — heuristic syntactic checks misfire on legitimate uses (`LineageBuffer` is concept-shaped despite the "Buffer" suffix). The discipline-document path catches what the heuristic can't. See `DECISIONS 2026-05-10 — Domain-first naming and ubiquitous-language consistency` for the full counterfactual + worked precedents (`Catalog`, `SsKey`, `SqlTypeCorrespondence`, `BatchSplitter`, `RemovalReason`, `AnnotationDetail`, `SiblingEmitterContractTests`). See `PLAYBOOK.md` decision tree "When you reach for a name" for the executable form.

**Pillar 7's substantive-rationale amendment** (codified 2026-05-10 chapter 3.7 sidebar; named after the slice-β failure mode). Every per-line `LINT-ALLOW` marker on a string-composition / built-in-substitute site MUST embody the four-question analysis BEFORE the marker is committed:

1. **What is the use-case-specific library** for THIS output structure? Name it explicitly (module + type + function).
2. **Is it already in the codebase** (or available as a non-V2-back-compat dep)? If yes, name the existing consumer site; if no, name the package + version.
3. **What is the cost of using it here?** Visibility lift (LOC), perf class (zero / O(1) / O(N) / ...), dep weight. The cost analysis IS the perf-clause cash-out at this site.
4. **Is there a structural reason it doesn't apply?**
   - **NO** → there is no shortcut; do the work (lift visibility, add helper, refactor call site).
   - **YES** → marker text MUST name the SPECIFIC reason — NOT generic vocabulary alone.

**Performance-of-compliance is the named failure mode**: a marker shaped like an audit trail without the substance. The lint passes, the vocabulary fits, the tests are green — and the structural commitment is unmet. Worked counterfactual: slice-β `Render.columnSqlType` ("terminal SQL DDL emission boundary; both segments are typed (closed-DU dispatch + literal)") → operator caught it → slice-β' delegated to ScriptDom's typed AST + `Sql160ScriptGenerator` for 87 LOC. See `DECISIONS 2026-05-10 — LINT-ALLOW substantive-rationale discipline` for the full counterfactual + the four-question analysis. See `PLAYBOOK.md` decision tree "When you reach for a string-composition primitive" for the executable form. Lint Rule 27 maintains an audit-trail inventory; the discipline document does the catching the heuristic can't.

**Read `DECISIONS.md`'s top section in full before adopting any pattern.**

---

## What this is

You're working on the **F# sidecar (V2) of an OutSystems DDL exporter** at `/home/user/outsystems-ddl-exporter`. V1 is the C# trunk (~78K LOC at `src/`, fully shipping). V2 lives at `sidecar/projection/` — pure F# core plus C# adapters at the boundary, **a green pure test pool + Docker-dependent canary tests, 0 skipped** (run `scripts/test.sh`; absolute counts in prose drift — the run is the source of truth), lint clean across 27 rules. **DECISIONS.md supreme operating discipline carries 8 pillars** (data-structure-oriented; no string-concat; built-in obligation; FP-promised-land; coding-style; no-V2-back-compat; gold-standard-library-precedence + perf-clause; domain-first naming).

V2's purpose: make a high-stakes database cutover **verifiable, reversible, and repeatable** through a sibling chorus of synchronized projections (SSDT DDL, CDC-aware data inserts, DACPAC, refactor log, distributions, diagnostics) emitted from a single algebraic core. **V1 ships the cutover; V2 makes it trustworthy.**

---

## The forcing function

A 300-table OutSystems 11 system facing an External Entities cutover. Every Entity swaps 1:1 to external on-prem SQL Server. Four environments (dev / qa / UAT / prod), Azure DevOps PR promotion. **CDC running in production with features depending on it; spurious change records would disrupt those features.** User FKs (CreatedBy / UpdatedBy) need environment-specific remapping. RefactorLog records must survive across schema versions. Repeatable cadence — schema and data evolve continuously.

If V2's emission is wrong: production data integrity corrupted; CDC-dependent features broken silently; partial cutover leaves hybrid state structurally hard to recover from. **This is what V2 must survive.** The algebra isn't aesthetic; it's the structural condition for the cutover being trustworthy.

---

## Strategic surfaces — read in this order

Don't skip. The first reading pass takes ~1 hour and gives you the full picture.

| # | Document | What it gives you | Lines |
|---|---|---|---|
| 1 | **`sidecar/projection/VISION.md`** | Strategic frame; cutover as forcing function; sibling chorus + verification posture; acceptance criteria; cutover fallback ladder; deeper structure overview | ~410 |
| 2 | **`sidecar/projection/SPINE.md`** | The system IS a category. Seven patterns tessellate (Π / Adapter / Pass / Render / Compare / Property / Diff). Seven primitives recur. Six structural inferences (sheaf / adjunction / Hom-set / quotient / continuation / tessellation instance). Ten leverage points. **Read carefully — it's the multiplier.** | ~660 |
| 3 | **`sidecar/projection/PLAYBOOK.md`** | Technical guidance bridging vision to implementation. Recurring patterns with code skeletons. F#/C# boundary contract. Five decision trees. Twelve anti-patterns. Per-chapter strategic notes. | ~740 |
| 4 | **`sidecar/projection/STAGING.md`** | Stage 0 foundation phase. Twelve dependencies to ship before chapter 3.1 opens; ~3,000 LOC budget; ~12-15 sessions. **This is what you're doing first.** | ~580 |
| 5 | **`sidecar/projection/V2_DRIVER.md`** | **Destination KPI + operative backlog** (codified 2026-05-10; supersedes the prior `BACKLOG.md` which is now a forwarding pointer). The "Executive backlog summary" table is the chapter-by-chapter sequencing under the V2-driver KPI; per-chapter operational detail lives in `CHAPTER_*_PRESCOPE_*.md`. Reads in 15 minutes; bookmark for every chapter open. | ~750 |
| 6 | **`sidecar/projection/CLAUDE.md`** | Fresh-agent navigation; operating disciplines table; F# feature surface; programming style; load-bearing commitments. | ~470 |
| 7 | **`sidecar/projection/AXIOMS.md`** | Formal system: A1–A40, T1–T12 with amendments. **Read on demand** when working on a specific axiom. | ~900 |
| 8 | **`sidecar/projection/DECISIONS.md`** | Append-only resolved-questions log. **Read the supreme operating discipline at the top FIRST** (codified 2026-05-09; supersedes most other intents). Then read most-recent ten entries. Older entries remain in force unless superseded. | ~7500 |
| 9 | **`sidecar/projection/ADMIRE.md`** | V1↔V2 component bridge. Per-component status (admiring → extracting → advanced). | ~2700 |
| 10 | **`sidecar/projection/HANDOFF.md`** | Chapter-bridge tactical letter — most recent prologue is **chapter 3.2 close (2026-05-10)**. What's load-bearing; what's deferred. | ~700 |
| 11 | **`sidecar/projection/CHAPTER_3_2_CLOSE.md`** | Chapter-3.2 close synthesis. SnapshotRowsets variant + JSON-projection-lossiness class structurally resolved. A1's bound operationally unblocked at the OSSYS-adapter boundary. | ~250 |
| 12 | **`sidecar/projection/CHAPTER_3_1_CLOSE.md`** | Chapter-3.1 close synthesis (sessions 27–36): canary milestone sequence, four meta-codifications, forward signals. | ~180 |
| 13 | **`sidecar/projection/CHAPTER_3_5_CLOSE.md`** | Chapter-3.5 close synthesis (retroactive; joint chapter-3 cross-cutting close). Π port realization + RefactorLog + CatalogDiff. T11 as type theorem. | ~140 |
| 14 | **`sidecar/projection/CHAPTER_4_1_A_CLOSE.md`** | Chapter-4.1.A close arc — joint coverage of 3.6/3.7/4.1.A/4.1.B-α/β/γ + RawTextEmitter retirement + Tier 1/2/3 transitions. | ~500 |
| 15 | **`sidecar/projection/AUDIT_2026_05_DDD_HEXAGONAL_FP.md`** | Five-agent DDD/Hexagonal/FP audit at chapter 3.1 close. Tier 1/2/3/4 backlog by epistemic level + leverage. (Tier-1 dispositions refreshed at chapter-3 close 2026-05-10: items #1, #4, #8 cashed.) | ~150 |

Plus **chapter pre-scopes** (`CHAPTER_3_PRESCOPE_*.md` and `CHAPTER_4_PRESCOPE_*.md`) — read the relevant one when you open a chapter. Each is the first-draft slice plan.

Plus **`VISION_REVIEW.md`** for review evidence and the eight subagent reports that produced revision 2 — consult on demand for context.

---

## Where you are in the timeline

- **Chapter 1** (sessions 1–12) **closed**. Algebraic foundation; IR; strategy layer codification; three sibling Π emitters (`RawTextEmitter`, `JsonEmitter`, `DistributionsEmitter`). Per `CHAPTER_1_CLOSE.md`.
- **Chapter 2** (sessions 13–25) **closed**. OSSYS adapter (25 translation rules); Diagnostics writer; `Lineage<Diagnostics<'a>>` dual composition; strategy-layer codification at stability mark. Per `CHAPTER_2_CLOSE.md`.
- **Stage 0** (sessions 26 prework) **shipped**. Twelve foundation items landed before chapter 3.1 opened.
- **Chapter 3.1** (sessions 27–36) **closed**. Canary milestone sequence M1–M3; bench-driven optimization protocol; typed statement-stream Π output; bulk realization layer; streaming readside; 300-table forcing-function fixture (500k rows in 27s warm); five-agent DDD/Hexagonal/FP audit; first refactor batch. Per `CHAPTER_3_1_CLOSE.md` and `AUDIT_2026_05_DDD_HEXAGONAL_FP.md`.
- **Chapter 3.5** (slices α–ω, branch `claude/kickoff-architecture-improvements-Hf6Uw`) **closed (joint chapter-3 close arc, 2026-05-10)**. RefactorLog + CatalogDiff Π port; UuidV5 (RFC 4122 §4.3 + segment-incremental SHA-1); ScriptDom Sql160 typed-AST emission for SQL bread-and-butter; per-DU `toStructured` / `toDiagnosticString` typed renderers (`StructuredString`); supreme operating discipline codification at top of `DECISIONS.md`; lint discipline expanded to 26 rules; pre-commit hook + CI workflow live; F# 9 nullness-strict tests; ScriptDom parse-roundtrip + determinism property tests. T11 amendments cashed (×2) + A38 promoted. Per `CHAPTER_3_5_CLOSE.md`.
- **Chapter 3.6** (slices α–ω, branch `claude/review-ddl-exporter-EH1lh`) **closed (joint at chapter-4.1.A close arc)**. Major deliverables: `LineageEvent` typed-payload widening; `SsKey.Synthesized` typed segments; pillar 6 + 7 codified; `RawValueCodec`; `BatchSplitter` strategy; `EmissionPolicy.create` (A39); `CatalogTraversal.mapKinds`; `BenchSink` port; statistical perf-gate + Stop hook; iterator-logging discipline; FsToolkit.ErrorHandling 4.18.0 adopted.
- **Chapter 3.7** (branch `claude/review-ddl-exporter-ilV0k`) **closed (joint at chapter-4.1.A close arc)**. Audit-cleanup hygiene chapter: `Lineage.Trail [<CustomEquality>]` (A26); `SqlTypeCorrespondence` bounded context (Tier-1 #8); `Render.columnSqlType` through ScriptDom typed AST (pillar 7 cash-out); LINT-ALLOW substantive-rationale discipline codified (pillar 7 amendment; named the **performance-of-compliance** failure mode); domain-first naming discipline codified (pillar 8; named the **domain-blind naming** failure mode); Json + Distributions Π typed per-kind JsonNode (Tier-1 #7); Docker hook canary-readiness fix; **V2-driver as destination KPI codified** (`V2_DRIVER.md` standalone canonical surface; supersedes `BACKLOG.md`). Lint Rule 27 added.
- **Chapter 3.2** (sessions ~41+, branch `claude/review-ddl-exporter-zB3LF`) **closed (2026-05-10)**. SnapshotRowsets variant + RowsetBundle DTO + parseRowsetBundle translation; reference rowsets (`#RefResolved` + `#FkReality`); `EspaceKind` activation (Origin three-way real); `IsSystemEntity` → `ModalityMark.SystemOwned` IR refinement; cross-source parity tests; `propagateOrFallback` codification (7 build-failure sites). **JSON-projection-lossiness class structurally resolved; A1's identity-survives-rename bound operationally unblocked at the OSSYS-adapter boundary.** Per `CHAPTER_3_2_CLOSE.md`.
- **Chapter 4.1.A** (branch `claude/review-ddl-exporter-ilV0k`) **closed at chapter-4.1.A close arc**. The V2 SSDT DDL emitter — production schema axis, Phase 2 of V2-driver KPI critical path. Slices 1+2+3+4+5+9+10 shipped: `SsdtDdlEmitter.emitSlices : Emitter<SsdtFile>` (CREATE TABLE + indexes + intra-module FKs + composite PKs); `ManifestEmitter.emit` (V1 SsdtManifest schema mirror); `SsdtBundle.compose` (composition of `(ArtifactByKind<SsdtFile>, Manifest)` into `Map<RelativePath, string>`). **Slices 6/7/8 (cross-module FKs / identity + defaults / extended properties) now unblocked by chapter 3.2's SnapshotRowsets** — IR widening surfaces via the rowset path's SsKey carriage + EspaceKind / IsSystemEntity activation.
- **Chapter 4.1.B** (branch `claude/review-ddl-exporter-ilV0k`) **opened + α/β/γ shipped; δ-θ pending**. The V2-driver KPI's highest-stakes deliverable per `V2_DRIVER.md` per-axis correctness stakes table. Slices: α — `StaticSeedsEmitter v0` + new `Projection.Targets.Data` project + `DataInsertScript` typed value (V1-shape MERGE; chapter-4.1.B pre-scope §2.4). β — `Profile.CdcAwareness` field + change-detection MERGE predicate (the load-bearing semantic addition that closes CDC-noise on idempotent redeploys per pre-scope §6). γ — **CDC-silence-on-idempotent-redeploy canary GREEN** (the chapter signature deliverable; positive + sensitivity tests; `sys.sp_cdc_scan` Agent-less synchronous capture; `cdc.<schema>_<table>_CT` row count assertion). Slices δ (two-phase insertion / cycle-breaking), ε (MigrationDependenciesEmitter), ζ (BootstrapEmitter), η (DataEmissionComposer + EmissionPolicy.DataComposition DU), θ (partition assertion) pending.
- **M4 Tolerance taxonomy** slice α (chapter 4.1.A close arc) **shipped**. `Projection.Core.ToleratedDivergence` typed DU + `Tolerance = Set<ToleratedDivergence>` value object with smart-constructor encapsulation (`strict` / `permissive` / `withDivergence` / `tolerates` / `divergences` / `isStrict`). Five empirically-grounded variants (HeaderCommentsOmitted / PostDeployForeignKeysSplit / IndexesUnreflected / StaticPopulationsUnreflected / CommentMetadataUnreflected) per pre-scope evidence. Slice β (quotient operator on PhysicalSchemaDiff) reframed as no-op-until-consumer-pressure: the slice α variants are all about axes that PhysicalSchemaDiff doesn't compare anyway. R4 multi-environment promotion property test pending.
- **RawTextEmitter retirement arc** (this session) **fully complete**. Slice 1 added `SsdtDdlEmitter.statements : Catalog -> seq<Statement>` (typed-stream surface). Slice 2 migrated all 9 call sites (Pipeline, CLI, canary tests, cross-emitter tests) from `RawTextEmitter` to `SsdtDdlEmitter`; topological order preserved via `TopologicalOrderPass.runWith SkipSelfEdges` (cross-module FK deploy ordering). Slice 3 deleted `RawTextEmitter.fs` + `RawTextEmitterTests.fs` (-520 LOC retired).
- **Tier-1 typed-AST transitions** (chapter 4.1.A close arc; this session) **all shipped**:
  - **#4 — `Projection.Core.SqlLiteral` typed expression module**. The IR→SQL-literal projection lives in Core; both consumers (SSDT.Render + Data.StaticSeedsEmitter) flow through the typed middle layer.
  - **#1 — MERGE → ScriptDom MergeStatement typed AST**. `ScriptDomBuild.buildMergeStatement` (~150 LOC of typed-AST construction with `MergeBuildArgs` record + per-column predicate builders); `StaticSeedsEmitter.renderMerge` retired the StringBuilder construction; **6 LINT-ALLOWs retired**. The change-detection predicate is now a typed `BooleanBinaryExpression(Or)` of `BooleanComparisonExpression` + `BooleanIsNullExpression` AST.
  - **#2 — `Compose.Outputs.Sql : string` → `SsdtBundle : Map<RelativePath, string>`**. Production-shape per-table file map (per `SsdtBundle.compose`). Pipeline.write iterates the bundle; Deploy.runEphemeral consumes `Compose.aggregateSsdt` for the legacy single-string deploy contract.
  - **#3 — `Compose.Outputs.Json + .Distributions : string` → `JsonNode`**. Typed at the Outputs seam; consumers query the typed tree (no `JsonNode.Parse` re-parse). Pillar 1 holds end-to-end across Pipeline composition.
- **Tier-3 codification** (this session) **shipped**. New named failure mode **text-builder-as-first-instinct** (sibling to performance-of-compliance + domain-blind naming): the agent's first instinct on a new SQL/text emitter is StringBuilder, not the typed-AST library. The 4-step protocol (articulate the typed library → cross-check the precedent emitters → first draft uses the typed AST → LINT-ALLOWs at terminal text boundaries only) is codified in DECISIONS.md, AGENTS.md (root), and CLAUDE.md operating-disciplines table. **Two hard-requirement Active deferrals** added to the index for chapter open: `Microsoft.SqlServer.Dac` (DacFx) adoption in chapter 3.x DacpacEmitter; `ScriptDomBuild.buildMergeStatement` adoption from slice α in chapter 4.1.B slices ε/ζ (MigrationDeps + Bootstrap).
- **Docker probe + verify-before-diagnose discipline** (this session) **shipped**. `Deploy.Docker.ensureRunning` memoized + `BringupBudgetMs` lowered 30s→5s (collapsed N×30s suite-level cost to 5s when Docker is down). PreToolUse `.claude/hooks/docker-probe.sh` auto-fires before infra-relevant Bash commands (matches `dotnet test` / `docker *` / `*canary*` / `*Canary*` / `*Testcontainers*` / `*sqlcmd*` / `*mssql*`); reports current Docker state + last session-start hook line via `additionalContext`. Named failure mode **infrastructure-blame jumping** codified in AGENTS.md (root).
- **You are here.** **Chapter 3 fully closed front-to-back** (2026-05-10). Chapter 3.2 (SnapshotRowsets) shipped + ritual discharged; chapter-3 cross-cutting close polish landed (`36d1cd1`) — AXIOMS scheduled-amendments list aligned with shipped bodies; CLAUDE.md retired-code pointers fixed; CHAPTER_3_5_CLOSE.md stub written; cross-module FK + ICatalogReader deferral framings sharpened; README baseline + AUDIT_2026_05 dispositions refreshed. **The natural next move is chapter 4.1.B slice δ** (two-phase insertion / cycle-breaking) — the V2-driver KPI's highest-leverage single deliverable per `V2_DRIVER.md`. R4 multi-env property test remains an independent forward-progress alternative. Chapter 4.1.A slices 6/7/8 are now unblocked by chapter 3.2's IR widening.

**The four-environment cutover is the fixed point.** V2 must reach the V2-augmented mode of the fallback ladder by T-30 days from cutover; V2-driver mode is the aspiration. V1 stays warm through cutover+30 days regardless.

---

## Where you are picking up — current state (post-this-session)

**Branch:** `claude/review-ddl-exporter-zB3LF`. **Test baseline:** pure pool green + Docker-dependent canary tests, 0 skipped, 0 build warnings under `TreatWarningsAsErrors=true` everywhere (run `scripts/test.sh`; the run is the source of truth, not a prose count). **Lint:** clean across 27 rules. **Perf-gate:** clean against operator-reality baseline.

The most recent working session (chapter 3.2 close arc + chapter-3 cross-cutting close polish) shipped:

| # | Commit | What |
|---|---|---|
| 1 | `6dab9cd` | Chapter 3.2 slice 1 — `SnapshotRowsets` variant + `RowsetBundle` carrier + parseRowsetBundle minimum |
| 2 | `0354727` | Chapter 3.2 slice 2 — Reference rowsets (`#RefResolved` + `#FkReality`; rule 16 same-module under rowset path) |
| 3 | `d5d1812` | Chapter 3.2 slice 3 — `EspaceKind` activation (Origin three-way real refines rule 17) |
| 4 | `6eae21f` | Chapter 3.2 slice 4 — `IsSystemEntity` activation (`ModalityMark.SystemOwned` IR refinement) |
| 5 | `a74b904` | Chapter 3.2 slice 5 — cross-source parity tests (JSON ↔ Rowset; total + shape parity) |
| 6 | `0336795` | `propagateOrFallback` codification — 7 build-failure sites refactored uniformly (audit precedent) |
| 7 | `f833d53` | Chapter 3.2 close ritual — 8/8 items + SnapshotRowsets cash-out + A1 boundary unblocked |
| 8 | `36d1cd1` | Chapter-3 cross-cutting close polish — AXIOMS / CLAUDE.md / README / AUDIT_2026_05 drift cleanup + CHAPTER_3_5_CLOSE.md stub |

### What's pending (in priority order)

**Immediate — chapter 4.1.B slice δ.** The V2-driver KPI's highest-leverage single deliverable per `V2_DRIVER.md`. Two-phase insertion / cycle-breaking; sets up the CDC-silence-on-idempotent-redeploy property test at full fidelity. **This is the natural next move.**

**Newly unblocked (chapter 3.2 cash-out side effects):**
- **Chapter 4.1.A slices 6 / 7 / 8** — cross-module FKs, identity + defaults, extended properties. Previously gated on chapter 3.2 SnapshotRowsets; now IR widening surfaces via the rowset path's SsKey carriage + EspaceKind / IsSystemEntity activation.

**Hard-requirement Active deferrals (read at chapter open per Tier-3 codification):**
- **Chapter 3.x DacpacEmitter** — MUST adopt `Microsoft.SqlServer.Dac` (DacFx). Pre-scope at `CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md`. Active deferral entry at top of `DECISIONS.md`.
- **Chapter 4.1.B slices ε / ζ — MigrationDependenciesEmitter + BootstrapEmitter** — MUST adopt `ScriptDomBuild.buildMergeStatement` from slice α. Pre-scope at `CHAPTER_4_PRESCOPE_DATA_TRIUMVIRATE.md`. Active deferral entry at top of `DECISIONS.md`. Precedent is `bface9a` (StaticSeedsEmitter MERGE typed-AST cash-out).

**Substantive forward chapters (each multi-session):**
- **Chapter 4.2** — UserFkReflowPass + UserMatchingStrategy + UserRemapContext + SourceTag refactor of SsKey. **Inherits chapter 3.2's `OssysOriginal` operational reachability** — cross-version `V1Mapped` UUIDv5 derivation lands here. Pre-scope at `CHAPTER_4_PRESCOPE_USERFK_REFLOW.md`.
- **Chapter 4.3** — three-channel Diagnostics split (DecisionLogEmitter / OpportunitiesEmitter / ValidationsEmitter). Pre-scope at `CHAPTER_4_PRESCOPE_DIAGNOSTICS_AND_REMEDIATION.md`.
- **Chapter 5+** — cutover-day operator runbook (joint with solution architect); F# Analyzers SDK; Coordinates Stage 2 typed `SchemaName`/`TableName`/`ColumnName`; port lifts; V1 sunset planning.

**Independent forward-progress alternatives:**
- **R4 multi-environment promotion property test** — exercises the M4 Tolerance taxonomy concretely. ~150 LOC; uses the `Set<ToleratedDivergence>` encoding shipped in slice α. No new chapter open required.

**Quietly deferred (no current consumer; reframe at next chapter audit):**
- **Tolerance slice β** (quotient operator on PhysicalSchemaDiff). The five slice-α variants are all about axes that PhysicalSchemaDiff doesn't compare anyway; the quotient operator is no-op work as currently designed. Reopen if a new `ToleratedDivergence` variant lands that actually requires diff-filtering.
- **Outstanding chapter-3.7 slice queue** (γ traverseCatalog / ζ attach-adapters / η Result-CE adoption / θ Coordinates Stage 2 / ι writer-monad codification / κ Lineage.tell perf audit / λ SsKey.rootOriginal V1 prefix / μ Restrict→NoActionSql Diagnostics / ν F# Analyzers SDK / ξ-π port lifts; ξ stayed deferred at chapter 3.2 per variant-vs-source distinction) — see HANDOFF.md for triggers.
- **Cross-module FK IR refinement** — partial cash-out at chapter 4.1.A (topological ordering at emit layer satisfies enterprise canary); IR refinement remains deferred pending a use case where topological ordering is insufficient (likely DacpacEmitter cross-database refs or multi-V1-SS catalog spans).

---

## What you'll do first

**Chapter 3 is fully closed front-to-back** (2026-05-10). All chapter-3 close rituals have been discharged (3.1 / 3.2 / 3.5 / 3.6 / 3.7 / 4.1.A / 4.1.B-α/β/γ — each has either its own close synthesis document or is covered by `CHAPTER_4_1_A_CLOSE.md`'s joint ritual; chapter-3 cross-cutting close polish at `36d1cd1` cleaned up cross-document drift). **The natural first move is chapter 4.1.B slice δ** (two-phase insertion / cycle-breaking) — the V2-driver KPI's highest-leverage single deliverable.

If you'd rather ship the now-unblocked chapter 4.1.A slices 6/7/8 (cross-module FKs / identity + defaults / extended properties) first — chapter 3.2's SnapshotRowsets cash-out removes their structural gating — that's also a clean entry point. R4 multi-env property test remains an independent forward-progress alternative.

### Step 1 — Orient (~45 minutes)

Read in this order:
1. **`DECISIONS.md` — supreme operating discipline at the top** (codified 2026-05-09 + amendments; eight pillars including domain-first naming; supersedes most other intents). This is non-negotiable groundwork.
2. **`DECISIONS.md` — Active deferrals index** (top of file, just below the supreme operating discipline). Two new hard-requirement deferrals codified 2026-05-10 (DacFx adoption; ScriptDomBuild.buildMergeStatement adoption). The chapter-close ritual scans this table; future agents at chapter open MUST read these entries.
3. **`HANDOFF.md`** — what's load-bearing, what's deferred. Most recent prologue is **chapter 3.2 close (2026-05-10)**.
4. **`V2_DRIVER.md`** — destination KPI + operative backlog. The "Executive backlog summary" table is the chapter sequencing.
5. The most recent ~20 `DECISIONS.md` entries — bottom of file. The 2026-05-10 cluster carries the V2-driver KPI codification, three named failure modes (performance-of-compliance, domain-blind naming, text-builder-as-first-instinct, infrastructure-blame jumping), Tolerance taxonomy slice α, chapter 4.1.B opening, and the Tier-1/2/3 transitions arc.
6. `scripts/lint-discipline.sh` — 27 grep-based rules; pre-commit hook runs `--ci` on staged V2 files; CI workflow at `.github/workflows/lint-projection.yml`. Run `bash sidecar/projection/scripts/install-hooks.sh` to install the pre-commit symlink.

### Step 2 — Pick the next chapter

**Strongly recommended first move: chapter 4.1.B slice δ** — two-phase insertion (DeferredFkSet) for kinds in FK cycles. The V2-driver KPI's highest-leverage single deliverable: sets up the CDC-silence-on-idempotent-redeploy property test at full fidelity. Independent; ~50 LOC src + 50 LOC test.

Other plausible next moves:

- **Chapter 4.1.A slices 6/7/8** — cross-module FKs / identity + defaults / extended properties. **Newly unblocked by chapter 3.2's SnapshotRowsets** (IR widening surfaces via the rowset path's SsKey carriage + EspaceKind / IsSystemEntity activation).
- **R4 multi-environment promotion property test** — concrete property test exercising the M4 Tolerance taxonomy across synthetic per-env fixtures. ~150 LOC; independent of any chapter open.
- **Chapter 4.1.B slices ε/ζ — MigrationDependenciesEmitter + BootstrapEmitter** — Active deferral; **MUST adopt `ScriptDomBuild.buildMergeStatement` from slice α** per Tier-3 codification. Read the deferral entry at top of `DECISIONS.md` first.
- **Chapter 4.2 — UserFkReflowPass + SourceTag refactor** — inherits chapter 3.2's `OssysOriginal` operational reachability; cross-version `V1Mapped` UUIDv5 derivation lands here. Pre-scope at `CHAPTER_4_PRESCOPE_USERFK_REFLOW.md`.
- **Chapter 3.x — DacpacEmitter** — Active deferral with **mandatory DacFx adoption** per Tier-3 codification.
- **Chapter 3.x DacpacEmitter** — Active deferral; **MUST adopt `Microsoft.SqlServer.Dac` (DacFx)** per Tier-3 codification. Read the deferral entry at top of `DECISIONS.md` first. Pre-scope at `CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md`. Conditional on whether the cutover deploy path requires DACPAC (product question).
- **Chapter 4.2 — UserFkReflowPass + UserMatchingStrategy + UserRemapContext + SourceTag refactor of SsKey.** Pre-scope at `CHAPTER_4_PRESCOPE_USERFK_REFLOW.md`. Multi-session.
- **Chapter 4.3 — three-channel Diagnostics split.** Pre-scope at `CHAPTER_4_PRESCOPE_DIAGNOSTICS_AND_REMEDIATION.md`. Multi-session.

### Step 3 — Open the chapter

Open with a chapter-open document naming the strategic-frame axes (`DECISIONS 2026-05-15` shape; chapter 4.1.A and 4.1.B both have worked examples at `CHAPTER_4_1_A_OPEN.md` and `CHAPTER_4_1_B_OPEN.md`). Multi-session chapters earn this discipline at chapter open. Operate the chapter-mid-audit at every 3–5 substantive sessions; operate the chapter-close ritual at chapter close (eight items + the five-agent audit for architectural-frame chapters).

### How chapter 3 ended (joint close arc, 2026-05-10)

**Test count:** see `scripts/test.sh` (the run is the source of truth; prose counts drift), 0 skipped, 0 build warnings under `TreatWarningsAsErrors=true` everywhere (production AND tests). Lint clean across 27 rules.

The chapter-3 cross-cutting close discharged the eight-item ritual for chapter 3.2 specifically (see `CHAPTER_3_2_CLOSE.md`) and jointly across 3.5 / 3.6 / 3.7 / 4.1.A / 4.1.B-in-flight (see `CHAPTER_4_1_A_CLOSE.md` and `CHAPTER_3_5_CLOSE.md`). Post-close polish (`36d1cd1`) cleaned cross-document drift surfaced by an audit subagent: AXIOMS scheduled-amendments list aligned with shipped T11×2 + A38 bodies; CLAUDE.md retired-code pointers fixed; README baseline updated to 882; AUDIT_2026_05 Tier-1 dispositions refreshed (#1 BenchSink ✅, #4 paired ✅, #8 SqlTypeCorrespondence ✅); cross-module FK + ICatalogReader deferral framings sharpened.

**Chapter 3.2 highlights** (slices 1–5 + post-close bug fix; commits `6dab9cd` → `36d1cd1`):
- `SnapshotRowsets` variant of `SnapshotSource` + `RowsetBundle` carrier — JSON-projection-lossiness class structurally closed.
- A1 `OssysOriginal` operationally reachable at the OSSYS-adapter boundary (Guid carriage at module / kind / attribute levels).
- Rule 17 Origin three-way refined (`OsNative` / `ExternalViaIntegrationStudio` / `ExternalDirect`); case-insensitive `"Extension"` IS-marker.
- `IsSystemEntity` → `ModalityMark.SystemOwned` IR refinement (chosen over flat boolean + Origin axis split + new Stewardship DU).
- Cross-source parity tests (JSON ↔ Rowset; total-equality at no-Guids; shape-equality at Guid-carrying).
- `propagateOrFallback` codification — 7 build-failure sites refactored; underlying error codes now propagate uniformly.

**Chapter 3.5 substantive deliverables shipped (slices α–ω):**
- `CatalogDiff` exhaustive partition (Renamed/Added/Removed/Unchanged) with smart constructor (A38 cash-out).
- `UuidV5.create` (RFC 4122 §4.3) + `UuidV5.createFromSegments` (typed byte-segment incremental SHA-1).
- `RefactorLogEmitter` over `EmitterOverDiff<RefactorLogEntry list>` Π port.
- `RefactorLogRender` via `XmlWriter` with pinned `XmlSettings.indentedUtf8NoBom()` (built-in obligation).
- `Sql160ScriptGenerator` typed-AST emission (`ScriptDomBuild` + `ScriptDomGenerate`) for SQL bread-and-butter; production target SQL Server 2022 = compat 160.
- `ArtifactByKind<'element>` smart constructor with strict-equality keyset enforcement (T11 structural-type-encoding cash-out).
- Per-DU `toStructured` / `toDiagnosticString` typed renderers across every Outcome / KeepReason / Evidence / Conflict in `NullabilityRules` / `UniqueIndexRules` / `ForeignKeyRules` / `CategoricalUniquenessRules`.
- `LineageBuffer` reified opaque accumulator (typed-private DU wrapping `List<LineageEvent>`).
- `PinnedWriting.JsonOptions` / `XmlSettings` reified BCL option-builders.
- `DatabaseNameGenerator` reified non-determinism boundary (eliminates `Guid.NewGuid()` leaks).
- 7 Skip-stub tests retired across 5 test files; 9 + 8 + 8 + 9 + 15 + 4 new property/example tests added (CatalogDiff / UuidV5 / RefactorLogEmitter / RefactorLogRender XDocument structural / ScriptDomRoundTrip / SiblingEmitterContract — renamed at chapter 3.7 slice ε; was T11TypeTheorem).

**Disciplines codified at chapter 3.5:**
1. **Supreme operating discipline (5 pillars)** at top of `DECISIONS.md` — supersedes most other intents per session.
2. **Built-in obligation** — when a BCL/vendor SDK emits the structure, agents are obliged to use it.
3. **Reified-primitive pattern** — opaque accumulators (`LineageBuffer`), reified BCL option-builders (`PinnedWriting`), reified non-determinism boundaries (`DatabaseNameGenerator`).
4. **Extended lint discipline** — 27 rules (string-aversion / mutation / determinism / core purity / FP strict / Big-O / hexagonal-coupling / LINT-ALLOW substantive-rationale audit); `LINT-ALLOW: <rationale>` per-line and `LINT-ALLOW-FILE` / `LINT-ALLOW-FILE-MUTATION` top-of-file allowlists; pre-commit hook + CI workflow defense-in-depth.
5. **Deep per-site analysis** — every concatenation site requires explicit consideration of (a) BCL built-in, (b) vendor SDK, (c) typed data structure, BEFORE falling back to `String.concat` with `LINT-ALLOW`.

**AXIOMS cash-outs:** A38 (CatalogDiff exhaustiveness); T11 (structural-type encoding via `ArtifactByKind<'element>`).

### How chapter 3.1 ended

**Test count at close:** 713 passing (697 non-canary + 16 canary). Bench surface live across 170 call sites. Canary scale ceiling: 500k rows in 27s warm.

**Substantive deliverables shipped:**
- Typed `Statement` DU as Π's canonical output form.
- Bulk realization layer (`Bulk.copyRows` + `Deploy.executeStream`) — 43k rows/sec on V2's deploy path.
- Streaming readside (`AsyncStream` + `readRowsStream` + `Bench.streamProbe`).
- `PhysicalSchema` projection with four axes (Columns, ForeignKeys, Rows, RowDigests).
- `Coordinates.TableId` value object (Stage 1; typed `SchemaName`/`TableName`/`ColumnName` deferred to Stage 2).
- Aggregate-root smart constructors (`Catalog.create` / `Module.create` enforce 5 referential-integrity invariants).
- Writer-fidelity codification (`LineageDiagnostics.tellDiagnostics` adopted at three pass drivers; `Lineage.ofValueAndEvents` extracted at 6 sites).

**Meta-codifications shipped:**
1. Bench-driven optimization protocol.
2. Stream-realization pattern.
3. Five-agent epistemic-tier audit at chapter close.
4. Harmonization-via-parameterization pattern.

**AXIOMS amended:** A35 (Π's output is a deterministic statement stream), A36 (bulk-vs-incremental is realization-layer policy), A32 cash-out, T1 strengthened to statement-level determinism. New A37–A40 candidates scaffolded; chapter agents fill at close.

---

## The disciplines you operate

Codified in `CLAUDE.md` operating-disciplines table. Short list with what makes them load-bearing:

- **F# pure core / no I/O in Core.** Audited clean. Don't break.
- **A18 amended.** Π consumes `Catalog × Profile`, never `Policy`. If you reach for Policy from inside an emitter, the work belongs in a pass.
- **Smart constructors return `Result<'a>`.** Every value-typed invariant rides on the value.
- **Closed DUs with `[<RequireQualifiedAccess>]`** for collision-prone case names.
- **Closed-DU expansion empirical test.** Adding a variant should produce F# exhaustiveness errors only at match sites within the variant's module. If callers outside the module need reshaping, the seam is wrong.
- **Two-consumer threshold for primitive extraction.** One consumer doesn't earn an abstraction. Per `DECISIONS 2026-05-13` (anticipation vs speculation).
- **IR grows under evidence, not speculation.** Wait for the second consumer.
- **Decimal as default for continuous statistical evidence.** T1 byte-determinism requires it.
- **Pass return-type codification.** `Lineage<'output>` for decisions only; `Lineage<Diagnostics<'output>>` when decisions plus observer-relevant findings.
- **Writer-fidelity (chapter-3.1 contribution).** `LineageDiagnostics.tellDiagnostics` / `Lineage.ofValueAndEvents` are the canonical pass-driver primitives. Manual record-building is forbidden.
- **Stream-realization pattern (chapter-3.1 contribution).** Π's output is a typed deterministic stream (`seq<Statement>` for SSDT). Realization layers (text, deploy, file artifacts) are sibling consumers. The algebra holds at the stream level.
- **Harmonization-via-parameterization (chapter-3.1 contribution).** Two implementations diverging on a single axis collapse to one parameterized algorithm. Worked example: `SelfLoopPolicy` in `TopologicalOrderPass`.
- **Bench-driven optimization (chapter-3.1 contribution).** Three-candidate / 2-refuted / 1-confirmed shape; refuted swaps documented with bench data so the same swap doesn't recur.
- **Trace-before-fixture.** When implementing a V1 capability, trace V1's actual handling first; classify into the three-class typology (JSON-projection-lossiness / V2-boundary-discipline / alternative-IR-surface); then write the failing test.
- **Audit during validation.** When something second-order surfaces, act on it before shipping the slice.
- **Five-agent epistemic-tier audit at chapter close (chapter-3.1 contribution).** Multi-agent parallel; convergence-map as primary surface; Tier 1/2/3/4 backlog by epistemic level + leverage. Worked example: `AUDIT_2026_05_DDD_HEXAGONAL_FP.md`.
- **Active deferrals re-checked at chapter close.** Silent-trigger fires get caught by table-scan, not chronological re-read.

---

## F# / C# boundary rules (per PLAYBOOK)

- **F# default.** `Projection.Core`, all emitters (even Profile-consuming), boundary adapters reading via SqlClient + JSON, the comparator, all test projects.
- **C# only when foreign-API mutation-heavy.** `Projection.Pipeline` (testcontainers boot, file-system writes, CLI host); `Projection.Targets.SSDT.Dacpac` (DacFx wrapping with `IDisposable` lifetimes).
- **Seam: value-typed.** `byte[]` for DACPAC bytes; `Result<T, E>` for errors; `Map<RelativePath, string>` for directory writes; `Catalog` as opaque to C#.
- **Never:** F# code calling DacFx directly. F# code touching `IDisposable` chains. C# code mutating `Catalog`. C# code reading `Policy`.

---

## Test discipline (per CLAUDE.md)

- **Test names cite the axiom or theorem they enforce.** F# backtick-quoted: `` ``A1: rename preserves OssysOriginal SsKey`` ``, `` ``T11: emitSlices key-set equals Catalog.allKinds`` ``.
- **`Skip = "..."` for deliberate V2 divergences from V1.** The rationale lives in the Skip string. The test stays in test discovery so the divergence is structurally visible.
- **Property tests for combinatorial spaces; example tests for specific contracts.** FsCheck.Xunit covers permutation invariance, idempotence, deterministic-output-under-shuffling.
- **Three-tier canary** (per `CHAPTER_3_PRESCOPE_CANARY_PROPERTY_SURFACE.md`): tier-1 pure (no Docker, sub-second); tier-2 container-pooled (~150ms per case); tier-3 nightly integration.

---

## Git / workflow

- **Branch:** `claude/review-ddl-exporter-zB3LF` (verify with `git status`; check `git log --oneline -20` for context — most recent commits ship the chapter 3.2 close arc + chapter-3 cross-cutting close polish).
- **Commit individual slices.** Never batch unrelated changes.
- **Follow the chapter-close ritual** at every chapter close (eight items per `CLAUDE.md` operating-disciplines table; `DECISIONS 2026-05-14`). **Chapter 3 fully closed front-to-back (2026-05-10);** chapter-4 chapters open with their own ritual at close.
- **Before pushing:** `dotnet test` should be green (882 non-canary tests, 0 skipped; ~16 Docker-dependent canary tests gate on Docker availability). `bash sidecar/projection/scripts/lint-discipline.sh --ci` must exit 0 (27 rules; the pre-commit hook also runs this on staged V2 files).
- **Perf-regression gate:** `bash sidecar/projection/scripts/perf-gate.sh` runs the **operator-reality canary** (50k rows × 300 tables × variegated, the production-shape baseline; ~10s warm) via `dotnet test --filter "FullyQualifiedName~Operator-reality"` with `PROJECTION_BENCH_DIR=$ROOT`, captures the bench JSON to `bench/canary/<utc>.json`, and gates per-label `TotalMs` against `bench/history-canary.jsonl` using `μ + Kσ` (default K=3.0; warm-up phase falls back to flat `BENCH_TOLERANCE` 1.5× until N=5 history runs accumulate). The pre-commit hook + Stop hook run this automatically; soft-skips when Docker / dotnet are unavailable. Re-record the baseline with `PERF_GATE_RECORD=1 bash sidecar/projection/scripts/perf-gate.sh` when the perf floor legitimately changes (pair with a DECISIONS amendment naming the new floor's rationale). Operator decision (2026-05-09): schema-only `canary-gate.sql` is **inappropriate** for the production-use-case baseline; the gate must exercise the production envelope so feature additions can't silently regress under operator-reality conditions. Per the iterator-logging-as-first-class-outcome discipline (CLAUDE.md operating disciplines table; chapter 3.6 cash-out), the bench surface is V2's perf evidence; this gate makes regression detection structural rather than aspirational.
- **Bypassing the lint guardrail** via `git commit --no-verify` is the explicit-deviance escape hatch; CI catches bypasses on PR. Don't bypass without writing the DECISIONS amendment first.
- **Commit message format:** follow the existing pattern in `git log --oneline` (each commit is `sidecar/projection: chapter <N> <slice tag> — <action>`). Multi-paragraph body explaining the *why*, not just the *what*. Sign with the Claude Code session URL.

---

## When you're stuck

Return to the algebra. Per SPINE, the system IS a category. Identify which pattern instantiates here:

- **Π (Emitter)** — `Catalog -> Result<ArtifactByKind<'element>, EmitError>` (or Profile-consuming, or diff-consuming).
- **Adapter** — `External -> Task<Result<'internal, _>>`.
- **Pass** — `Catalog -> Policy -> Profile -> Lineage<'output>` (or with Diagnostics).
- **Render** — `SsKey list -> ArtifactByKind<'element> -> 'output`.
- **Compare** — `Tolerance -> Catalog -> Catalog -> Diff`.
- **Property** — `Catalog -> bool` (universally quantified canary).
- **Diff** — `Catalog -> Catalog -> Result<CatalogDiff, _>`.

Name the type variable. Identify which primitives compose. The pattern's universal properties tell you what tests must pass. The chapter writes itself.

When tempted to shortcut: trace V1 before writing tests; wait for the second consumer before extracting; add the AXIOMS amendment at chapter close. Each shortcut is a debug session deferred.

---

## What success looks like

**End of first session (chapter-decision):** chapter-open document for the picked next chapter (4.1.B slice δ / 4.1.A slices 6/7/8 / 4.2 / 3.x DacpacEmitter) with strategic-frame axes named. Existing suite green (`scripts/test.sh`); 0 skipped; lint clean across 27 rules. The audit-deferred items routed to the picked chapter sketched as concrete first-slice plans.

**End of next sub-chapter (~5-10 sessions):** the picked chapter's substantive deliverable shipped. AXIOMS amendments filled at close. Chapter-close ritual operated (eight items + five-agent audit if architectural-frame chapter). Forward signals cluster identified for the chapter after that.

**Cutover-quarter trajectory:** chapter 3 fully closed (V2-augmented mode operational; chapter 4.1.B slices δ→θ ship the CDC-silence-on-idempotent-redeploy property test at full fidelity). Chapter 4.2 + 4.3 close (V2-driver mode possible per T-30 gate). Cutover proceeds environment-by-environment. V1 sunset deferred until all four environments run V2 emissions for one full schema-evolution cycle.

**Chapter-3.1's structural inheritance to the chapters ahead:**
- Π output as typed stream → chapters 3.5 (shipped) / 4.1 / 3.x inherit the seam.
- `Bulk` / `RowDigester` / `AsyncStream` substrate → chapter 4.1 fills the data triumvirate emitters.
- `Coordinates.TableId` value object → chapter 4 / 5 extend with `SchemaName` / `TableName` / `ColumnName` Stage-2.
- Aggregate-root smart constructors → chapter 4.2 User FK reflow consumes the integrity invariants.
- Writer-fidelity discipline → all future passes operate the dual-writer's API.
- Five-agent audit protocol → repeat at every chapter close that warrants architectural review.

**Chapter-3.5's structural inheritance to the chapters ahead:**
- Supreme operating discipline (5 pillars) → all sessions confirm intent against each pillar before adopting any pattern.
- `ArtifactByKind<'element>` smart constructor → all sibling Π's land their typed output through it (T11 structural).
- `Sql160ScriptGenerator` typed-AST emission → chapters 4.1 / 4.4 inherit the SQL-emission seam (built-in obligation).
- `XmlWriter` / `Utf8JsonWriter` pinned-settings reified primitives → all future XML/JSON emission paths inherit `PinnedWriting`.
- Lint discipline (27 rules + pre-commit + CI) → all future code lands lint-clean by default; deviations carry `LINT-ALLOW` markers.
- Per-DU `toStructured` / `toDiagnosticString` typed renderers → all future Outcome / KeepReason / Evidence DUs ship with the same surface.

---

## Hold the spine

V2 isn't aesthetic. The algebra isn't ceremonial. Every type theorem (T1, T11, A1) maps to a cutover-blocking property. The seven primitives compound. The chapter pre-scopes are tessellation instances, not arbitrary slice plans.

**V1 ships the cutover. V2 makes it verifiable.** Stage 0 is the moment the algebra becomes types. Land it cleanly; the rest compounds.

Three structural type theorems, one foundation phase, one property-test surface, one triangulation comparator, one fallback ladder, ten chapters. Hold the spine. The rest follows.

— Welcome aboard. Read the surfaces. Write the documentation. Open the first commit.
