# Chapter B.4 open — operator-facing CLI + logging-format contract + LiveProfiler hygiene close

**Branch:** `claude/chapter-b4-opening-vGe7J`. **Predecessor:** chapter B.3 close (2026-05-19; merged via PR #550). **Chapter shape:** 8 substantive slices; axiom-first ordering (logging-format contract leads); hygiene close folded in (slices 2 + 3 retire LiveProfiler deck before CLI references the live-probe path).

This chapter operationalizes **V2_PRODUCTION_CUTOVER §7.5 (Phase B.4)** — Phase B's terminal chapter. V2 owns operator-facing extract / profile / full-export subcommands end-to-end; the V2 logging-format contract lands at slice 1 (`sidecar/projection/docs/logging-format.md`) and proposes two new L3 axioms (L3-X11: structured event-stream conformance; L3-X12: actionable events carry `suggestedConfig`) for the verifiability-triangle catalog, both landing at chapter close in Bucket A; `ModuleFilter` + `MetadataContractOverrides` ports carbon-copy from V1; the post-slice-6b dead-code SQL captures retire from `LiveProfiler`; the slice-1 composite-PK FK deferral cashes out via `projectTupleKeys`. Estimated effort: 1-2 weeks per §7.5; ~2-3 weeks with the hygiene close folded in.

## Strategic-frame axes

1. **Axiom-promotion is the chapter's load-bearing deliverable.** Slice 1 ships `sidecar/projection/docs/logging-format.md` first; everything downstream (CLI subcommands at slices 6-8; future operator-tooling consumers) consumes that contract. Under partial-chapter pressure, the axiom-shape document is the highest-leverage artifact — operator's downstream-tooling rewrite (per `V2_PRODUCTION_CUTOVER §7.5`) gates on its existence, not on the CLI's. The contract proposes two new L3-X axioms (L3-X11 + L3-X12) for catalog entry at chapter close per the verifiability-triangle audit cadence; the chapter-open scoping treated them as a single "L3-Operational-LoggingContract" placeholder, but the catalog convention is `L3-X#` under §3.4 — see `docs/logging-format.md §16` for the exact statements.

2. **Hygiene close folded in, not deferred.** Slices 2 + 3 retire the five `capture*` SQL surfaces (`captureAttributeRealities`, `captureColumnProfiles`, `captureForeignKeyRealities`, `captureCompositeUniqueCandidates`, `captureForeignKeyOrphanSamples` — all unused by `LiveProfiler.attach` post-slice-6b) and cash out the slice-1 composite-PK FK deferral (`Outcome = AmbiguousMapping` early-return; slice-6b's `projectTupleKeys` makes the multi-column Set.difference trivial). Both surface as chapter B.3 close-letter open questions; folding them into B.4's front cleans the LiveProfiler deck before slices 7-8 reference the live-probe path through CLI. Per the chapter-close-ritual discipline: chapter open absorbs the prior chapter's named open questions rather than punting them.

3. **CLI surface = operator's V2-driver acceptance surface.** `projection extract` / `projection profile` / `projection full-export` are the operator-touchable artifacts that Phase B leaves behind for the cutover dry-run. Pre-scope §7.5 names them; this chapter ships them. Each subcommand reads a unified config (per `Pipeline.Config` D9 connection-source resolution); each emits structured log events conforming to the slice-1 contract; each ships with a Docker-gated integration test.

4. **V1 inheritance via carbon-copy editorial discipline.** `ModuleFilter` (V1) and `MetadataContractOverrides` (V1) are slices 4 + 5 — carbon-copies per `DECISIONS 2026-05-16 (later) — V2 self-containment + carbon-copy editorial inheritance`. File-header citation comments name the V1 source + date; ADMIRE.md rows record the inheritance event; refactor at copy-time or in follow-up. V2 vocabulary applies (eventually) on every file.

5. **Pillar 9 — config-driven CLI carries OperatorIntent through registered transforms.** The CLI subcommands' configuration is operator-supplied intent; per pillar 9 + `L3-CC-Transform-Totality`, every operator-supplied choice surfaces as a registered transform with classification. `ModuleFilter` is `OperatorIntent of Selection`; `MetadataContractOverrides` is `OperatorIntent of Tightening` or `OperatorIntent of Emission` (per-axis classification at slice land time). The logging-format contract (slice 1) names the event categories that carry these classifications.

6. **Faker emitter stays deferred-with-trigger-met.** Per chapter B.3 close, the Active deferrals row remains `TRIGGER STRUCTURALLY MET — awaiting explicit promotion`. This chapter does not promote it. Rationale: cutover-window priority routes structural work to operator-facing CLI surface first; Faker awaits concrete consumer demand. No DECISIONS update; status unchanged.

7. **Chapter close = Phase B end-to-end exit gate.** Phase B's exit criterion (per `V2_PRODUCTION_CUTOVER §8.2`) requires the L3 catalog reaches its target bucket, V2 `full-export` runs against OSSYS and produces functionally-equivalent `osm_model.json` to V1, V2 profile probes produce functionally-equivalent `Profile` data, V2 logging format documented, ≥1 full end-to-end production dry-run completed. This chapter closes the structural surface; the dry-run is operator-paced and runs in parallel.

8. **No CDC governance dry-run in this chapter.** That work belongs to chapter 4.1.B's CDC-silence canary (shipped). This chapter is structural CLI shape; the per-CDC-table multi-redeploy property holds from chapter 4.1.B onward and is not re-validated here.

## Slice plan (8 substantive slices)

Slice naming uses `B.4.<n>.<axis>` for chapter coherence. Each slice ships its own commit + DECISIONS entry. **Axiom-first ordering**: slice 1 is the load-bearing axiom-promotion deliverable; slices 2 + 3 are the hygiene close; slices 4 + 5 are V1 inheritance ports; slices 6-8 are the CLI subcommand triad.

| Slice | Scope | Status |
|---|---|---|
| **1** B.4.1.logging-format-contract | `sidecar/projection/docs/logging-format.md` — defines V2's structured event categories (config / extract / profile / transform / emit / deploy / canary / summary), envelope (runId / ts / level / category / code / phase / source / ssKey / stepId / payload), encoding (UTF-8; line-delimited JSON to stderr; one event per line), classification taxonomies carbon-copied verbatim from V1 (TighteningRationale + ProbeOutcome + TransformSource), the terminal `summary.runComplete` event + roll-up collapse algorithm, the `suggestedConfig` actionable-payload discipline, and 12 banned antipatterns. Proposes L3-X11 + L3-X12 for catalog entry at chapter close. The doc lands BEFORE downstream subcommands consume the contract; subcommands at slices 6-8 emit events conforming to it. | **DONE** |
| **2** B.4.2.capture-retirement | Retire the five unused `capture*` SQL surfaces from `LiveProfiler` (`captureAttributeRealities` / `captureColumnProfiles` / `captureForeignKeyRealities` / `captureCompositeUniqueCandidates` / `captureForeignKeyOrphanSamples`) — all dead post-slice-6b. The seven `LiveProfilerIntegrationTests` direct-SQL tests update to assert on `Cache.derive*` output (cache equivalence is structurally proven; SQL parity-witness no longer earns its keep). Net: ~1500 LOC of SQL-capture code retires; tests reshape but count preserved. | not-started |
| **3** B.4.3.composite-pk-fk | **RESOLVED OUT-OF-SCOPE.** At slice open, surfacing the design choice to the principal-PO produced the canonical answer: composite primary keys are not an OS use case the operator has encountered; the slice's premise (V2 probes FK reality against composite-PK targets) does not match operator-reality demand. The slice-1 `AmbiguousMapping` outcome stands as the correct answer for the degenerate case. Documentation-only change: clarifying comments at the two `ambiguous` branches in `Cache.deriveForeignKeyRealitiesWith` + `Cache.deriveForeignKeyOrphanSamplesWith` point at `DECISIONS 2026-05-19 (slice B.4.3.composite-pk-fk)`. The cross-module FK reserved IR refinement deferral remains separate and untouched. Chapter B.4 effective slice count: 7 (no test count change). | **DONE — RESOLVED OUT-OF-SCOPE** |
| **4** B.4.4.module-filter-port | Carbon-copy `ModuleFilter` from V1 per `DECISIONS 2026-05-16 (later)`. New `Projection.Adapters.Osm/ModuleFilter.fs` or `Projection.Core/ModuleFilter.fs` per the V1 source's natural V2 location. File-header citation + ADMIRE row. Operator-supplied include/exclude lists for module-scoped extract / profile / emit. Pillar 9 classification: `OperatorIntent of Selection`. | not-started |
| **5** B.4.5.metadata-contract-overrides | Carbon-copy `MetadataContractOverrides` from V1. New module / file per V1 source's natural V2 location. Operator-supplied overrides for the metadata-contract surface (per-attribute tightening / emission overrides). File-header citation + ADMIRE row. Pillar 9 classification: per-axis (`OperatorIntent of Tightening` or `OperatorIntent of Emission`) at slice land time. | not-started |
| **6** B.4.6.projection-extract | `projection extract --config <path>` CLI subcommand. Connection-source resolution per D9 (config-only; no env-var override at this chapter). Reads OSSYS catalog (via `CatalogReader.parse`) under config-supplied `ModuleFilter` (slice 4); writes osm_model.json to config-supplied output path. Docker-gated integration test exercises a small OSSYS-shaped fixture. Logging conforms to slice-1 contract. | not-started |
| **7** B.4.7.projection-profile | `projection profile --config <path>` CLI subcommand. Runs `LiveProfiler.attach` against the config-supplied live source (with `SqlProfilerOptions` sampling cap per slice-B.3.7); writes Profile JSON to config-supplied output path. Docker-gated integration test exercises a small live SQL fixture. Logging conforms to slice-1 contract. | not-started |
| **8** B.4.8.projection-full-export | `projection full-export --config <path>` CLI subcommand. Composition: extract + profile + emit in one orchestrated flow. The operator's single-command shape per `V2_PRODUCTION_CUTOVER §7.5` task list. Wires together slices 6 + 7 + the existing `projection emit --config` (chapter 4.x). Docker-gated integration test exercises end-to-end OSSYS-to-SSDT flow. Logging conforms to slice-1 contract. **Phase B exit gate** runs here. | not-started |

**Chapter close ritual** runs after slice 8. Eight items per `CLAUDE.md` operating disciplines: Active deferrals scan (verify Faker still trigger-met / consumer-pressure absent; check composite-PK FK rolled off; verify five retired captures no longer linked); contract-vs-implementation walk on the new CLI subcommands; CLAUDE.md / README.md staleness (README pointer at CLI surface); HANDOFF + close-doc scope; fresh-eye walk; operating-disciplines table currency; **V1-input-envelope walk** (V2 vs V1 osm_model.json + Profile JSON byte-comparison for the canary fixture; this is the operational arm of the §8.2 functional-equivalence gate); per-axis-stakes evaluation against V2_DRIVER (this chapter advances Schema + Data + Operational-Diagnostics + Identity axes; no advance on User-FK or RefactorLog).

**Bonus close item**: L3-X11 + L3-X12 catalog entry. Slice 1's contract proposes both axioms; chapter close lands the entries in `AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md §3.4` + coverage-map row in §10.4 (Bucket A — the doc landed; CLI subcommands emit conforming events; property tests + Docker-gated integration tests verify).

## Out of scope

- **Faker emitter promotion.** Per chapter B.3 close, the Active deferrals trigger is structurally met (all four `ADMIRE.md`-named gating evidence chain nodes shipped). This chapter does NOT promote; status remains `TRIGGER STRUCTURALLY MET — awaiting explicit promotion`. Re-evaluate at chapter B.5 or 5+ open under concrete consumer demand (per the principal-PO's cutover-window priority).
- **`MaxIdentityValueQueryBuilder`** (Q1 cash-out per V2_PRODUCTION_CUTOVER §7.4 B.3 task list). Independent of CLI shape; surfaces under chapter 4.x consumer pressure if at all. Not blocking Phase B exit.
- **`ProfilingQueryExecutor` (672 C# LOC orchestration port).** Per chapter B.3 close: V2 already has F#-shaped composition primitives; orchestration shape lifts inline at each `LiveProfiler` capture function. The 672 C# LOC is V1's plumbing; V2's plumbing is the Task-monad + Result-monad composition. Cut at this chapter open.
- **`LiveOssysConnection` variant of `SnapshotSource`.** Reserved in `SnapshotSource` DU (`CatalogReader.fs`); surfaces in chapter 5+ when canary's deployment-validation arc materializes. This chapter ships `projection extract` against `SnapshotRowsets` (chapter 3.2) + `SnapshotJson` only.
- **Cross-module FK IR refinement.** Reserved DU variant remains; trigger condition (DacpacEmitter cross-database reference; multi-V1-SS-instance OssysOriginal) does not fire in this chapter.
- **CSV adapter for `ManualOverride` (UserMapLoader).** Chapter 4.2 deferral; surfaces under concrete operator workflow demand. Not blocking Phase B exit.
- **CDC governance dry-run.** Per chapter 4.1.B close + V2_DRIVER per-axis stakes: dry-run runs on operator's production-CDC-enabled tables, owned by operator, scheduled jointly. Not gated on this chapter's close.

## Dependency map

```
Slice 1 (logging-format contract)
  └─ no upstream dependency
  └─ DOWNSTREAM consumer: slices 6 + 7 + 8 emit conforming events
     (CLI subcommands' log output must match the contract or fail
      chapter close)

Slice 2 (capture retirement)
  └─ no upstream dependency (post-slice-6b dead code retires)
  └─ DOWNSTREAM consumer: slice 7's `projection profile` subcommand
     references the live-probe path via `LiveProfiler.attach`; retiring
     dead captures first prevents the new CLI surface from accidentally
     re-introducing references

Slice 3 (composite-PK FK cash-out)
  └─ depends on slice-6b's `projectTupleKeys` (shipped chapter B.3)
  └─ DOWNSTREAM consumer: none in this chapter (FK realities improve
     for composite-PK fixtures; tightening rules already handle
     `AmbiguousMapping` as defer-to-empty; cash-out is correctness
     amendment, not new CLI surface)

Slice 4 (ModuleFilter port)
  └─ no upstream dependency (carbon-copy)
  └─ DOWNSTREAM consumer: slice 6's `projection extract` reads
     ModuleFilter from config

Slice 5 (MetadataContractOverrides port)
  └─ no upstream dependency (carbon-copy)
  └─ DOWNSTREAM consumer: slice 8's `projection full-export` may
     read MetadataContractOverrides from config (per V1 precedent);
     slice 6 + 7 do NOT consume (extract / profile are pre-overrides)

Slice 6 (projection extract)
  └─ depends on slice 4 (ModuleFilter); slice 1 (logging contract)
  └─ DOWNSTREAM consumer: slice 8 composition

Slice 7 (projection profile)
  └─ depends on slice 1 (logging contract); slice 2 (capture-retired
     LiveProfiler surface)
  └─ DOWNSTREAM consumer: slice 8 composition

Slice 8 (projection full-export)
  └─ depends on slices 1 + 4 + 5 + 6 + 7
  └─ Phase B exit gate runs here
```

## Anchor commitments preserved across the chapter

These don't shift during B.4. If a slice trips one, the discipline is to write the amendment first.

- **F#-pure-core / no-I/O-in-Core.** CLI subcommands live in `Projection.Cli`; live SQL probes live in `Projection.Adapters.Sql`. Core stays I/O-free.
- **A18 amended.** CLI subcommands route through `Compose.run` (chapter A.4.7'); no emitter consumes Policy directly.
- **Pillar 9 — DataIntent / OperatorIntent classification.** Every CLI option that affects projection output is `OperatorIntent` with explicit `OverlayAxis` classification (Selection / Emission / Insertion / Tightening). The logging-format contract names how the classification surfaces in event payload.
- **Smart-constructor-FIRST.** Any new IR aggregate (none expected this chapter) gets `.create` before the slice that consumes it.
- **EvidenceCache discovery-then-derive.** Slice 7's `projection profile` consumes the existing `LiveProfiler.attach` cache pipeline; no new SQL probes land. New evidence shapes (none expected) would land as `Cache.deriveX` per the chapter B.3 codification.
- **V2 self-containment.** Carbon-copy discipline at slices 4 + 5; file-header citations + ADMIRE rows; no V1 ProjectReference.
- **Sibling-wrapper discipline.** CLI subcommands likely surface `runX` + `runXWith config` pairs; the wrapper supplies the config-default path; the canonical surface is the With variant.
- **R6 split-brain governance.** V2 emits-but-doesn't-ship while V1 owns the production write path. CLI subcommands write to disk; they do NOT touch the production DB. The canary asserts equivalence; this chapter does not change R6.
- **V1 stays warm through cutover+30.** Phase B exit at slice 8 close does NOT mean V1 retires; V1 sunset begins at chapter 5+.

## Test baseline at chapter open

- **1695 / 1695 non-Docker** baseline at B.3 close.
- **33 / 33 LiveProfiler Docker-gated** integration tests at B.3 close.
- **0 build warnings** under `TreatWarningsAsErrors=true`.

Slice 2 reshapes 7 tests (SQL-direct → cache-derivation assertions); slice 3 adds 1+ composite-PK FK test; slices 6-8 add 3 Docker-gated CLI integration tests (one per subcommand). Expected close: ~1696-1700 non-Docker + ~36-37 Docker-gated; build clean.

## Reading order for the slice agent (recurrence reference)

1. **This document** (~5 minutes; the chapter strategic frame + slice plan).
2. **`HANDOFF.md`** — latest letter at the top; the next-agent orientation block from chapter B.3 close.
3. **`CHAPTER_B_3_CLOSE.md`** — the substantive contributions that earn this chapter (DATA-axis silent-default closure; EvidenceCache substrate; Faker evidence-chain complete).
4. **`V2_PRODUCTION_CUTOVER.md §7.5`** — the chapter's task-list reference.
5. **`V2_DRIVER.md` per-axis stakes table** — verify each slice advances at least one named axis; logging-format contract sits under Operational diagnostics (lower verification depth) but the L3 promotion is structural.
6. **`AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md §3.4 (L3-X axioms)** — slice 1 reserves L3-X11 + L3-X12; chapter close adds them to the catalog. Read the existing L3-X1 through L3-X10 statements to mirror the convention.
7. **Active deferrals index at `DECISIONS.md`** — slice 2 + 3 closing-confirmation; Faker row stays unchanged.
8. **`src/Projection.Cli/Program.fs`** — current CLI surface; slices 6-8 extend it.
9. **`src/Projection.Pipeline/Config.fs`** — current `Config` record; slices 4 + 5 extend it.
10. **`src/Projection.Adapters.Sql/LiveProfiler.fs`** — current capture surfaces; slice 2 retires 5 of them; slice 3 amends FK reality for composite PKs.

When you open slice 1, the work is bounded: a markdown doc defining 6 event categories + their properties + their encoding. Read the existing `Bench` benchmark JSON output for the precedent on UTF-8 / line-delimited encoding. The doc's structural role: every CLI subcommand emits events conforming to this contract; chapter close verifies conformance.

When you open slices 6-8, the discipline is: every operator-supplied config knob gets a pillar 9 classification at slice land time. The classification surfaces in the logging events per the slice-1 contract. Docker-gated integration tests exercise the conformance.

Hold the spine. Chapter B.4 closes Phase B's structural surface. The cutover dry-run runs in parallel; the structural commitments earn the dry-run its confidence.

— The chapter B.4 architect (opening).
