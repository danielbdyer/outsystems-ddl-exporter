# Chapter B.3 open — LiveProfiler deep-probe sweep (source-side statistical evidence)

**Branch:** `claude/audit-parity-matrix-37foc`. **Predecessor:** slice `A.4.7'-prelude.live-profiler` (2026-05-19; `4c0cbce`) — the LiveProfiler subset shipped (boolean-witness probes for `HasNulls` / `HasDuplicates`; `IsNullableInDatabase` reflection). **This chapter** closes the deferrals that slice named explicitly + extends to the four remaining profile axes.

This chapter operationalizes **V2_PRODUCTION_CUTOVER §7.4 (Phase B.3)** — V2 owns source-side profile capture end-to-end, so V1's JSON-snapshot path can sunset at cutover+30 without degrading tightening-pass decisions. The B.3 pre-scope names: port V1's 5 query builders (NullCount / NullRowSample / UniqueCandidate / ForeignKeyProbe / ForeignKeyOrphanSample) + the `ProfilingQueryExecutor` orchestrator; add `MaxIdentityValueQueryBuilder`; hydrate `Profile` directly via the smart constructor; respect sampling policy from `Pipeline.Config`. Estimated effort: 3-4 weeks.

## Strategic-frame axes

1. **Cutover-driven, not feature-driven.** V1-JSON is the safety net for cutover+30; after sunset, V2 owns source-side statistical evidence or tightening decisions silently default. `ForeignKeyRules.evaluate` (`Strategies/ForeignKeyRules.fs:278-286`) reads `reality.HasOrphan` + `reality.OrphanCount` directly; today the live-probe path leaves `Profile.ForeignKeys` empty → `ForeignKeyReality.create` defaults → "no orphans, enforce as-is" silently. The first slice closes this. Every subsequent slice closes a sibling silent-default risk.

2. **Sibling-adapter composability holds throughout.** The three Profile-adapter siblings (`ProfileSnapshot.attach` for V1-JSON, `ProfileStatistics.attach` for V2-distribution JSON, `LiveProfiler.attach` for live-SQL probes) compose without overwriting sibling axes. Every slice in this chapter preserves that — each LiveProfiler capture function emits its own evidence stream; `attach` composes captures into a `Profile` without touching axes the caller already filled.

3. **Pillar 9: all probes carry DataIntent.** No operator policy enters at probe time. Sampling policy (when V2 adopts non-full-table probing) lives in `Pipeline.Config` per `DECISIONS 2026-05-18 (slice 5.4.δ.profiling) — Sampling policy is operator intent, lives in the orchestrator, not in Profile IR`. The probe queries themselves observe deployed reality; the orchestrator chooses *how* (full-table / TOP N / sampled / per-partition).

4. **Probe shape: combined-query short-circuit.** Per the live-profiler precedent (`LiveProfiler.fs:84-127`), each probe is a *single round-trip per entity* via combined `EXISTS` + `COUNT_BIG(*)` projections, not separate queries per axis. Slice 1 extends the pattern to FK reality: one query per Reference returns `HasOrphan : bit` + `OrphanCount : bigint`. Slice 2 extends to per-column NULL evidence: one query per Kind returns null counts for all probed attributes via aggregate `SUM(CASE WHEN col IS NULL THEN 1 ELSE 0 END)` per column. This matches V1's `NullCountQueryBuilder` query shape line-for-line.

5. **Closed-DU expansion empirical test holds.** No new IR types — `ForeignKeyReality` / `ColumnProfile` / `CompositeUniqueCandidateProfile` / `Distribution` already carry the field shapes. The work extends *adapters* and adds *smart constructors* (`ForeignKeyReality.create` etc.) where missing. Per the smart-constructor-FIRST discipline (HANDOFF 2026-05-18, insight 4), aggregate types get `create` before being extended; the lift happens once per type, propagation is bounded to the constructor body.

6. **Closure trigger structure inherited from 2026-05-19.** The slice A.4.7'-prelude.live-profiler shipped four refreshed deferrals: `HasOrphans` per-FK probe / `IsPresentButInactive` full closure / sampling policy / composite-unique probes. This chapter cashes the first three named ones; the fourth (composite-unique) becomes slice 3. Two new slices extend beyond the named-deferral surface: distribution probes (chapter 4.1.B uses `Profile.Distributions` — no live-probe path today); multi-environment merge (row 92).

7. **No production-CLI dependency.** The deep-probe surface is callable from any consumer with a `SqlConnection`. Production CLI wiring lives in chapter B.4 (V2_PRODUCTION_CUTOVER §7.5); this chapter ships the F# primitives + Docker-gated integration tests. The chapter close does NOT block on CLI exposure.

8. **Close ritual paired with the live-profiler arc + the row-49/85/86/87 reclassifications.** Single chapter close at the end of the arc; the closure document re-walks the deferrals named at slice A.4.7'-prelude.live-profiler and records which fired + cashed vs which deferred-further.

## Slice plan (9 substantive slices — expanded from 8 at slice 6 mid-flight)

**Plan re-expanded at slice 6 in-flight** per principal-PO architectural pivot. Originally slice 6 was "probe consolidation (batched per-kind SQL)"; the mid-slice conversation pivoted to "EvidenceCache architecture (in-memory typed-row substrate; pure-F# derivations)" — a much larger structural change. The MVP shipped as slice 6; the full retrofit (folding FK + composite + categorical SQL captures into cache derivations) splits into slice 6b for cohesion.

## Slice plan (8 substantive slices — expanded from 6 at slice 5 close)

Slice naming uses `B.3.<n>.<axis>` for chapter coherence. Each slice ships its own commit + matrix amendment + DECISIONS entry. **Plan expanded from 6 to 8 slices at slice 5 close** per two principal-PO refinements: (a) per-data-type evidence + no-overfetching architectural concern, and (b) FK-mediated correlative analysis for shape-preserving synthetic data.

| Slice | Scope | Status |
|---|---|---|
| **1** B.3.1.foreign-key-reality | `ForeignKeyReality.create` smart constructor; `LiveProfiler.captureForeignKeyRealities`; closes `ForeignKeyRules` silent-default | ✓ shipped |
| **2** B.3.2.column-null-counts | `LiveProfiler.captureColumnProfiles` batched-per-kind null-count probe; closes `NullabilityRules` silent-default | ✓ shipped |
| **3** B.3.3.unique-candidates | `LiveProfiler.captureCompositeUniqueCandidates` + `projectUniqueCandidates` + `ProbeStatus` primitives extracted; closes `UniqueIndexRules` silent-defaults | ✓ shipped |
| **4** B.3.4.fk-orphan-samples | `LiveProfiler.captureForeignKeyOrphanSamples` emitting Diagnostics; first worked example of pillar 9 pivot at adapter layer | ✓ shipped |
| **5** B.3.5.statistical-moments-ir | **IR keystone only** — `StatisticalMoments` + `NumericDistribution.Moments` + `withMoments` + `coefficientOfVariation`. Algebraic-paradigm work the user asked for; live-probe captures deferred to slice 6 (probe consolidation). | ✓ shipped |
| **6** B.3.6.evidence-cache | **EvidenceCache architectural pivot** — replace accreted per-attribute SQL probes with a typed in-memory cache populated via 3 queries per kind (aggregate + row-stream + nullability reflection). Pure-F# derivations (`Cache.deriveAttributeRealities` / `deriveColumnProfiles` / `deriveNumericDistributions`) replace SQL captures from slices 1-5 where applicable. Built on `CachedValue` closed DU + column-oriented `CachedKind`. ✓ shipped MVP (5 Docker tests). | ✓ shipped (MVP) |
| **6b** B.3.6b.cache-fold-residuals | ✓ Folded remaining SQL captures into cache derivations: `Cache.deriveForeignKeyRealities` (cross-table Set.difference); `Cache.deriveForeignKeyOrphanSamples` (in-memory sample TOP-N; pillar 9 Diagnostics output); `Cache.deriveCompositeUniqueCandidates` (tuple-keyed `Array.groupBy` via `projectTupleKeys`); `Cache.deriveCategoricalDistributions` (single-pass `Dictionary` frequency tally). `attach` now cache-only — 3 SQL queries per kind regardless of axis count. **Big-O audit shipped 3 inline optimizations**: pre-indexed `CachedKind.ColumnsByKey` Map; memoized FK target-PK sets via `*With`-overload pattern; single-pass categorical Dictionary tally. Net round-trips: ~6000 (pre-6) → ~2400 (6 MVP) → **~900** (6b). | ✓ shipped |
| **7** B.3.7.sampling-multi-env | Sampling policy + multi-environment merge (rows 90 + 92). `Profile.merge : Profile → Profile → Profile` (commutative + associative property tests); wire `SqlProfilerOptions.Sampling` through `captureEvidenceCache` (replace full-scan default with operator-tunable sample cap). | pending |
| **8** B.3.8.fk-correlation | FK-mediated correlative evidence: (a) per-Reference fan-out cardinality (child-count distribution per parent — `Array.groupBy` over cached child source-FK values); (b) per-Reference selectivity / clumping (value-frequency over source FK column — same primitive as (a) viewed differently); (c) multi-FK joint distributions (co-occurrence counts via `Array.zip` over multiple cached columns of a shared kind, then `Array.groupBy` on tuples). Foundation for the deferred Faker emitter's joint-distribution requirement (per `ADMIRE.md` and Active deferrals index). All three derive in pure F# from the cache once slice 6b lands. | pending |

**Chapter close ritual** runs after slice 8. Eight items per `CLAUDE.md`: Active deferrals scan; contract-vs-implementation walk; CLAUDE.md / README.md staleness; HANDOFF + close-doc scope; fresh-eye walk; operating-disciplines table currency; V1-input-envelope walk; per-axis-stakes evaluation against V2_DRIVER. **Bonus close item for this chapter**: re-evaluate Faker's Active-deferrals trigger condition — slices 5 + 6 + 8 collectively ship the statistical-moments + per-type-distribution + joint-FK evidence that the Faker emitter's gating condition names ("third evidence type lands OR concrete consumer demand").

## Out of scope

- **Production CLI exposure** (`projection profile --config <path>`). V2_PRODUCTION_CUTOVER §7.5 (chapter B.4); this chapter ships primitives only.
- **`MaxIdentityValueQueryBuilder`** (the Q1 cash-out per B.3 task list). Independent of statistical probes; can ship in chapter B.4 or as a separate slice if a chapter 4.x consumer surfaces.
- **`ProfilingQueryExecutor` (672 C# LOC orchestration port)**. V2 already has the F#-shaped composition primitives (`Task<Result<_>>` over `SqlConnection`); the orchestration shape lifts inline at each LiveProfiler capture function. The 672 C# LOC is V1's orchestration plumbing; V2's plumbing is the Task-monad + Result-monad composition, not a separate orchestrator.
- **Distribution analytics post-capture** (six-dimension synthetic-data quality scoring per `VISION_REVIEW.md §G.9`). Cut from cutover scope; revisits in chapter 5+ if Faker emitter ships.
- **JSON-middleman retirement for the V1-JSON consumer path** (`ProfileSnapshot.attach`). The V1-JSON adapter stays through cutover+30 as the V1-side safety net; sunsets with V1.

## Dependency map

```
Slice 1 (FK orphan-count probe)
  └─ enables ForeignKeyRules to make decisions on live-probe path
     (no slice dependency upstream)

Slice 2 (exact NullCount)
  └─ enables NullabilityRules to make precise decisions on live-probe path
     (no slice dependency upstream)

Slice 3 (composite-unique probes)
  └─ enables UniqueIndexRules composite-decision precision
     (no slice dependency upstream)

Slice 4 (FK orphan-sample rows)
  └─ depends on slice 1 (probe shape carries through; sample is the
     TOP-N extension of slice 1's orphan-count query)
     └─ lands in Diagnostics, not Profile (pillar 9)

Slice 5 (Distribution probes)
  └─ depends on slice 6 sampling policy if probes exceed scan threshold
     (cardinality estimate via TOP-N percentile; needs sampling for scale)

Slice 6 (Sampling + multi-env)
  └─ depends on slices 1+2+3 (each capture function must accept a
     sampling-policy parameter; refactor lands here)
```

Slices 1/2/3 are independent and parallelizable (different captures, independent integration tests). Slice 4 depends on slice 1. Slice 5 depends on slice 6 for scale-bounded probes. Slice 6 is the rollup refactor that retros sampling into the earlier slices' captures.

## Operator pressure signals (when to chapter-mid-audit)

- Per-slice: integration test runtime (target ≤30s warm per slice's Docker tests; the `EphemeralContainerFixture` amortizes container init across the class).
- Cross-slice: the `Profile` aggregate after `LiveProfiler.attach` must have populated entries in every axis exercised by the slice's tests; sibling axes from prior slices must remain populated under composition.
- Discipline: after slice 3, chapter-mid-audit dispatched per `DECISIONS 2026-05-19 — Chapter-mid-audit as a routine practice (session 23; session 24 amendment)`. Required dimension: Active deferrals scan + contract-vs-implementation walk.

## What success looks like

**End of slice 1 (this session):** Row 88 closes from 🟠 NOT-MAPPED (partial) → 🟢 PARITY. `Profile.ForeignKeys` populated by `LiveProfiler.attach`. Docker-gated integration tests assert HasOrphan/OrphanCount on clean + dirty fixtures, with `attach` composability proven (AttributeRealities and ForeignKeys both populated). 2026-05-19 deferral "HasOrphans per-FK probe" closes. Non-Docker baseline holds at ≥1629 passing.

**End of chapter B.3 (6 sessions):** Phase B.3 of V2_PRODUCTION_CUTOVER closes; V2 owns source-side statistical evidence end-to-end; V1-JSON path stays as cutover+30 fallback only. Tightening passes consume V2-emitted Profile evidence directly. Matrix rows 86 / 87 / 88 / 89 / 90 / 92 reach 🟢 PARITY. The "synthetic data later" capability the user named has its evidence foundation in place.

---

*Per the V2-driver KPI: every slice in this chapter advances the DATA axis. V1 sunset becomes a real plan once V2 owns this surface.*
