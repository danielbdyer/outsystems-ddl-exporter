# Appendix D — Implementation Sequencing Plan from VISION.md

**Date:** 2026-05-08
**Reviewing:** VISION.md @ commit `2fb51ef`
**Brief:** Software-architect implementation plan derived from the vision. Sequence chapters 3 and 4. Address F#/C# boundaries, test strategy per phase, and propose a fallback ladder for the cutover.
**Synthesis location:** `VISION_REVIEW.md`

---

# Chapter 3 + Chapter 4 implementation plan

## 1. Chapter 3 sequencing — argued order

Run chapter 3 as a four-phase arc: **(3.1) SnapshotRowsets → (3.2) read-side adapter → (3.3) DacpacEmitter → (3.4) canary closure → (3.5) RefactorLogEmitter**. The ordering is forced by two real dependencies and one risk-management choice.

**3.1 SnapshotRowsets first** (5–6 substantive slices). The DACPAC pre-scope §1 names "real Catalog flowing end-to-end through a pipeline exercising T11 sibling-Π commutativity on real metadata" as the trigger that fired the DacpacEmitter cash-out. That trigger is louder when the Catalog actually carries `EntitySsKey`, `EspaceKind`, and `IsSystemEntity`. The SnapshotRowsets pre-scope §7 is explicit: open this **parallel-to or before canary**, so canary inherits a Catalog with full SsKey carriage rather than the synthesized `OS_KIND_*` SsKeys — otherwise canary's compare-by-SsKey rests on a placeholder identity and any A1 violation surfaces only after the canary chapter closes. SnapshotRowsets also has the smallest surface (no DacFx, no testcontainers); landing it first burns down the JSON-projection-lossiness class before adding canary's mechanical complexity.

**3.2 Read-side adapter second** (the canary's "back" half). Pre-scope DACPAC §5 explicitly recommends "read-side adapter first, then DacpacEmitter" inside the canary chapter, on the argument that the read-side has two consumers from day one (canary round-trip and future drift detection) while DacpacEmitter has only canary as near-term consumer. The read-side defines the round-trip target before the emit shape is committed. Build it under `src/Projection.Adapters.Sql.ReadSide/` (slot already reserved in README §Layout) consuming `DacServices.Extract` output as the input substrate.

**3.3 DacpacEmitter third**. Now there is a Catalog with real SsKeys (3.1) and a round-trip target to be tested against (3.2). Implement against the minimal first slice in DACPAC pre-scope §5: single-table Catalog, content-equality via DacFx round-trip rather than byte-equality. **Defer byte-determinism to a post-pass** (see §4 below).

**3.4 Canary closure**. Wire 3.1 + 3.2 + 3.3 inside `Projection.Pipeline` (C#): emit dacpac → testcontainers ephemeral SQL Server → `DacServices.Deploy` → read-side `Extract` → compare by SsKey root. T11 commutativity test surface lights up here. This is the chapter's structural deliverable — the algebra goes from claim to proof.

**3.5 RefactorLogEmitter last** in chapter 3. VISION §"five demands and the algebraic moves" couples this to A1 + UUIDv5 + cross-version identity. SnapshotRowsets must land first (the V1 SSKey Guids it surfaces are exactly UUIDv5's input space). Defer to chapter 3 tail rather than chapter 4 because the cutover demand (rename history surviving across schema versions) is in chapter-3 territory, not chapter-4 trajectory territory.

**Cross-module FK** is the tactical-completeness step from `HANDOFF.md`. Land it inside slice 2 or 3 of SnapshotRowsets (rule 16's same-module assumption tests under actual SsKey carriage) — not as a standalone slice.

## 2. Chapter 4 sequencing

**4.1 Data-emission triumvirate** (StaticSeeds → MigrationDependencies → Bootstrap), in that order. StaticSeeds is structurally smallest: VISION §"sibling chorus" already names it, and DACPAC §2 routes Static populations to it. EmissionPolicy DU (`AllRemaining` default / `AllExceptStatic` / `AllData`) lives in `Projection.Core/Policy.fs` because it is intent, not evidence; emitters dispatch under it but never consume Policy directly (A18 amended). Define the DU when StaticSeeds lands, expand variants when MigrationDependencies and Bootstrap force them.

**4.2 User FK reflow as algebraic Policy**. VISION §"forcing function" names this as cutover-adjacent work; it is post-cutover-substrate machinery. Implement as a pass under `Projection.Core/Passes/UserFkReflow.fs` consuming a new Policy axis (UserMatchingStrategy: ByEmail / BySsKey / ManualOverride). Reflow is environment-specific intent; multi-environment Profile/Policy demand from VISION is satisfied by the same algebra running against four (Profile, Policy) pairs.

**4.3 Drift detection**. Composes 3.2's read-side adapter with 4.1's data-emission baseline: extract deployed schema → compare to source Catalog → surface delta as `Diagnostics` finding. Lives in `Projection.Pipeline` (C#), reuses canary's read-side. No new module.

**4.4 Operational diagnostics V2**. V1's decision-log/opportunities/validations surfaces. Implement as a chapter-4 deliverable consuming the existing `Diagnostics<'a>` writer; the three-channel split (operator/auditor/developer) deferred from chapter 2 fires here if the consumers diverge.

## 3. Critical files to touch / create

The directory layout you guessed is partly off. Actual layout: there is no `Projection.Emitters/` — sibling Π's live under `src/Projection.Targets.{SSDT,Json,Distributions}/`. There is no `Projection.Pipeline/` yet (slot reserved, README §Layout). Adapters live under `src/Projection.Adapters.{Sql,Osm}/`. Use:

**Chapter 3**:
- `src/Projection.Adapters.Osm/CatalogReader.fs` — extend `SnapshotSource` DU with `SnapshotRowsets of RowsetBundle`; add `parseRowsetBundle`.
- `src/Projection.Adapters.Osm/RowsetBundle.fs` (new) — F# DTO records for rowsets 1–3 first.
- `src/Projection.Adapters.Sql.ReadSide/` (new F# project) — DACPAC-extract → Catalog translation.
- `src/Projection.Targets.SSDT/DacpacEmitter.fs` (new) — pure F# `Catalog -> Result<byte[]>`.
- `src/Projection.Targets.SSDT/RefactorLogEmitter.fs` (new) — sibling Π for rename records.
- `src/Projection.Pipeline/` (new C# project) — DacFx wrapper, testcontainers harness, canary orchestration.

**Chapter 4**:
- `src/Projection.Core/Policy.fs` — extend with `EmissionPolicy` DU, `UserMatchingStrategy`.
- `src/Projection.Targets.StaticSeeds/StaticSeedsEmitter.fs` (new project + module).
- `src/Projection.Targets.MigrationDependencies/`, `src/Projection.Targets.Bootstrap/` (new projects).
- `src/Projection.Core/Passes/UserFkReflow.fs` (new pass).
- Drift detection lives inside `Projection.Pipeline` (C#); no new F# project.

Tests mirror existing convention under `tests/Projection.Tests/`: `RowsetBundleTests.fs`, `DacpacEmitterTests.fs`, `ReadSideAdapterTests.fs`, `CanaryRoundTripTests.fs`, `RefactorLogEmitterTests.fs`, `StaticSeedsEmitterTests.fs`, `UserFkReflowTests.fs`, `EmissionPolicyTests.fs`. Cross-source parity (JSON ↔ Rowsets) lives in `OsmCatalogReaderDifferentialTests.fs` (extend, don't fork).

## 4. Architectural trade-offs

**F#/C# boundary for DacFx.** Land it where DACPAC pre-scope §1 placed it: F# `DacpacEmitter` produces T-SQL DDL strings; C# wrapper inside `Projection.Pipeline` (or a dedicated `Projection.Targets.SSDT.Dacpac` C# project) owns `TSqlModel.AddObjects`, `BuildPackage`, `IDisposable` lifetimes, and exception-driven validation. Seam is `Catalog -> Result<byte[]>`. Rationale: DacFx's API is dictionary-of-properties + mutation-by-script-add + disposable-scopes — the exact "object-instantiation-heavy, foreign-API-I/O" shape `DECISIONS 2026-05-09` sends to C#. F# Π stays pure; the algebra holds. **Recommend the dedicated C# project over folding it into `Projection.Pipeline`** for module cohesion (the SSDT target's binary half).

**Canary pipeline language.** C#, per `DECISIONS 2026-05-15` and the slot reserved in README. Testcontainers, DacFx deployment, and ephemeral SQL Server orchestration are all natural-language-of-the-boundary C# territory. F# core consumes the Catalog round-trip result as a value type.

**Determinism boundary.** DACPAC pre-scope §3 names three options; **pick option (b) for the algebra and option (a) for operations**. T1 amends to "byte-determinism for text/JSON; content-determinism via DacFx model-API equality for binary." The byte-canonicalization (Origin.xml timestamp pinning, model.xml checksum recomputation, zip-entry timestamp pinning) lives as a **post-pass inside the C# DacFx wrapper**, not inside the F# emitter. Rationale: the determinization touches zip internals and is fragile under DacFx version bumps; isolating it in C# keeps the F# emitter free of `System.IO.Packaging` surgery, and the post-pass is a clean unit to disable when DacFx eventually ships byte-stable output natively. The compare-by-SsKey canary layer never inspects bytes — it round-trips through the model API, so canary correctness is independent of the determinization pass.

## 5. Test strategy per phase

Chapter 3 ratio target stays ~1:1.7 source:test.

- **3.1 SnapshotRowsets** — fixture/example tests dominant (rowset literals as F# records, mirroring session-18 minimal fixture). Cross-source parity tests (JSON ↔ Rowsets) are differential. One property test for SsKey-shape divergence handling.
- **3.2 Read-side adapter** — golden-file tests against pinned `.dacpac` fixtures; differential tests asserting `Extract → Catalog` round-trips equal to source Catalog modulo the documented divergences.
- **3.3 DacpacEmitter** — three test classes: (1) golden T-SQL output per Catalog shape; (2) DacFx round-trip property tests (`emit → load → enumerate → equal source`) — this is where property tests fit, sweeping permutation invariance and structural-commitment; (3) T11 commutativity tests asserting `RawTextEmitter` and `DacpacEmitter` agree on the SsKey-root-mention property over generated catalogs.
- **3.4 Canary** — integration tests via testcontainers; expensive, opt-in via `[<Trait("Category", "Integration")>]`. One round-trip integration test per Catalog shape class (single-table, multi-table-no-FK, FK, indexes, composite PK, cross-module). Property tests do not fit here — too slow.
- **3.5 RefactorLogEmitter** — property tests for UUIDv5 determinism (same V1 SSKey Guid → same V2 SsKey, regardless of run); golden-file tests for refactor-log XML.

Chapter 4:
- **4.1 Data-emission triumvirate** — golden-file tests per emitter; property tests for content-equality determinism (T1 binding for data emissions); EmissionPolicy DU exhaustiveness verified by F# match.
- **4.2 User FK reflow** — pure-pass property tests dominate (algebra: same Profile + same Policy → same reflow). Per-strategy golden examples.
- **4.3 Drift detection** — integration tests against drifted ephemeral DBs.
- **4.4 Diagnostics V2** — fixture tests for sink shapes; property tests for writer composition laws.

## 6. Fallback plan for cutover

VISION names no fallback. Propose:

**V1-only path**: V1's existing extraction + topologically-sorted two-phase inserts + Azure DevOps PR pipeline runs the cutover. Trust mechanism is "implicit correctness verified by experience" (per VISION §"What V1 already does"). Loss: no canary; every emission is hand-verified; cross-version identity held by V1's existing SSKey discipline rather than V2's UUIDv5 + RefactorLog.

**V2-augmented path**: V1 still drives the cutover pipeline; V2 runs the canary as a verification-only sidecar — emit + deploy-ephemeral + read-back + compare-to-V1-output. If canary disagrees with V1's emission, the PR blocks. V2 owns no production write path; V2 owns the verification surface only. Lower risk than V2-as-driver; preserves V1's empirical trust while V2's algebra earns its keep on the verification axis.

**V2-driver path**: V2 emits dacpac + StaticSeeds + MigrationDependencies + Bootstrap; V1 retired from cutover. Highest payoff (full sovereignty per VISION §"What V2 ultimately is"); highest risk (V2's algebra unproven on production-scale 300-table workload).

**Decision criterion at T-30 days**: V2-driver requires (a) chapter 3 closed with canary green on full 300-table Catalog; (b) chapter 4.1 (data triumvirate) shipping; (c) chapter 4.2 (user FK reflow) shipping; (d) at least one full dry-run against UAT environment with cross-environment Profile/Policy pairs producing structurally-consistent artifacts. If any of (a)–(d) is yellow at T-30, drop to V2-augmented (canary-only). If V2-augmented's canary is unstable at T-15, drop to V1-only and ship V2 as post-cutover substrate. **Hard rule: never drop V1 from the path between T-30 and cutover-day**; V1 stays warm even on the V2-driver path until cutover+30 days.

## 7. Three explicit non-goals

VISION's "informational widening" §95–106 contains GraphQL emitters, FakerEmitter, AI-agent substrate, recipes, and per-developer Docker compose. **Defer all four families through chapter 4**:

1. **GraphQL schema and resolver emitters — defer.** VISION names them as sibling Π's emerging without rebuilds; that claim depends on the algebraic core holding. Cutover does not exercise GraphQL; cutover exercises DACPAC + data emissions + canary. GraphQL pays no cutover dividend. Risk of premature scope: a GraphQL emitter with no consumer drives speculative IR refinements that contaminate the cutover-critical chorus. Re-open trigger: first non-cutover consumer demands a Catalog projection via GraphQL.

2. **FakerEmitter and synthetic-data quality scoring — defer.** Already deferred per `HANDOFF.md` "lower-priority, watch for accidental fires" — gates on third evidence type, which has not landed. Profile-shaped synthesis is post-cutover-substrate, not cutover machinery; production cutover uses real data with environment-specific Profile, not synthesized data. Re-open trigger: third evidence type lands, OR per-developer local Docker SQL Server demand surfaces in chapter 5+.

3. **AI-agent substrate (Playwright agents, code agents, domain copilots) and "recipes" emission (docker compose, provisioning scripts).** VISION §"informational widening" frames these as post-cutover trajectory; they explicitly are not load-bearing for the cutover. Building them inside chapters 3–4 would inflate the cutover-critical surface with capabilities whose consumers do not yet exist (per the IR-grows-under-evidence-not-speculation discipline). Re-open trigger: a real AI-agent consumer (operator-decided) demands a Catalog projection, OR a per-developer local environment becomes a chapter goal.

The defer-justification compounds: each non-goal is a sibling Π or substrate axis whose absence does not block the cutover. The cutover is the load test; everything not on its critical path waits until the load test is passed.

### Critical Files for Implementation

- `sidecar/projection/src/Projection.Adapters.Osm/CatalogReader.fs`
- `sidecar/projection/src/Projection.Targets.SSDT/RawTextEmitter.fs`
- `sidecar/projection/src/Projection.Core/Catalog.fs`
- `sidecar/projection/src/Projection.Core/Policy.fs`
- `src/Osm.Pipeline/SqlExtraction/SnapshotJsonBuilder.cs`
