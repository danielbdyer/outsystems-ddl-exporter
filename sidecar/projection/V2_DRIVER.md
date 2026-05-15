# V2_DRIVER — Destination KPI for the V2 sidecar

**Codified:** 2026-05-10 (chapter 3.7 sidebar; principal-PO discussion).
**Status:** load-bearing operative target. Supersedes the implicit "V2-augmented is the floor; V2-driver is aspirational" framing in `DECISIONS 2026-05-22 — R6: Split-brain governance rule for the dual-track cutover window`.
**Scope:** every chapter, every slice, every architectural decision in V2 from this date forward.

---

## The KPI in one sentence

**V2 reaches V2-driver mode for the cutover by being provably correct on every axis V2 owns — schema, data, identity, diagnostics, and any future sibling — with provable correctness defined as structural-type-level enforcement plus per-axis property tests, not aspirational discipline plus selective coverage.**

Operationally: every decision (chapter sequencing, slice extraction, primitive design, test coverage, lint enforcement, codification timing) is biased toward V2-driver. When two paths offer the same correctness with different LOC, prefer fewer LOC. When two paths offer different correctness depth, prefer the deeper correctness — even if the LOC investment is larger.

---

## Why this document exists

The R6 governance rule (`DECISIONS 2026-05-22`) framed three rungs of a fallback ladder: V1-only (V1 cutover, V2 ships post-cutover); V2-augmented (V1 drives, V2 verifies in PR; per-environment-per-artifact-type V2-driver transition gated on N=10 consecutive green canary runs plus operator sign-off); V2-driver (V2 emits production artifacts; V1 stays warm through cutover+30 as fallback). The R6 entry was operationally precise about *how* the transition happens but ambiguous about *whether* V2-driver was the destination or the stretch goal.

The principal-PO discussion (chapter 3.7 sidebar; this date) resolved the ambiguity. V2-driver IS the destination. V2-augmented is the gate, not the floor. V1-only is the safety net for the cutover window itself, not the sustained operating mode. The cutover (~late July 2026 per operator estimate; ~80 days from this codification) is V1-functional already; V2's job is to make every axis it owns provably correct so the cutover is verifiable end-to-end and so V1 sunset becomes a real plan post-cutover.

This document codifies that operative target as a standalone canonical surface. Every chapter agent reads this before opening a slice. Every chapter close evaluates progress against the per-axis correctness depth table below. Every primitive extraction, port lift, axiom amendment, and tolerance entry serves this KPI.

---

## V2-driver — what it means concretely

**V2-driver mode** = V2's emitted artifacts ARE the artifacts that deploy to production environments. V1 stays warm through cutover+30 days as a fallback rung the operator can drop to if a V2-emitted artifact triggers an unexpected divergence; V1 sunset begins after all four environments have run V2 emissions for one full schema-evolution cycle without canary divergence.

The artifacts V2 emits in V2-driver mode (each chapter is the substantive deliverable):

- **SSDT DDL** for production deploy (chapter 4.1.A SsdtDdlEmitter; depends on chapter 3.5 Π port realization).
- **CDC-aware data inserts** for static populations, migration dependencies, bootstrap (chapter 4.1.B data triumvirate; CDC discovery via `sys.cdc_tables`; idempotent-redeploy property test asserts zero records in `cdc.change_tables` on second deploy).
- **User FK reflow** for cross-environment User identity (chapter 4.2 UserFkReflowPass; UserMatchingStrategy DU; UserRemapContext; SourceTag value-object refactor of SsKey).
- **Operational diagnostics** for the three-channel split (chapter 4.3 DecisionLogEmitter / OpportunitiesEmitter / ValidationsEmitter; activates Diagnostics writer's deferred channel-routing under real consumer pressure).
- **DACPAC** if the deploy path requires it (chapter 3.x DacpacEmitter via DacFx; T11 sibling-Π commutativity at the structural level between SSDT and DACPAC; A37 named erasure axes in Tolerance).
- **RefactorLog** for cross-version identity tracking (chapter 3.5 slices θ/ι; round-trip property: composing diff(V0→V1) with diff(V1→V2) yields diff(V0→V2) modulo loss-free paths).
- **CatalogDiff** for evolution as value (chapter 3.5 slice ζ; exhaustive partition; smart constructor enforces partition disjointness).
- **Manifest** for V2's own emission contract (multi-environment policy/profile parameterization; per-emitter erasure declarations; tolerance config).

This is not what V2-augmented mode requires. V2-augmented requires V2 to emit *something* diffable against V1 plus a tolerance config; V2-driver requires V2 to emit *the production bytes* with property tests asserting every axis V2 owns.

---

## What V1 retains under V2-driver mode

V2-driver is **not** "V2 reimplements V1." Per the cherry-pick discipline (`DECISIONS 2026-05-06 — Sidecar lives at sidecar/projection/`), V2 references no V1 source files; per the IR-grows-under-evidence discipline, V2 doesn't duplicate V1 surfaces that don't add provability.

V1 retains:

- **Live SQL extraction.** V1's `IAdvancedSqlExecutor` (and successor) reads from real SQL Server instances; V1's evidence cache is the source of truth. V2 consumes V1's `osm_model.json` (or successor manifest) via the OSSYS adapter — V2 does not re-extract from SQL Server.
- **The V1 manifest as evidence source.** V1's `FullExportRunManifest` (proof artifacts + cache + evidence files) is the contract V2 consumes. V2 reads it; V2 doesn't re-derive it.
- **The V1 documentation surfaces V2 doesn't duplicate.** V1's `handbook/` (operator-facing teaching material) and `ssdt-playbook/` (66 markdown files of per-change-tier mechanics + multi-phase patterns + CDC handling) are kept and maintained. V2 has its own `KICKOFF.md` / `VISION.md` / `SPINE.md` / `PLAYBOOK.md` / `DECISIONS.md` / `AXIOMS.md` — but these are V2-internal. The cutover-day operator runbook (joint deliverable: solution architect + V2) bridges them.
- **Operator-facing CLI surfaces during transition.** V1 commands continue to work. The transition to V2-driver is per-environment-per-artifact-type via R6 governance, not a flag-day cutover. During transition, V1 commands and V2 commands coexist; the operator chooses per-pair what to drive with.
- **V1 stays warm through cutover+30.** Per `DECISIONS 2026-05-22 — T-30 / T-15 cutover fallback ladder gates`. This rule is preserved unchanged. V1 sunset begins after the cutover-survival window AND after all four environments have run V2 emissions for one full schema-evolution cycle.

V1's role under V2-driver is **upstream evidence + safety net**, not co-driver. The V1↔V2 cohabitation is durable through cutover+30; sustained-mode is V2-only with V1 archived.

---

## Per-axis correctness stakes — where to apply the deepest rigor

Not every axis V2 owns has the same failure-mode cost. Provable correctness applies to all of them; *depth* of verification investment maps to per-axis stakes:

| Axis | Failure mode if wrong | Primary property test | Verification depth |
|---|---|---|---|
| **CDC silence on idempotent redeploy** | Spurious `cdc.change_tables` records corrupt CDC-dependent features silently in production | "Deploying the same insert script twice produces zero records in `cdc.change_tables` for every CDC-tracked table" | **Highest.** Per-CDC-table coverage. Multi-redeploy property. CI gate: red on any non-zero. |
| **Skeleton/overlay separation (Transform Totality)** | Operators can't tell which overlay touched what; A18 leaks at a new pass go unnoticed; CDC silence canary asserted against an unenumerated baseline; V1↔V2 differential disagreements become unattributable (skeleton drift or overlay omission?) | "Every `Pass` emitting a `LineageEvent` is in `TransformRegistry`; every registered pass is exercised in canary; `Compose.runWithSkeleton` produces a deterministic baseline; manifest names every applied overlay per artifact" | **Highest.** Co-equal with CDC silence as load-bearing structural enforcement. Coverage tests (`TransformRegistryCompletenessTests`) fail the build on any inclusion gap. The seam that catches skeleton/overlay drift at scale. See `V2_PRODUCTION_CUTOVER.md` §6.4.7 (A.4.7); L3-CC-Transform-Totality in `PRODUCT_AXIOMS.md`. |
| **Schema (SSDT DDL)** | Production deploy fails or deploys wrong shape | T11 structural (every Π's keyset = `Catalog.allKinds.SsKey set`) + PhysicalSchema round-trip empty + parse-roundtrip + byte-determinism | High. Mostly shipped (chapter 3.1 substantive deliverable). |
| **Data (static populations + seeds + bootstrap)** | Seed data missing, duplicated, or topologically out-of-order causing FK violations | Idempotent redeploy produces zero net changes; topological ordering preserves FK validity | High. Substrate ready (chapter 3.1 Bulk + RowDigester); emitters chapter 4.1.B. |
| **User FK reflow** | Production reports break or data loss when User remapping is incomplete | Every CreatedBy/UpdatedBy FK in target environment resolves to a valid target User; per-strategy coverage (ByEmail, BySsKey, ManualOverride, FallbackToSystemUser) | High. Chapter 4.2. |
| **RefactorLog round-trip** | Cross-version identity tracking breaks; downstream systems lose history | Composing diff(V0→V1) with diff(V1→V2) yields diff(V0→V2) modulo loss-free paths | High. Chapter 3.5 θ/ι. |
| **Multi-environment promotion** | Per-env policy divergence not caught until second env deploys | Artifacts pairwise-equal modulo named per-env policy/profile differences (R4 multi-environment property) | Medium-high. Tolerance taxonomy (M4) is the decision surface; integration test exercises four-pair flow. |
| **Identity preservation across reload** | A1 unconditional bound breaks; SSKey synthesis becomes lossy | Every SsKey from `osm_model.json` round-trips through V2's IR and back to a stable SsKey (variant-aware) | Medium. Mostly structural via SsKey four-variant DU; property test in `SsKeyTests.fs`. |
| **DACPAC round-trip** (if deploy path requires) | DACPAC erases axes silently; SSDT and DACPAC siblings diverge | T11 structural between SSDT and DACPAC; named erasure axes (A37) in Tolerance | Medium-high. Chapter 3.x DacpacEmitter; conditional on deploy path. |
| **Static populations** | Seed data missing or duplicated | Idempotent redeploy produces zero net changes; rows hash-equal to V1 emission modulo named tolerances | Medium. Chapter 4.1.B + PhysicalSchema.Rows axis (chapter 3.1 shipped). |
| **Operational diagnostics** | Operator can't diagnose post-cutover issues | Per-channel routing property: each entry routes to correct channel per severity; Lineage trail audit completes | Lower. Chapter 4.3. |

The CDC-silence property test is the **highest-leverage single deliverable for the catastrophic-silent-failure axis**. The transform-registry / skeleton-overlay separation is its **load-bearing structural sibling** — A18 amended is the Π-side commitment; the registry is the Pass-side commitment; together they carry the operator's promise ("ask for the vanilla projection and I get a deterministic factual baseline; every override I apply is named and recorded") as a type-witnessed contract, not a discipline. Without the registry, CDC silence is asserted against an unenumerated baseline; the canary stops being airtight. Build the registry under A.4.7 (post-A.0' close); gate the workstream close on the five `TransformRegistryCompletenessTests`.

---

## Executive backlog summary

This document IS the V2 backlog (supersedes the prior `BACKLOG.md` which is now a forwarding pointer here). The V2-driver KPI reorders the backlog by V2-destination axis rather than V1-source-area; per-chapter operational detail continues to live in `CHAPTER_*_PRESCOPE_*.md` documents, which this backlog cross-references.

### Counts by phase (chapter ownership under V2-driver KPI)

Status legend:
- **shipped** — V2 has it; tests prove it; chapter close ritual operated.
- **substantively shipped** — V2 has it; chapter close ritual deferred (next-chapter prologue absorbs).
- **in-flight** — slices landing in current chapter; not yet at chapter close.
- **not-started (critical)** — pre-scoped; on V2-driver critical path.
- **not-started (conditional)** — pre-scoped; conditional on deploy-path / consumer demand.
- **deferred-with-trigger** — named in V2 plan; explicit re-open trigger; protected by two-consumer threshold or similar discipline.

| Phase | Chapter | Substantive deliverable | Status | LOC budget | Gate |
|---|---|---|---|---|---|
| Stage 0 | (foundation) | 12 foundation items per `STAGING.md` | **shipped** | ~3,000 | Twelve items landed before chapter 3.1 opened |
| (closed) | Chapter 1 | Algebraic foundation; IR; strategy layer; three sibling Π emitters | **shipped** (sessions 1–12) | ~5,000 | `CHAPTER_1_CLOSE.md` |
| (closed) | Chapter 2 | OSSYS adapter (25 translation rules); Diagnostics writer; `Lineage<Diagnostics<'a>>` dual composition | **shipped** (sessions 13–25) | ~3,500 | `CHAPTER_2_CLOSE.md` |
| (closed) | Chapter 3.1 | Canary milestone sequence M1–M3; bulk realization; streaming readside; PhysicalSchema four-axis; aggregate-root smart constructors | **shipped** (sessions 27–36) | ~4,000 | `CHAPTER_3_1_CLOSE.md` + `AUDIT_2026_05_DDD_HEXAGONAL_FP.md` |
| (closed) | Chapter 3.5 (substantive) | Π port realization (RawText slice α only); RefactorLog + CatalogDiff Π port; UuidV5; Sql160ScriptGenerator typed-AST; per-DU `toStructured`/`toDiagnosticString`; supreme operating discipline codification (5 pillars); lint discipline expanded to 26 rules | **substantively shipped** (slices α–ω; close ritual deferred) | ~3,500 | KICKOFF.md table |
| (closed) | Chapter 3.6 (substantive) | LineageEvent typed-payload widening; SsKey.Synthesized typed segments; Pillar 6 + 7 codified; RawValueCodec; ConnectionString.parse; BatchSplitter; EmissionPolicy.create; CatalogTraversal.mapKinds; BenchSink port; statistical perf-gate; FsToolkit adoption | **substantively shipped** (slices α–ω; close ritual deferred) | ~2,500 | KICKOFF.md prologue |
| (closed) | **Chapter 3.7 (substantive)** | Audit-cleanup hygiene + pillar 7/8 codification + Docker hook fix + V2-driver KPI codification | **substantively shipped** (slices α/β/β'/β''/ε/β'''/ζ/Docker; close ritual deferred) | ~1,500 | Chapter close ritual deferred to joint pass with 4.1.A + 4.1.B-in-flight + RawTextEmitter retirement + Tier 1/2/3 |
| (closed) | **RawTextEmitter retirement arc** | The chapter-3-era one-big-string + raw-INSERT pre-cursor fully retired; -520 LOC | **shipped** (slices 1+2+3; chapter 4.1.A close arc) | -520 | Pillar 8 win: action-shaped name retires; concept-shaped `SsdtDdlEmitter` + `StaticSeedsEmitter` remain |
| (closed) | **Tier-1 typed-AST transitions** | `Projection.Core.SqlLiteral` typed module (#4); MERGE → ScriptDom MergeStatement (#1; -6 LINT-ALLOWs); `Compose.Outputs.Sql` → `SsdtBundle Map<RelativePath, string>` (#2); `Compose.Outputs.Json + .Distributions` → `JsonNode` (#3) | **shipped** (chapter 4.1.A close arc; this session) | ~400 | Pillar 1 holds end-to-end across Pipeline composition seam |
| (closed) | **Tier-3 codification** | Text-builder-as-first-instinct discipline named (third failure mode); DACPAC + MigrationDeps/Bootstrap hard-requirement Active deferrals codified | **shipped** (chapter 4.1.A close arc; this session) | n/a | Active deferrals scan at chapter open forces typed-AST adoption |
| Phase 1 (closed) | **Chapter 3.5 (close)** | RawText typed Π port realization; RefactorLog + CatalogDiff Π port; UuidV5; Sql160ScriptGenerator typed-AST | **substantively shipped** (Phase 1 complete; Json + Distributions Π ports + RawText typed-stream surface + RawTextEmitter retirement; T11 fully structural across SsdtDdl + Json + Distributions siblings) | ~600–800 | ✓ T11 fully structural across all three siblings + the SSDT-DDL fourth sibling |
| Phase 2 (in-flight) | **Chapter 4.1.A + Tolerance + multi-env** | SsdtDdlEmitter (production output); Tolerance taxonomy; multi-environment promotion property test | **substantively in-flight** (chapter 4.1.A in-flight surface SHIPPED slices 1+2+3+4+5+9+10; Tolerance slice α SHIPPED; slices 6/7/8 gated on chapter 3.2; R4 multi-env property test pending) | ~600–900 | Slices 6/7/8 + R4 remain |
| **Phase 3 (highest-stakes deliverable shipped)** | **Chapter 4.1.B (CDC-critical)** | StaticSeedsEmitter; MigrationDependenciesEmitter; BootstrapEmitter; **CDC silence on idempotent redeploy property test (highest-stakes deliverable)** | **opened + slices α/β/γ shipped; δ-θ pending**. Slice γ — CDC-silence canary GREEN under real SQL Server 2022 CDC (positive + sensitivity tests). Slices ε/ζ have **hard-requirement Active deferral**: MUST adopt `ScriptDomBuild.buildMergeStatement` from slice α precedent (Tier-3 codification) | ~1,500–2,000 | ✓ CDC-silence property green; ε/ζ/η/θ remain |
| **Phase 4** | **Chapter 4.2** | UserFkReflowPass; UserMatchingStrategy DU; UserRemapContext; SourceTag value-object refactor of SsKey identity DU; per-strategy coverage (ByEmail / BySsKey / ManualOverride / FallbackToSystemUser) | **not-started (critical)** | ~800–1,200 | Every CreatedBy/UpdatedBy FK in target environment resolves to a valid target User; per-strategy property tests green |
| **Phase 5** | **Chapter 4.3** | DecisionLogEmitter; OpportunitiesEmitter; ValidationsEmitter; three-channel split (operator / auditor / developer) activated under real consumer pressure | **not-started (critical)** | ~600–800 | Per-channel routing property green; Lineage trail audit completes for at least one full pass cycle at canary scale |
| **Phase 6** | **Chapter 3.x DacpacEmitter** (conditional) | DacFx adoption; DacpacEmitter; A37 named erasure axes; T1 binary-normal-form amendment | **not-started (conditional)** — depends on deploy-path requirement | ~800–1,200 | T11 structural between SSDT and DACPAC; canary asserts SSDT and DACPAC round-trip identically modulo erasure axes |
| **Phase 7** | **Chapter 3.2 SnapshotRowsets** | SnapshotRowsets adapter variant; closes JSON-projection-lossiness; A1 holds unconditionally; lifts `ICatalogReader` port (Position B → A); Diagnostics-emission scaffolding for V1 silent drops | **not-started** (critical for cross-version identity stability under V2-driver) | ~600–900 | A1 unconditional bound property green for rowset-sourced SsKeys |
| **Phase 8** | **Chapter 5+ pragmatic close** | F# Analyzers SDK custom analyzer (slice ν); Coordinates Stage 2 typed VOs (slice θ from chapter 3.7); Hex port lifts (`IArtifactSink` + `IDeployHost`) under consumer demand; cutover-day operator runbook (joint with solution architect); V1 sunset planning | **deferred-with-trigger** (consumer-pressure-driven) | varies | per-item triggers |
| Won't-carry-forward | (cuts) | V1-specific items; SMO emission path; multi-spine state pattern (until use case surfaces); Faker emitter (until third evidence type); etc. | **cut** (~26 items per prior BACKLOG; documented as intentional non-coverage) | n/a | n/a |

**Total V2-driver critical path (Phases 1–5 + Phase 7):** ~5,100–7,600 LOC. At observed ~7-sessions/day cadence: ~6–10 calendar days. At session-cadence: ~9–12 weeks.

### Counts by V2-status (informational)

Drawn from prior BACKLOG.md eight-subagent survey at chapter-3.1-close (2026-05-30); chapters 3.5 + 3.6 + 3.7 progress incorporated:

| Area | Items | Shipped | In-flight / pre-scoped | Not-started (critical or conditional) | Won't-carry-forward |
|---|---|---|---|---|---|
| 1. Extraction + Domain (V1 input surface) | 34 | 19 | 12 (chapter 3.2 SnapshotRowsets) | 0 (V2 consumes V1 manifest; doesn't re-extract) | 3 |
| 2. Profile + Tightening passes | 70 | 48 | 18 | 0 (chapter 1 + 2 substrate complete) | 4 |
| 3. SSDT/SMO emission | 76 | 1 (RawText debug) | ~5 (chapter 3.5 Π port + chapter 4.1.A SsdtDdlEmitter) | ~62 (chapter 4.1.A) | 8 (SMO path cut) |
| 4. Data emission + UAT + Migration | 60 | 0 | ~5 (chapter 4.1.B + 4.2) | ~50 (chapter 4.1.B + 4.2) | 5 |
| 5. Pipeline + CLI | 50 | 5 (canary CLI; perf-gate; CLI surface partial) | ~5 (chapter 4.3 + multi-env) | ~36 (chapter 4.x) | 4 |
| 6. DMM + AdvancedSql + test infra | 30 | 4 | 16 | 5 (chapter 5+) | 5 |
| 7. V2 intent + governance | 55 | 18 (this codification + chapter 3.7 codifications + R6 + T-30/T-15 ladder + chapter-close ritual + 8 pillars + 2 named failure modes + ...) | ~25 (chapter 4.x + AXIOMS amendments at chapter close) | ~12 (chapter 5+ + governance ad-hoc) | 0 |
| **Total** | **~375** | **~95** | **~86** | **~165** | **~29** |

Net change since chapter-3.1-close (2026-05-30): +23 shipped (chapter 3.5 substantive + chapter 3.6 substantive + chapter 3.7 9 slices). The "shipped" count grows by ~one slice per session; the "not-started" count shrinks by chapter activation, not slice activation.

### Top-of-mind risks under V2-driver KPI

1. **CDC governance dry-run never executed.** Production CDC-enabled tables have never seen V2 emission. The highest-stakes silent-failure mode is unobserved. **Mitigation:** chapter 4.1.B opens with the CDC silence property test; gate chapter close on it; run dry-run on at least one CDC-enabled table (production-shape) before chapter close.
2. **Multi-environment promotion flow has no integration test.** Single-environment canary is green; the four-environment governance protocol is prose, not code. **Mitigation:** chapter 4.1.A includes the multi-environment promotion property test; integration test runs on at least two environments before phase close.
3. **Tolerance taxonomy is reserved DECISIONS prose, not implementation.** Without it, "canary green" has no decision criterion. **Mitigation:** chapter 4.1.A delivers M4 Tolerance taxonomy as part of the phase 2 critical path.
4. **The cutover-day operator runbook doesn't exist.** Owned by solution architect (per principal-PO sidebar); V2 supports via canonical surfaces. **Mitigation:** chapter 5+ joint deliverable; tracked but not blocking.
5. **Discipline-density risk under chapter 4.x scope expansion.** 14 load-bearing commitments today; chapter 4.x adds User FK semantics, Diagnostics channel routing, multi-environment policy/profile shape. By chapter 4.3, the load-bearing list could be 20+ items. **Mitigation:** every chapter close that earns a new commitment writes it into the supreme operating discipline (top of `DECISIONS.md`); the chapter-close ritual catches discipline drift.
6. **AXIOMS amendments accumulate without cash-out.** T1-binary-normal-form (chapter 3.x), T11-typed (chapter 3.5 close), T11-diff-typed-inputs (chapter 3.5), A1-four-variant (chapter 3 cross-cutting close), A35–A40 candidates have a structured-amendment cadence per Stage 0 S0.F. **Mitigation:** chapter close cannot complete without resolving its placeholders; the structural forcing function is the chapter-close ritual itself.

### Free corollaries (per `SPINE.md` leverage points; preserved from prior BACKLOG)

Several backlog items are **free corollaries** of foundation work — they ship without being chapters.

| Backlog item | Free given | SPINE leverage |
|---|---|---|
| Drift detection on four real DBs | Read-side adapter (chapter 3.1 shipped) + Compare (chapter 3.1 shipped) | L3 — drift is one CI cron job, not a chapter |
| RemediationEmitter (would be chapter 4.4) | DacpacEmitter (chapter 3.x) + CatalogDiff (chapter 3.5 ζ) + Render.toDacpac (Stage 0 S0.C) | L4 — one-line composition; chapter would be ~360 LOC; **deferred under V2-driver KPI; revisit at chapter 5+ if remediation is operator-needed** |
| Drift across schema versions (no chapter) | CatalogDiff (chapter 3.5 ζ) + diff composition | L5 — `CatalogDiff.compose` from existing primitive |
| Multi-environment cutover (chapter 4.1.A + ad-hoc) | Multi-env generator (Stage 0 S0.K shipped) + `policyOrthogonal` predicate | L6 — one algebra applied N times |
| Future Faker / OData / REST-API readers | `Adapter<'source, 'internal, 'error>` typed pattern | L7 + L8 — type variable choice; pattern free; **deferred per IR-grows-under-evidence** |
| V1 sunset per environment | Fallback ladder DECISIONS entry + per-env quotient configuration (Tolerance taxonomy from chapter 4.1.A) | L9 — YAML edit per environment; sunset begins after cutover+30 + one full schema-evolution cycle |

**Implication:** the backlog's ~375 items overstate the *implementation* surface. After Stage 0 + chapters 1–3.7, the actual remaining V2-driver implementation surface is ~250 items; the rest are inherited or free corollaries.

### V1-soak debt lane (v1-side PRs that accelerate Phase A.6)

Three v1-trunk fixes that reduce false-positive noise during Phase A.6 differential testing (V1 ≈ V2 on real workload). Each one removes a class of disagreement V2 would otherwise have to absorb as a Tolerance entry or attribute as a V1 bug at canary time. **Sequenced as v1-side PRs against the v1 trunk; not v2 work.** The lane sits in V2_DRIVER (not in V1's roadmap) because the value of fixing them is felt during V2 soak, not during V1 standalone operation. See `V2_PRODUCTION_CUTOVER.md` §13.6 for the full rationale connecting each vector to Phase A.6 / Phase B preconditions.

| # | Vector | Estimated effort | Phase-A.6 leverage | Status |
|---|---|---|---|---|
| **V1.1** | **EntityFilters wiring** — extend `ModuleEntityFilterOptions` (currently wired only in `SqlDynamicEntityDataProvider`) to `SqlModelExtractionService` + `SqlDataProfiler` + validation scope, so V1's metadata fetch and profiling respect the same selection V2 does. | 0.5-1 week | High — eliminates "V1 over-fetches" tolerance class | un-started |
| **V1.2** | **Global topological sort for StaticSeeds** — fix `BuildSsdtStaticSeedStep` to sort across all categories (static + regular) then filter, matching the pattern `BuildSsdtBootstrapSnapshotStep` already uses correctly. Cross-category FKs (static→regular, regular→static) currently violate constraints in V1's StaticSeeds output. | 0.5 week | High — removes "V1 emits FK-broken seed output" from the disagreement class | un-started |
| **V1.3** | **DatabaseSnapshot dedup** — consolidate the three independent OSSYS_* fetch paths (`SqlModelExtractionService` / `SqlDataProfiler` / `SqlDynamicEntityDataProvider`) into a single snapshot; eliminates 2-3x redundant queries; stabilizes V1 manifest output under concurrent extraction or mid-call schema drift. | 1 week | Medium-high — reduces variability in V1 manifest output that Phase B (V2 owning extraction) would otherwise inherit | un-started |

**Provenance.** These three vectors originate from a v1-refactor proposal (`docs/architecture/entity-pipeline-unification-v2.md`) authored before V2's algebraic frame was established. The full document is a v1-side modernization plan, not a V2 plan; this lane surfaces only the highest-leverage items whose payoff is felt during V2 soak.

**Operating discipline.** Each vector is small, surgical, and reversible. Owner: V1 maintainers under V1 governance. Exit gate: all three landed before Phase A.6 begins, OR treated as named Tolerance entries until they land. **NOT load-bearing for V2-driver KPI directly** — the KPI tracks V2-axis property tests; V1-side fixes are accelerators, not deliverables. If a v1-side PR doesn't land, V2 continues; the cost is paid as tolerance churn during soak.

### Reading guide for this backlog

This backlog is **strategic reference**, not a sprint plan. Each chapter pre-scope (`CHAPTER_3_PRESCOPE_*.md`, `CHAPTER_4_PRESCOPE_*.md`) is the operational plan for that chapter; this backlog cross-references which items each chapter delivers and what V2-driver KPI gate the chapter passes.

**When to consult:**
- **Chapter open**: cross-reference items the pre-scope claims against this backlog's "by phase" table. Identify any items the pre-scope omits relative to V2-driver KPI gates.
- **Chapter mid-audit** (per chapter-mid-audit discipline): scan items in flight; surface any that have silently shifted disposition.
- **Chapter close** (chapter-close ritual): confirm shipped items are marked, ADMIRE.md entries land, AXIOMS.md amendments commit, deferred items remain triggered, V2-driver gate for the phase is green.
- **Pre-cutover check-ins**: verify CDC silence property green; verify multi-env property green; verify Tolerance taxonomy operationalized; verify per-axis property tests green for every axis V2 owns under V2-driver mode.

---

## The disciplines that compound under this KPI

The eight pillars in the supreme operating discipline (top of `DECISIONS.md`) and the two named failure modes (performance-of-compliance; domain-blind naming) **are exactly the substrate provable correctness needs**. Each pillar protects against a specific class of runtime-only invariant — invariants that pass tests today but drift tomorrow because their correctness rests on convention rather than structure.

**Pillar 1 (data-structure-oriented over string-parsing).** Provable because typed values flow through the pipeline; the BCL writer is the only string-emission boundary. Drift between producer and consumer is structurally impossible — the type system proves the consumer can't see something the producer didn't intend.

**Pillar 2 (avoid string concatenation aggressively).** Every concat site requires per-site analysis. The lint guardrail (Rule 27) maintains the inventory; the four-question analysis prevents performance-of-compliance.

**Pillar 3 (built-in obligation).** When BCL or vendor SDK emits the structure, V2 is *obliged* to use it. Provable because vendor specifies emission semantics. ScriptDom's `Sql160ScriptGenerator` IS the SQL DDL grammar; `Utf8JsonWriter` IS the JSON emission rules; `XmlWriter` IS the XML emission rules. V2 delegates rather than reinvents.

**Pillar 4 (promised land of FP).** ≥95% pure functions; ≤5% mutation isolated, reified, exhaustively tested. Provable because purity makes equational reasoning sound. T1 byte-determinism rests on this.

**Pillar 5 (coding-style commitments).** Deep DDD, point-free composition, hexagonal architecture, hardcore FP. Each is provable because it makes the type system carry more of the proof burden.

**Pillar 6 (no V2-internal back-compat paths).** Refactor at insight, no exceptions. Provable because back-compat shims introduce runtime-only invariants ("the old API still works as the new one expects") that the type system can't verify.

**Pillar 7 (gold-standard library precedence + LINT-ALLOW substantive-rationale amendment).** The use-case-specific library is the gold standard. The four-question analysis (use-case-specific library / availability / cost / structural reason) prevents the performance-of-compliance failure mode where a marker is shaped like an audit trail without the substance.

**Pillar 8 (domain-first naming + ubiquitous-language consistency).** Every name in V2 names a domain concept in cutover-business vocabulary. The four-question analysis (concept articulation / V2 already names this / concept-shaped vs action-shaped / generic-suffix smell test) prevents the domain-blind naming failure mode. Provable because the type system's structural reasoning depends on the names being domain-meaningful — operators reading V2 source must recognize their concepts; engineers reviewing V2 changes must recognize when the concept being changed has business implications.

**Two named failure modes.** Performance-of-compliance (a marker shaped like an audit trail without the substance) and domain-blind naming (a name shaped like a placeholder for the absent domain concept) are both the agent self-deception failure mode — feels productive, lint passes, tests are green, structural commitment is unmet. Both are detectable only by the four-question analyses. Both are protected by the discipline-document path, not by lint heuristics.

**Two-consumer threshold for emergent primitives + IR-grows-under-evidence.** Both prevent speculative LOC. Provable correctness doesn't mean *more* code; it means *the right code with provable properties*. The two-consumer threshold ensures every primitive earned its place before extraction; IR-grows-under-evidence ensures every type / field / variant landed when a consumer demanded it.

**Closed-DU expansion empirical-test discipline.** Every variant addition fires F# exhaustiveness errors only at consumer match sites within the variant's module. Provable because the type system enforces total handling; if a consumer outside the variant's module needs reshaping, the seam is wrong.

**Bench-driven optimization protocol + iterator-logging-as-first-class-outcome.** Every refactor cites perf implications; every hot-path function has `Bench.scope`; every loop flows through `Bench` iterators; every counter via `Bench.recordSample`. Provable performance because the bench surface IS the perf evidence — operators reading the bench rollup table see structural per-call distribution, not aspirational claims.

These disciplines and the V2-driver KPI are not in tension. They are the same project. Every chapter agent who applies the disciplines is biasing decisions toward V2-driver by construction.

---

## Chapter sequencing under V2-driver KPI

The remaining chapters before V2-driver mode is reachable, in dependency order. Calendar estimates assume the operator's current ~3-day cadence; session-cadence estimates assume one session ≈ a few hours.

**Phase 1 — Π port keystone (chapter 3.5).** ~2 weeks at session cadence; ~1.5 calendar days at observed cadence.
- Slices α/β/γ — Π port realization for typed RawText / Json / Distributions emitters. Json + Distributions already shipped (chapter 3.7 slice ε); RawText is the remaining work.
- Slices θ/ι — RefactorLogEmitter + RefactorLogRender via XmlWriter (built-in obligation).
- Slice ζ — CatalogDiff smart constructor (exhaustive partition; A38 cash-out).
- Gate: T11 fully structural across all three siblings; RefactorLog round-trip property green.

**Phase 2 — Schema-as-driver (chapter 4.1.A + Tolerance + multi-env).** ~1 week at session cadence.
- SsdtDdlEmitter for production output (consumes chapter 3.5 Π port).
- Tolerance taxonomy (M4) — named DU + YAML config + per-environment quotient flip. The decision surface for "is this divergence acceptable per tolerance?"
- Multi-environment promotion property test — artifacts pairwise-equal across env pairs modulo named tolerances.
- Gate: PR canary integration test green on at least two environments; per-env tolerance config exercised.

**Phase 3 — Data-as-driver (chapter 4.1.B; CDC-critical).** ~2-3 weeks at session cadence.
- StaticSeedsEmitter, MigrationDependenciesEmitter, BootstrapEmitter (the data triumvirate).
- CDC discovery adapter — reads `sys.cdc_tables` from deployed DB; produces `CdcContext` for the emitters.
- **CDC silence on idempotent redeploy property test** — the highest-stakes deliverable in the entire chapter sequence. Asserts zero records in `cdc.change_tables` after second deploy.
- Gate: CDC-silence property green on every CDC-tracked table at the operator-reality canary scale.

**Phase 4 — Identity-as-driver (chapter 4.2; parallel-able with phase 3).** ~1-1.5 weeks at session cadence.
- UserFkReflowPass — discovers User mappings via UserMatchingStrategy DU; builds UserRemapContext.
- SourceTag value-object refactor of SsKey identity DU (makes the `V1Mapped` variant reachable from production input).
- Per-strategy coverage: ByEmail, BySsKey, ManualOverride, FallbackToSystemUser.
- Gate: every CreatedBy/UpdatedBy FK in target environment resolves to a valid target User; per-strategy property tests green.

**Phase 5 — Operational diagnostics (chapter 4.3).** ~1.5-2 weeks at session cadence.
- DecisionLogEmitter, OpportunitiesEmitter, ValidationsEmitter.
- Three-channel split (operator/auditor/developer) finally activated under real consumer pressure (the per-channel deferral from chapter 2 fires here).
- Gate: per-channel routing property green; Lineage trail audit completes for at least one full pass cycle at canary scale.

**Phase 6 — DACPAC if needed (chapter 3.x).** ~2.5 weeks at session cadence.
- DacFx adoption + DacpacEmitter.
- A37 named erasure axes (DACPAC erases index names, default constraint names, check constraint names).
- T1 binary-normal-form amendment (DACPAC binary representation IS the normal form for byte-equality at the package level).
- Gate: T11 structural between SSDT and DACPAC; canary asserts SSDT and DACPAC round-trip identically modulo erasure axes.
- **Conditional on deploy path.** If the operator's deploy path is SSDT-direct, defer indefinitely. If DACPAC is part of the deploy path (or becomes part of it), this is critical.

**Phase 7 — SnapshotRowsets (chapter 3.2).** ~1.5 weeks at session cadence.
- Closes JSON-projection-lossiness; A1 holds unconditionally for rowset-sourced SsKeys.
- Lifts `ICatalogReader` port (Position B → A; trigger fired with two consumers).
- Diagnostics-emission scaffolding for V1 silent drops (paired with chapter 4.3 channel routing).
- Gate: A1 unconditional bound property green for rowset-sourced SsKeys.

**Phase 8 — Pragmatic close (chapter 5+).** Indefinite cadence; consumer-pressure-driven.
- F# Analyzers SDK custom analyzer (slice ν from chapter 3.7) — complements 27 grep rules with AST detection.
- Coordinates Stage 2 typed `SchemaName` / `TableName` / `ColumnName` VOs (slice θ from chapter 3.7) — DDD VO win when adapter ripple is acceptable.
- Hex port lifts (`IArtifactSink`, `IDeployHost`) under genuine consumer demand.
- Cutover-day operator runbook (joint deliverable: solution architect + V2; bridges V1's `ssdt-playbook/` and V2's algebraic guarantees).
- V1 sunset planning (after cutover+30 + one full schema-evolution cycle on V2 emissions).

**Total: ~9-12 weeks at session cadence; ~6-9 days at observed 3-day cadence.** Plenty of headroom against the ~80-day cutover window.

---

## The governance protocol — R6 reframed under V2-driver KPI

The R6 split-brain governance rule (`DECISIONS 2026-05-22`) defined the transition mechanism: per-environment-per-artifact-type V2-driver transition gated on N=10 consecutive green canary runs plus operator sign-off. Under the V2-driver KPI, R6's transition mechanism is preserved unchanged; what shifts is the *direction* of the gate.

**Pre-V2-driver-KPI codification (R6 as written):** the floor is V2-augmented; V2-driver is aspirational; the gate is whether N=10 + operator sign-off justifies the climb. Per-environment-per-artifact-type means "we might progress here, might not, depends on evidence."

**Post-V2-driver-KPI codification (this document):** the destination is V2-driver; V2-augmented is the gate; the gate is whether N=10 + operator sign-off + per-axis property tests give *confidence in the V2 emission for production*. Per-environment-per-artifact-type means "we progress here as fast as the evidence supports."

The fallback ladder operates as before. V1-only is the safety net for the cutover window itself. V2-augmented is the cutover-window mode where V2 emits production artifacts AND V1 emits the same artifacts AND the canary asserts agreement modulo Tolerance — disagreement blocks the PR. V2-driver is the post-cutover mode where V2 is the sole emitter and V1 is archived.

The Tolerance taxonomy (M4) is the decision surface. Without it, "canary green" is binary; with it, "canary green modulo named tolerance" is a structured judgment. Every divergence either matches a named tolerance (acceptable; no change) or fails the canary (PR block). Tolerance entries are append-only DECISIONS amendments naming each acceptable divergence with a re-open trigger.

V1 sunset begins after:
1. The cutover survival window (cutover+30 days; V1 stays warm regardless).
2. All four environments (dev/qa/UAT/prod) have run V2 emissions for one full schema-evolution cycle.
3. The operator confirms V2 has been the sole emitter for that cycle without canary divergence.

---

## What this KPI is NOT

To prevent misinterpretation, the V2-driver KPI is explicitly NOT:

- **Not "ship faster."** The KPI is provable correctness, not delivery speed. The 80-day cutover window has substantial headroom; the constraint is rigor, not date.
- **Not "more code at any cost."** The two-consumer threshold + IR-grows-under-evidence + smart-product-choices framing all apply. V2-driver requires the right code with provable properties; it doesn't require duplicating V1 surfaces that don't add provability.
- **Not "less rigor in some places."** Per-axis stakes vary, but the discipline floor is uniform. Pillar 8 doesn't relax for chapter 4.3; performance-of-compliance is unacceptable in any chapter; the four-question analyses fire at every name and every LINT-ALLOW marker.
- **Not "skip V2-augmented."** V2-augmented IS the cutover-window mode AND the gate to V2-driver. The KPI says V2-augmented is not the destination; it doesn't say V2-augmented is skipped.
- **Not "V1 must sunset on a deadline."** V1 sunset is conditional on cutover+30 AND one full schema-evolution cycle on V2 AND operator confirmation. There is no sunset deadline; there are sunset preconditions.
- **Not a deadline-driven framing.** The cutover (~late July 2026) is V1-functional already. V2's job is to make every axis it owns provably correct. If the V2 deliverable for a given axis is not yet provably correct, V1 stays on that axis. There is no "ship by" pressure that compromises the KPI.

---

## Operating implications for chapter agents

What "bias all decisions toward V2-driver" means in slice-by-slice terms:

**When opening a chapter:** name the axes the chapter advances (per the per-axis stakes table). State explicitly which property tests the chapter will make hold. State explicitly what V1 capability the chapter is making V2 own. The chapter-open document (per `DECISIONS 2026-05-15`) is the strategic-frame artifact.

**When choosing the next slice:** prefer slices that advance an axis V2 is committing to own under V2-driver (chapters 4.1.A / 4.1.B / 4.2 / 4.3 / 3.5 keystone). Defer slices that are quality-of-life without advancing a V2-driver axis (chapter 3.7 slice θ Coordinates Stage 2; slice ξ ICatalogReader port lift without consumer pressure; etc.).

**When considering primitive extraction:** the two-consumer threshold still applies. Do NOT extract speculatively just because V2-driver might want it later. Extract when the second consumer arrives. The traverseCatalog deferral (chapter 3.7 slice γ) is the worked example: 0 consumers today → deferred-with-trigger; not extracted under V2-driver pressure because the buffer-based mapKinds covers demand.

**When reaching for a string-composition primitive:** the four-question analysis (pillar 7 amendment) fires. Performance-of-compliance is the named failure mode. The discipline-document path catches what the heuristic can't.

**When naming a new type / function / file:** the four-question analysis (pillar 8) fires. Domain-blind naming is the named failure mode. The cutover vocabulary (operators / DBAs / OutSystems platform / CDC / SQL Server admin guides / V2 algebra) is the source vocabulary.

**When considering a refactor that doesn't advance an axis:** ask "does this make a future V2-driver axis cheaper to ship?" If yes (foundation work, primitive extraction with consumer evidence, audit-finding cleanup that unblocks downstream work), proceed. If no (intellectual cleanup, nice-to-have rename, speculative abstraction), defer or skip.

**When considering whether to add a property test:** weight by the per-axis stakes table. CDC-silence on idempotent redeploy is the highest-leverage property in the entire chapter sequence. Schema fidelity is well-covered. User FK reflow needs deep coverage. Operational diagnostics needs lighter coverage.

**When considering deferring a chapter:** chapters 4.1.B / 4.2 / 4.3 / 3.x / 3.2 are NOT optional under V2-driver KPI. Sequence them; don't skip them. The only chapters genuinely deferable under V2-driver are slice χ (F# Analyzers SDK; independent), the chapter-3.7 quality-of-life slices (θ/ι/κ/λ/ν/ξ/ο/π without consumer pressure), and chapter 5+ pragmatic close items.

**When closing a chapter:** the eight-item chapter-close ritual (per `DECISIONS 2026-05-14`) executes. The five-agent epistemic-tier audit (per chapter-3.1 close) executes for architectural-frame chapters. The KPI evaluation: did this chapter advance a V2-driver axis? Are the property tests for that axis green? Is the per-axis stakes coverage met? AXIOMS amendments cashed at chapter close.

---

## Cross-references

This document supersedes the implicit "V2-augmented as floor; V2-driver as aspirational" framing in:

- `DECISIONS 2026-05-22 — R6: Split-brain governance rule for the dual-track cutover window` — R6's transition mechanism is preserved; the gate direction shifts per the codification above.

This document anchors:

- `KICKOFF.md` — fresh-agent first-message brief; V2-driver KPI is the operative target.
- `CLAUDE.md` — operating-disciplines table; V2-driver KPI joins the disciplines list.
- `HANDOFF.md` — chapter-handoff letter; chapter close evaluates progress against this KPI.
- `VISION.md` — strategic frame; V2-driver mode is the destination per this codification.
- `DECISIONS.md` — supreme operating discipline at the top; pillar references this document for the KPI framing.
- `AXIOMS.md` — formal system; per-axis property tests cite axioms (T1, T11, A1, A18, A35, A36, A39, A40, T1-binary-normal-form when chapter 3.x lands).
- `PLAYBOOK.md` — decision trees; the "When you reach for a name" / "When you reach for a string-composition primitive" trees apply uniformly under the KPI.
- `BACKLOG.md` — ~375-item inventory; chapters 3.5 / 4.1 / 4.2 / 4.3 / 3.x / 3.2 are critical-path under the KPI.

This document anchors-but-doesn't-supersede:

- **V1's roadmap.** V1's M1–M4 milestones (export verification, UAT-users guarantees, integrated workflow, performance/security) continue under V1's own governance. V2-driver KPI describes V2's destination; V1's roadmap is V1's concern.
- **V1's `handbook/` and `ssdt-playbook/`.** Operator-facing teaching material + per-change-tier mechanics. V2 doesn't duplicate; the cutover-day operator runbook (joint deliverable) bridges.
- **The cutover-day operator runbook.** Owned by the solution architect; V2 supports via canonical surfaces (KICKOFF / VISION / SPINE / DECISIONS) and via the canary as forcing function.

---

## Closing

V2's reason for existing is not to displace V1; it is to make every axis V1 ships provably correct so the cutover is verifiable end-to-end and so V1 sunset becomes a real plan post-cutover. V2-driver mode is the destination because provable correctness applied selectively (only to the axes V2 verifies) is a weaker correctness claim than provable correctness applied uniformly (to every axis V2 owns). The chapters before V2-driver mode is reached are all critical path; the sequencing determines when each axis becomes provably correct; the disciplines codified in the supreme operating discipline determine that each axis becomes provably correct *with substance*, not with the shape-of-correctness without the substance.

V2 makes the cutover trustworthy by being trustworthy itself. Every chapter that ships an axis V2 owns advances the destination. Every primitive that earns its place serves provable correctness. Every property test that holds at canary scale closes a class of failure. Every codification that captures a discipline makes future agents inherit the bias toward V2-driver without re-deriving it.

Hold the spine. The KPI is the spine.
