# Chapter B.3 close — LiveProfiler deep-probe sweep (8 slices)

**Opened:** 2026-05-19 (mid-session pivot from row-14 inquiry to the deep-probe arc per principal-PO direction).
**Closed:** 2026-05-19 (same session; 8 substantive slices shipped + tested).
**Branch:** `claude/audit-parity-matrix-37foc`.
**Test baseline at close:** 1695 / 1695 non-Docker + 33 / 33 LiveProfiler Docker-gated integration; 0 build warnings under `TreatWarningsAsErrors=true`.

The chapter operationalizes **V2_PRODUCTION_CUTOVER §7.4 (Phase B.3)** — V2 owns source-side statistical evidence end-to-end. V1-JSON sunsets at cutover+30 without degrading tightening-pass decisions; the Faker emitter's gating evidence chain has its foundation in place.

## What shipped (8 slices)

| Slice | Commit | Cash-out |
|---|---|---|
| **B.3.1** foreign-key-reality | `e4bce7e` | FK orphan-count probe; `ForeignKeyRules` silent-default closes. Row 88 → 🟢. |
| **B.3.2** column-null-counts | `982add1` | Exact int64 NullCount probe; `NullabilityRules` 5-branch silent-default closes. Row 86 → 🟢. |
| **B.3.3** unique-candidates | `b22ba84` | Composite + projection; `UniqueIndexRules` silent-default closes. `ProbeStatus` primitives (`noProbeRun` / `observed` / `ambiguous`) extracted at 8-consumer threshold. Row 87 → 🟢. |
| **B.3.4** fk-orphan-samples | `4b33d8e` | Pillar 9 pivot worked example — operational diagnostics land in `Diagnostics<'_>`, not in Profile axis. Row 89 → 🟢. |
| **B.3.5** statistical-moments-ir | `1c68248` | IR keystone — `StatisticalMoments` + `NumericDistribution.withMoments` + `coefficientOfVariation`. No live-probe yet; foundation for slice 6's cache derivations. |
| **B.3.6** evidence-cache | `76f573d` | **Architectural pivot** — in-memory typed-row `EvidenceCache` replaces per-attribute SQL probes. 3 MVP derivations (`deriveAttributeRealities` / `deriveColumnProfiles` / `deriveNumericDistributions`). New `Projection.Adapters.Sql/EvidenceCache.fs`. |
| **B.3.6b** cache-fold-residuals | `d0188ee` | Remaining 4 derivations folded into cache. ALL Profile axes derive from cache; `attach` becomes purely cache-driven. **Big-O audit** ships 3 inline optimizations (pre-indexed `ColumnsByKey`; memoized FK target-PK sets via `*With`-overload; single-pass `Dictionary` categorical tally). |
| **B.3.7** sampling-multi-env | `20f51b5` | `SqlProfilerOptions` operator-tunable sampling cap + `Profile.merge` with FsCheck commutative/associative/identity property tests. `UserPopulation.union`. Rows 90 + 92 → 🟢. |
| **B.3.8** fk-correlation | `eda4504` | Three new IR types (`ForeignKeyCardinality` / `ForeignKeySelectivity` / `JointDistribution`); three new Cache derivations. **Faker emitter's deferred trigger structurally met** — all four `ADMIRE.md`-named gating evidence chain nodes shipped. |

## Substantive contributions

### 1. DATA-axis cutover-blocker silent-default closed across all three tightening rules

Before chapter B.3, the live-probe path populated `AttributeReality` only. The tightening rules (`NullabilityRules`, `UniqueIndexRules`, `ForeignKeyRules`) consumed `ColumnProfile.NullCount` / `Profile.UniqueCandidates` / `Profile.ForeignKeys.HasOrphan` respectively — none of which the live-probe filled. Every live-probe-driven decision routed to the conservative default (`LogicalMandatoryNoProfile` / `NoCandidateProfiled` / `EvidenceMissing`).

At chapter close, all three rules consume V2-emitted Profile evidence on the live-probe path. V1-JSON sunsets at cutover+30 without degrading tightening decisions.

### 2. Architectural pivot to discovery-then-derive

Slices 1-5 accreted SQL probes (~6000 round-trips at 300 tables). Principal-PO surfaced the no-overfetching concern mid-slice-5; slice 6 pivoted to a typed in-memory cache (`EvidenceCache`) populated via 3 SQL queries per non-static kind. Slices 6b/8 folded the remaining derivations into cache; slice 7 added operator-tunable sampling.

**Net SQL round-trip count:** ~6000 → ~900 at 300-table production scale. 6-7× reduction.

All cache derivations are pure F# (`Array.sort`, `Array.groupBy`, `HashSet` membership, `Dictionary` frequency tally). The cache substrate is the keystone for chapter B.4+ probe extensions — new evidence shapes land as `Cache.deriveX` primitives, not as new SQL queries.

### 3. Faker emitter's gating evidence chain structurally complete

Per `ADMIRE.md`'s evidence-chain diagram, the deferred Faker emitter needs four shapes of evidence:

| Chain node | Slice |
|---|---|
| Categorical value frequencies | 6b `Cache.deriveCategoricalDistributions` |
| Numeric histograms + range | 5 (`StatisticalMoments`) + 6 (`Cache.deriveNumericDistributions`) |
| Joint distributions across FK pairs | 8 `JointDistribution` |
| Cardinality-aware tightening | 8 `ForeignKeyCardinality` |

Per `DECISIONS Active deferrals — Faker emitter`: trigger named "third evidence type lands OR concrete consumer demand." Three new evidence types shipped at slice 8 (`ForeignKeyCardinality` / `ForeignKeySelectivity` / `JointDistribution`); **the deferred trigger is structurally met.** Promotion from deferred to scoped-for-implementation is a chapter B.4 / chapter 5+ open decision; the structural prerequisites are in place.

### 4. Matrix-row impact

Six rows transitioned to 🟢 PARITY:

- Row 86 (`NullCountQueryBuilder` live-probe acquisition) — slice 2
- Row 87 (`UniqueCandidateQueryBuilder` live-probe acquisition) — slice 3
- Row 88 (`ForeignKeyProbeQueryBuilder` orphan-count) — slice 1
- Row 89 (`ForeignKeyOrphanSampleQueryBuilder`) — slice 4
- Row 90 (`TableSamplingPolicy`) — slice 7
- Row 92 (`MultiTargetSqlDataProfiler` multi-env merge) — slice 7

Plus three chapter-level matrix amendments (slice 6 architectural pivot; slice 6b cache-fold completion; slice 8 chapter close).

## Disciplines codified or reinforced

### New (this chapter)

- **EvidenceCache discovery-then-derive pattern** (slice 6 pivot) — when multiple per-axis SQL probes accrete over a chapter, the architectural answer is "discover once, derive many times in pure F#." The cache substrate replaces N+1 per-kind round-trips with 3 per-kind. Cross-derivation shared state lives in the cache as precomputed indices (e.g., `ColumnsByKey`). See `DECISIONS 2026-05-19 (slice B.3.6.evidence-cache)`.

- **Big-O audit at multiple-derivation sites** (slice 6b audit during validation) — when adding multiple derivations over the same cached substrate, plan cross-derivation shared-state explicitly. The `*With`-overload pattern is the F# idiom for sharing computed work (e.g., `deriveForeignKeyRealitiesWith targetIndex`); the audit catches inadvertent N-pass-reconstruction before it ships. Pairs with `DECISIONS 2026-05-24 — Bench-driven optimization protocol`: structural Big-O optimization at design time + bench-driven at hot paths.

### Reinforced

- **Smart-constructor-FIRST** — slice 1's `ForeignKeyReality.create`, slice 3's `UniqueCandidateProfile.create` + `CompositeUniqueCandidateProfile.create`, slice 5's `StatisticalMoments.create` + `NumericDistribution.withMoments`, slice 8's three new IR types' creates. Each absorbs subsequent field extensions at one site, not N.
- **Sibling-wrapper discipline** (chapter 4.7 cleanup amendment) — `captureAttributeRealities` / `captureForeignKeyRealities` / `captureColumnProfiles` etc. form a ubiquitous-language-consistent family. `*With`-overload pattern at slice 6b is the principled F# default-argument idiom.
- **Audit during validation** — 5+ inline fixes across the chapter (SUM(int) → COUNT_BIG; `RowCount` reserved-keyword bracket-quoting; FS0960 let-before-member; `CAST AS bit` → INT-as-boolean; F# nullness analyzer on `obj.ToString()`; ProbeStatus 8-site literal duplication extracted at threshold).
- **Pillar 9 (data-intent / operator-intent classification)** — slice 4's orphan samples emit `Diagnostics<'_>` not Profile axis (operational, not data-intent); slice 7's `SqlProfilerOptions` carries operator intent at the adapter, not in Profile IR.
- **A1 deterministic sampling** — slice 7's `SELECT TOP (N) … ORDER BY <pk>` + slice 8's deterministic tuple-key ordering preserve T1 byte-determinism under repeated probes.

## Test coverage delta

- **+33 Docker-gated integration tests** in `LiveProfilerIntegrationTests.fs` (~1m55s warm across the class via `EphemeralContainerFixture` per-class amortization)
- **+9 IR keystone tests** in `AttributeDistributionTests.fs` (slice 5 `StatisticalMoments` + `withMoments` + `coefficientOfVariation`; pure-Core; 34ms)
- **+7 algebraic-law property tests** in `ProfileTests.fs` (slice 7 `Profile.merge` commutative / associative / identity; FsCheck.Xunit; 173ms)

Total +49 new tests; all pass green at chapter close.

## Code surface delta

| File | LOC delta | Substance |
|---|---|---|
| `src/Projection.Adapters.Sql/EvidenceCache.fs` (NEW) | +224 | `CachedValue` / `CachedColumn` / `CachedKind` / `EvidenceCache` types + `SqlProfilerOptions` + lookup primitives |
| `src/Projection.Adapters.Sql/LiveProfiler.fs` | +~1600 | 7 captures + 9 `Cache.derive*` + `attach` rewire + sampling overload |
| `src/Projection.Core/Profile.fs` | +~500 | 4 new IR types (`StatisticalMoments`, `ForeignKeyCardinality`, `ForeignKeySelectivity`, `JointDistribution`) + smart constructors + `Profile.merge` with worst-case-aggregation algebra |
| `src/Projection.Core/UserIdentity.fs` | +14 | `UserPopulation.union` (multi-env merge helper) |
| `src/Projection.Adapters.Sql/ProfileSnapshot.fs` | +5 | V1-JSON-path defaults for new Profile axes |
| `tests/Projection.Tests/LiveProfilerIntegrationTests.fs` | +~600 | 33 Docker tests across the 8 slices |
| `tests/Projection.Tests/ProfileTests.fs` | +90 | 7 algebraic-law property tests |
| `tests/Projection.Tests/AttributeDistributionTests.fs` | +120 | 9 IR keystone tests |
| `tests/Projection.Tests/ProfileFixtures.fs` | +3 | 3 new Profile field defaults |
| Docs (CHAPTER_B_3_OPEN / V1_PARITY_MATRIX / DECISIONS / HANDOFF) | +~3000 | Strategic frame + 8 slice cash-outs + chapter-level amendments + handoff letters |

Total: ~6160 net LOC across 8 commits. Roughly 50% code / 50% docs.

## Closing posture

Chapter B.3 leaves the DATA axis cleanly closed for V2-driver mode. The live-probe path is the source of truth; V1-JSON is the cutover+30 safety net. The cache substrate is the architectural keystone — new probe shapes land as pure-F# derivations, not new SQL queries.

Three open questions for the next chapter's opening:

1. **Promote Faker emitter from deferred?** Gating evidence is in place; trigger structurally met. Chapter B.4 (CLI subcommands per V2_PRODUCTION_CUTOVER §7.5) is the natural sequencing alternative — defer Faker to chapter 5+ when operator-facing CLI consumers can request synthetic data outputs.
2. **Retire the transitional SQL captures?** Slices 1+3+4's `captureForeignKeyRealities` / `captureCompositeUniqueCandidates` / `captureForeignKeyOrphanSamples` remain available as public surface but `attach` no longer uses them. Slice-by-slice retirement when no consumer depends on them — likely a chapter-close hygiene slice in B.4.
3. **Composite-PK FK extension?** Slice 1 deferred composite-PK targets as `Outcome = AmbiguousMapping`. Slice 6b's `projectTupleKeys` primitive makes this trivial (multi-column Set.difference); awaits a composite-PK fixture or consumer demand. **RESOLVED at chapter B.4 slice 3 open (2026-05-19) as out-of-scope** — the principal-PO confirmed composite primary keys are not an OS use case the operator has encountered. The `AmbiguousMapping` outcome is the right answer for the degenerate case. See `DECISIONS 2026-05-19 (slice B.4.3.composite-pk-fk)`.

The chapter compounds. Slice 5's IR keystone enabled slice 6's distributions. Slice 6's cache substrate enabled slice 6b's full fold. Slice 6b's `projectTupleKeys` enabled slice 8's joint distributions. Slice 8's three new IR types complete the Faker foundation. Each slice was earned by the slice before it.

Hold the spine.
