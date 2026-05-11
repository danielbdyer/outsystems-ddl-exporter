# Chapter 4.2 open — User FK reflow + UserMatchingStrategy

**Sessions:** opens with this document (2026-05-11). **Posture:** Phase 4 of V2-driver KPI critical path (per `V2_DRIVER.md`; the User FK reflow axis is one of the per-axis correctness stakes ranked HIGH because production reports break or data loss occurs when User remapping is incomplete). **Predecessors:** chapter 4.1.B (CDC-aware data triumvirate; `UserRemapContext` placeholder shape established at slice ζ; `composeRenderedFull` pipeline-integration entry shipped at slice ι); chapter 3.2 (`OssysOriginal` operationally reachable for cross-version `V1Mapped` UUIDv5 derivation).

This is the chapter-open document per `DECISIONS 2026-05-15 — Strategic frame for the OSSYS implementation chapter`. Companion close synthesis lands at `CHAPTER_4_2_CLOSE.md` when this chapter ends. **Operational pre-scope:** `CHAPTER_4_PRESCOPE_USERFK_REFLOW.md` — the implementation-grade plan with §3 `UserMatchingStrategy` DU, §4 `UserRemapContext` + discovery pass, §5 consumer integration with the data triumvirate, §6 Lineage / Diagnostics shape, §7 slice-by-slice breakdown (7 slices), §9 V1 differential (V1's `UserMatchingEngine` is the oracle).

---

## Why this chapter

V2-driver KPI Phase 4. Per `V2_DRIVER.md` per-axis correctness stakes:

> | **User FK reflow** | Production reports break or data loss when User remapping is incomplete | Every CreatedBy/UpdatedBy FK in target environment resolves to a valid target User; per-strategy coverage (ByEmail, BySsKey, ManualOverride, FallbackToSystemUser) | High. Chapter 4.2. |

V1 already does this in C# (`src/Osm.Pipeline/UatUsers/`). What V2 inherits is the **operator workflow shape** (orphan discovery + per-strategy match + fallback + audit artifact). What V2 adds is **algebraic legibility**: the strategy is a closed DU, the result is a writer-monadic `Lineage<Diagnostics<UserRemapContext>>` value, and the consumers are sibling Π's whose commutativity is structurally testable (T11). The output is a `UserRemapContext` value that any data-emission Π can consume to rewrite User-FK columns at emission time without each Π re-implementing the matching logic — exactly the seam chapter 4.1.B slice ζ established.

The cutover stake is concrete: the 300-table OutSystems 11 system threads `CreatedBy` / `UpdatedBy` user FKs through every entity. Reflowing legacy domain rows from Dev → UAT (or QA → UAT, or any source-target pair across the four-environment plan) produces orphan FK values whenever a source-environment user has no direct counterpart in the target. **The cutover-window safety criterion is: every reflowed row's `CreatedBy` and `UpdatedBy` resolves to a real target-environment user** — no orphan FKs to phantom users, no silently dropped attributions.

---

## Strategic frame — eight axes named at chapter open

Per the chapter-4.1.A / chapter-4.1.B precedent, multi-session chapters name their load-bearing axes at chapter open before substantive slices begin.

1. **DDD — `UserMatchingStrategy` is operator intent; `UserRemapContext` is the discovered remap evidence.** Per pre-scope §2: `UserMatching` is the fifth `Policy` axis (joining Selection / Emission / Insertion / Tightening) — it is operator decision *how to bridge* per-environment user identity. `UserRemapContext` is the value the discovery pass produces; it is empirical evidence (which source users mapped to which target users; which didn't), not intent. Both have concept-shaped names per pillar 8.

2. **FP — closed DU for the strategy + writer-monadic result + recursive composition.** `UserMatchingStrategy = ByEmail | BySsKey | ManualOverride of Map<SourceUserId, TargetUserId> | FallbackToSystemUser of fallback: TargetUserId * primary: UserMatchingStrategy`. The recursive `FallbackToSystemUser` variant encodes "try the primary; on miss, attribute to fallback" structurally — the alternative (list-of-rules) invites composability the operator workflow doesn't need. `UserFkReflowPass.discover` returns `Lineage<Diagnostics<UserRemapContext>>` per the pass return-type codification.

3. **Hardcore (no-string-concatenation) — value objects with smart constructors.** `Email.create` validates non-blank + normalizes via `Trim()` (mirrors V1's `OrdinalIgnoreCase + Trim`); `SourceUserId` / `TargetUserId` wrap `UserId` for newtype-level safety against id-orientation confusion. Identity-typed closed DU prevents passing a `SourceUserId` where `TargetUserId` is expected.

4. **Streaming — bench observability per pass invocation + per-strategy.** `UserFkReflowPass.discover` records `Bench.scope "passes.userFkReflow"` at entry; per-strategy bench scopes (`Bench.scope "userFkReflow.byEmail"`, `"userFkReflow.bySsKey"`, etc.) surface per-strategy cost when populations grow large. `Bench.iterDo` over source users for per-iteration distribution at canary scale.

5. **Hexagonal — `ManualOverride` adapter at the boundary.** The CSV (`SourceUserId,TargetUserId,Rationale`; V1's `UserMapLoader.cs` shape) is operator-supplied. `Projection.Adapters.UserMap.UserMapLoader` parses at the boundary; Core sees only the resolved `Map<SourceUserId, TargetUserId>`. Pure-F# core / no-I/O-in-Core holds.

6. **Built-in obligation — UUID v5 for cross-version `V1Mapped` derivation.** Per pre-scope inheritance from chapter 3.2: `OssysOriginal` SsKey is now operationally reachable; `V1Mapped` Source-tag derivation flows through `UuidV5.create` (RFC 4122 §4.3) — chapter 3.5's existing primitive. No re-implementation.

7. **Aggregate-root + smart constructor — `UserRemapContext` invariant: `Mapping.Keys ∩ Unmatched = ∅` (disjoint).** Per `AXIOMS.md` operational principle (structural-commitment-via-construction-validation): every value carries its own truth. `UserRemapContext.create` validates the disjointness; consumers query via `isFullyMapped` / `unmatchedCount` without re-validating. Slice 3 ships the smart constructor; slice 4 routes `UserFkReflowPass.discover` through it.

8. **Test-fidelity — V1 differential test as the oracle + multi-environment commutativity property.** V1's `UserMatchingEngine.Execute` is the V1↔V2 differential surface (per pre-scope §9). Skip-stubs name the deliberate divergences (V1's `Regex` collapses into V2's `ManualOverride`; V1's `ExactAttribute` folds into V2's `BySsKey` only when V1's configured attribute IS SsKey). The multi-environment property test (slice 7): same `(sourceUsers, ByEmail)` against four distinct `targetUsers` populations yields four `UserRemapContext` values; the source-keyset of `Mapping` agrees across all four (modulo per-environment unmatched). T11 specialization for sibling Π's commuting on shared `UserRemapContext`.

---

## Slice arc

Per pre-scope §7 + the strategic frame above, the chapter's slices are ordered by IR-grows-under-evidence (each subsequent slice has at least one consumer for each new type it lands).

| # | Slice | Goal | LOC budget |
|---|---|---|---|
| α | `UserMatchingStrategy` DU + identity types (`UserId` / `SourceUserId` / `TargetUserId` / `Email`) + smart constructors + `Policy.UserMatching` axis | Types compile; `Policy.empty` extends; smart-constructor invariants tested | ~80 src + ~80 test |
| β | `UserPopulation` + `UserAttributes` in Profile | Profile carries source/target user populations; A34 unaffected | ~50 src + ~40 test |
| γ | `UserRemapContext` shape + smart constructor + module accessors | Disjointness invariant (`Mapping.Keys ∩ Unmatched = ∅`); `isFullyMapped` / `unmatchedCount` | ~60 src + ~50 test |
| δ | `UserFkReflowPass.discover` minimal — `ByEmail` only | Pass walks source population, indexes target by email, produces `UserRemapContext` + lineage events + diagnostics | ~150 src + ~100 test |
| ε | Add `BySsKey`, `ManualOverride`, `FallbackToSystemUser` strategies | Full strategy DU coverage; per-strategy worked example; composition test | ~80 src + ~80 test |
| ζ | IR refinement — `IsUserFk` flag on `Reference` | Closed-DU empirical-test holds; OSSYS adapter resolves the flag | ~80 src + ~50 test |
| η | Wire into MigrationDependenciesEmitter + BootstrapEmitter; multi-environment property test | Each emitter consumes `UserRemapContext` and rewrites User-FK column values; T11 holds across four-env populations | ~200 src + ~100 test |

**Total: ~700 LOC source + ~500 LOC tests.** Per the V2_DRIVER.md Phase 4 budget.

This document opens with **slice α** ready to ship. Slice η is the chapter signature deliverable (the multi-environment commutativity property is what makes the cutover-day cross-environment User reflow operationally provable).

---

## Inheritance from prior chapters

- **Chapter 3.5's `UuidV5.create` (RFC 4122 §4.3)** — used at slice ε for `ManualOverride`'s `V1Mapped` SourceTag derivation if cross-version remap surfaces (chapter-3.2 `OssysOriginal` operational reachability is the prerequisite that's now met).
- **Chapter 4.1.B slice ζ's `UserRemapContext = Map<SsKey, Map<int64, int64>>` placeholder** — slice γ refines this to the structural shape described in pre-scope §4 (`{ Mapping; Unmatched; Diagnostics }`), preserving consumer compatibility (the composer's `composeRenderedFull` already accepts a `UserRemapContext`-typed parameter).
- **Chapter 4.1.B slice δ's `Kind.tryFindAttribute`** — used at slice ζ for resolving the source attribute when checking FK target identity. Three-consumer threshold: StaticSeeds (slice δ) + MigrationDeps (slice ε) + UserFkReflow (this chapter).
- **Chapter 2's `Lineage<Diagnostics<'a>>` dual-writer composition** — `UserFkReflowPass.discover` returns the codified shape; one `LineageEvent` per matched user (`Annotated "matched-by-<strategy>"`); one `Warning` `DiagnosticEntry` per unmatched user.
- **Chapter 4.1.B slice θ's `EmitError.OverlappingEmitterCoverage` precedent** — at slice η, if the User remap rewrites produce a kind that's also covered by another emitter under the same `DataComposition`, the existing partition assertion catches it.

---

## What this chapter does NOT do

Bounded by the strategic frame and the V2-driver KPI sequencing:

- **No three-channel Diagnostics split.** Chapter 4.3 ships that. This chapter's diagnostics flow through the existing single-channel Diagnostics writer; chapter 4.3's split absorbs the chapter-4.2 emissions when it lands.
- **No live-V1-DB User population reader.** The OSSYS adapter at the boundary supplies `UserPopulation` from the `osm_model.json` evidence (or a per-environment sibling source); live SQL connections to V1 DBs are out of scope (chapter 5+ host-shell territory).
- **No SourceTag value-object refactor of SsKey.** The pre-scope mentions this but it's a separate slice arc (chapter 4.2's `SourceTag` refactor of `SsKey`). Defer to a slice ι if cross-version `V1Mapped` derivation surfaces under real cutover demand; today the existing four-variant `SsKey` (`OssysOriginal | Original | Synthesized | Derived`) covers chapter 4.2's needs.
- **No real CSV adapter at the boundary.** Slice ε ships `ManualOverride` consuming a programmatic `Map<SourceUserId, TargetUserId>`; the CSV reader (`Projection.Adapters.UserMap.UserMapLoader`) is deferred until a real consumer demands the file-format pickup path (mirrors chapter 4.1.B slice ε's deferral of the NDJSON migration adapter for the same reason).

---

## Companion documents

- **Pre-scope (operational plan):** `CHAPTER_4_PRESCOPE_USERFK_REFLOW.md`.
- **V2-driver KPI:** `V2_DRIVER.md` (per-axis correctness stakes table — User FK reflow is HIGH).
- **Strategic frame precedents:** `CHAPTER_4_1_A_OPEN.md` + `CHAPTER_4_1_B_OPEN.md` (sibling chapters; same eight-axis discipline).
- **V1 oracle:** `src/Osm.Pipeline/UatUsers/UserMatchingEngine.cs:19-79` + `tests/Osm.Pipeline.Tests/UatUsers/UserMatchingEngineTests.cs` (differential fixture source).

---

## Closing

Chapter 4.2 closes the V2-driver KPI's User-axis verification depth. After this chapter, the remaining critical-path is chapter 4.3 (three-channel Diagnostics split) + the deferred chapter 4.1.A slices 6/7/8 + chapter 3.x DacpacEmitter (conditional). Chapter 5+ pragmatic close (cutover-day operator runbook + V1 sunset planning) is in sight.

Each slice ships with its own commit; the close ritual (eight items + the V1-input-envelope walk + AXIOMS A32 amendment cash-out) discharges at chapter 4.2 close.
