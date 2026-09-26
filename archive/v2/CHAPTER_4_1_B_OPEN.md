# Chapter 4.1.B open — CDC-aware data triumvirate

**Sessions:** opens with this document. **Posture:** Phase 3 of V2-driver KPI critical path (per `V2_DRIVER.md` — the highest-leverage single deliverable in the entire chapter sequence). **Predecessors:** chapters 3.1 (canary substrate + AsyncStream), 3.5 (Π port realization), 3.6 (LineageEvent typed payloads), 3.7 (audit-cleanup hygiene + V2-driver KPI codified), 4.1.A (SSDT DDL emitter; sibling-Π emitter on the production schema axis), and the M4 Tolerance taxonomy slice α (typed equivalence-class definition).

This is the chapter-open document per `DECISIONS 2026-05-15 — Strategic frame for the OSSYS implementation chapter` (multi-session chapters earn this discipline at chapter open). The companion close synthesis lands at `CHAPTER_4_1_B_CLOSE.md` when this chapter ends. **Operational pre-scope:** `CHAPTER_4_PRESCOPE_DATA_TRIUMVIRATE.md` — the implementation-grade plan with §1 scope, §2 the three emitters, §3 EmissionPolicy DU, §4 CdcAwareness, §5 topologically-sorted two-phase insertion, §6 change-detection MERGE shape, §7 ordering constraints, §8 slice-by-slice breakdown.

---

## Why this chapter

Chapter 4.1.B delivers V2's **data-emission half** of the production-deployment chorus. V1 already performs topologically-sorted two-phase MERGE inserts (`Osm.Emission/PhasedDynamicEntityInsertGenerator.cs:88-148`); V2 inherits the empirical foundation and re-expresses it as the algebraic pair `Catalog × Profile → Result<DataInsertScript ArtifactByKind, EmitError>`, with three named projection sites that share an FK-aware ordering and a **change-detection MERGE shape** that closes the CDC-noise hole in V1's `WHEN MATCHED THEN UPDATE SET` unconditional path.

**The cutover stake** (per `VISION.md` §"The forcing function" + `V2_DRIVER.md` per-axis correctness stakes table — ranked HIGHEST):

> CDC running in production with features depending on it; spurious change records would disrupt those features. **The CDC-silence-on-idempotent-redeploy property test is the highest-leverage single deliverable in the entire chapter sequence.**

V1's MERGE always issues UPDATE on match; a redeploy of identical content fires UPDATE → CDC capture-process emits a row → consuming features see a "change" that didn't happen. V2 closes this by adding the change-detection predicate to `WHEN MATCHED` so identical-content redeploys are no-ops on tracked tables. **T1 byte-determinism alone does not deliver this**: CDC noise comes from SQL Server's MERGE applying UPDATE, not from V2's emission. The composition is **T1 × idempotent-MERGE × correct change-detection-predicate**.

The load-bearing acceptance criterion:

```
deploy → enable CDC → redeploy same artifact
   → cdc.fn_cdc_get_all_changes_<capture_instance>(...) returns ∅ on every tracked table
```

---

## Strategic frame — eight axes named at chapter open

Per the OSSYS / chapter-4.1.A precedent, multi-session chapters name their load-bearing axes at chapter open before substantive slices begin.

1. **DDD — `DataInsertRow` is the orderable unit; `DataInsertScript` is the per-kind aggregate.** Each row carries `KindKey × Identifier × Values × DeferredFkSet` (the cycle-breaking metadata is part of the row's identity). The `DataInsertScript` aggregates `Phase1Merges + Phase2Updates`. Per pre-scope §2.4: the structured form survives the `Discrete-rationale DUs absorb continuous evidence` discipline — rather than carry a `RawSql` flag, the shape NAMES the orderable units. Pillar 8 (concept-shaped naming): `DataInsertScript` is the data-axis sibling of SSDT's `Statement list` shape; "Script" names the artifact per V1 convention without action-shaping.

2. **FP — three emitters share one algebraic signature, dispatched by composition.** `StaticSeedsEmitter.emit : Catalog -> Profile -> Result<ArtifactByKind<DataInsertScript>>`; `MigrationDependenciesEmitter.emit` adds a `MigrationDependencyContext` sibling input; `BootstrapEmitter.emit` adds a `UserRemapContext` sibling input. **A18 amended holds across all three**: emitter signatures literally cannot type-check with a `Policy` parameter. The `DataEmissionComposer` (the dispatcher) reads `Policy.Emission.DataComposition` and chooses which emitters fire; emitters do not. The composition layer IS where Policy lives; emitters consume Catalog × Profile (× boundary-supplied evidence siblings).

3. **Hardcore (no-string-concatenation) — MERGE shape flows through a typed `MergeStatement` AST.** Per pillar 7's gold-standard library precedence (substantive-rationale amendment): the canonical use-case-specific library for the MERGE shape is ScriptDom's `MergeStatement` typed AST + `Sql160ScriptGenerator`. Slice α may ship a transitional `MergeShape` value type (parallel to `Statement` in SSDT) that the renderer converts via the same path as chapter-4.1.A used (`ScriptDomBuild.buildMergeStatement` + `ScriptDomGenerate.generateOne`). The change-detection predicate (see axis 6) builds inline through the typed AST; no `String.Concat` at the predicate-construction site.

4. **Streaming — bench observability per emitter, per phase.** Each emitter's `Bench.scope` records its `Catalog → Result` traversal; the composer's topological ordering scope records the cross-emitter interleave; the renderer's `Bench.streamProbe` records GO-batched output. Per-row scopes (`Bench.iterMap "data.row"`) surface in the rollup table. The 50k-row × 300-table operator-reality canary is the chapter's primary perf forcing function (this chapter is where the data-axis path earns its operator-reality bench evidence).

5. **Hexagonal — the CDC discovery extension lives in the read-side adapter, never in Core.** Pre-scope §4.3: `CdcAwareness` is discovered by an adapter against `cdc.change_tables`. The adapter returns `Result<CdcAwareness>`; Core consumes the typed value via `Profile.CdcAwareness`. Core knows nothing about how CDC is enabled or whether the deployed environment uses CT vs CDC; that's adapter territory. F#-pure-core / no-I/O-in-Core holds.

6. **Built-in obligation — change-detection-predicate MERGE shape closes CDC-silence-on-idempotent-redeploy.** Per pre-scope §6 (and `VISION.md` §"The forcing function"), the load-bearing semantic addition is:

   ```sql
   WHEN MATCHED AND (
       target.col1 <> source.col1 OR
       (target.col1 IS NULL AND source.col1 IS NOT NULL) OR
       (target.col1 IS NOT NULL AND source.col1 IS NULL) OR
       ...  -- repeat for every non-key column
   ) THEN UPDATE SET ...
   ```

   The predicate is non-trivial under nullable columns (NULL ≠ NULL in SQL). V2 emits the full nullable-aware comparator across every non-key column. Per CdcAwareness: kinds without CDC use V1's predicate-free MERGE (V1 already proven correct in trunk; the CDC-noise path is irrelevant for non-tracked tables). Per-kind dispatch.

7. **Aggregate-root + smart constructor — `DataInsertScript` per-kind via `ArtifactByKind`.** The smart constructor enforces strict-equality keyset (T11 structural; chapter 3.5 cash-out). Per the V2-driver KPI: data fidelity verification rests on T11 structural correctness across (StaticSeeds ∪ MigrationDependencies ∪ Bootstrap) emitters. The composer's `unionInTopoOrder` partition assertion (every kind covered by at most one emitter under a given `DataComposition`) surfaces as `EmitError.OverlappingEmitterCoverage of (SsKey * EmitterName list)` per pre-scope §5.3.

8. **Test-fidelity — CDC-silence property test is the chapter's signature deliverable.** Per the per-axis correctness stakes table in `V2_DRIVER.md`: the data axis verification depth is "highest stakes; CDC silence on idempotent redeploy is the highest-leverage single property in the entire V2-driver KPI." The chapter ships:
   - **Idempotence property:** `emit(catalog) = emit(emit(catalog) |> redeploy |> readBack)` (the round-trip's data half).
   - **Topological-order property:** every Phase 1 row's FK targets either appear earlier in Phase 1 or are deferred via `DeferredFkSet`.
   - **CDC-silence property (canary-territory):** `deploy → enable CDC → redeploy → CDC.fn_cdc_get_all_changes returns ∅`. This is the Docker-dependent canary that proves the change-detection predicate works under real SQL Server semantics.

---

## Slice arc

Per pre-scope §8 + the strategic frame above, the chapter's slices are ordered by IR-grows-under-evidence (each subsequent slice has at least one consumer for each new type it lands).

| # | Slice | Goal | LOC budget |
|---|---|---|---|
| α | StaticSeedsEmitter v0: V1-shape MERGE (no change-detection predicate yet) | Emit MERGE for `Modality.Static` kinds; idempotent under repeat invocation; T1 byte-deterministic | ~150 src + ~100 test |
| β | `Profile.CdcAwareness` field + change-detection MERGE predicate | Dispatch per-kind: CDC-enabled → predicated MERGE; otherwise → V1-shape MERGE | ~60 src + ~80 test |
| γ | CDC-silence-on-idempotent-redeploy property test (canary) | Docker-dependent canary that asserts the property under real SQL Server CDC | ~80 src + ~60 test |
| δ | `DataInsertScript.Phase2Updates` + `DeferredFkSet` (cycle-breaking) | Two-phase insertion for kinds in FK cycles | ~50 src + ~50 test |
| ε | `MigrationDependenciesEmitter v0` + adapter | Migration-team-published row pickup; adapter at boundary | ~120 src + ~80 test |
| ζ | `BootstrapEmitter v0` (UserRemapContext = empty pass-through) | Bootstrap fills the remainder; `UserRemapContext` lands in chapter 4.2 | ~100 src + ~80 test |
| η | `EmissionPolicy.DataComposition` DU + `DataEmissionComposer` dispatch | The composer reads Policy; emitters do not | ~80 src + ~60 test |
| θ | `EmitError.OverlappingEmitterCoverage` + partition assertion | Composer asserts no two emitters cover the same kind | ~30 src + ~40 test |

**Total: ~670 LOC source + ~550 LOC tests.** Per the V2_DRIVER.md Phase 3 budget.

This document opens with **slice α** ready to ship. Slice γ (the CDC-silence canary) is the chapter's signature deliverable; slices β + γ together prove the V2-driver KPI's highest-stakes claim.

---

## What this chapter does **not** do

Bounded by the strategic frame and the V2-driver KPI sequencing:

- **No SSDT DDL emission.** Chapter 4.1.A is the schema-emit sibling; it shipped concurrently. The two emitters are siblings under the V2-driver KPI; their compositions land at the chapter-4.3 three-channel split + the chapter-5 cutover-day runbook.
- **No User FK reflow.** Chapter 4.2's `UserFkReflowPass` is Phase 4. Chapter 4.1.B emits per `UserRemapContext` (which is empty / pass-through until chapter 4.2 ships).
- **No DACPAC adoption.** Chapter 3.x DacpacEmitter via DacFx is conditional on the deploy path requiring DACPAC; orthogonal to the data-emission triumvirate.
- **No SnapshotRowsets gating.** Unlike chapter 4.1.A slices 6/7/8 (gated on chapter 3.2's IR widening), 4.1.B's slices α through θ all consume types that already exist in the V2 IR. The CDC discovery in slice β extends the read-side adapter (chapter 3.1 territory), not chapter 3.2.

---

## Companion documents

- **Pre-scope (operational plan):** `CHAPTER_4_PRESCOPE_DATA_TRIUMVIRATE.md`.
- **V2-driver KPI:** `V2_DRIVER.md` (the per-axis correctness stakes table places this chapter at the top under "highest-leverage single deliverable").
- **Strategic frame precedent:** `CHAPTER_4_1_A_OPEN.md` (sibling chapter; same eight-axis discipline).

---

## Closing

Chapter 4.1.B is V2-driver's most consequential chapter. The CDC-silence property is the property the cutover team most needs proven; this chapter ships the structural commitment that makes it provable. Each slice ships with its own commit; the close ritual rolls in chapters 3.6 + 3.7 + 4.1.A + 4.1.B once the CDC-silence canary shows N=10 consecutive green runs.
