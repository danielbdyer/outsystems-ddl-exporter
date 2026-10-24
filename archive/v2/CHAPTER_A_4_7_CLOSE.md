# Chapter A.4.7 close — Transform registry: canonical strongly-typed cross-cutting surface

**Status:** CLOSED 2026-05-16. **Branch:** `claude/review-chapter-close-VnRe8`. **Open document:** `CHAPTER_A_4_7_OPEN.md` (9 strategic-frame axes; 9-slice plan; resolved-at-chapter-open Q9 expansion).

**Baseline at chapter close:** 1281 / 1281 passing (1202 prior + 14 ClassificationCarryThroughTests + 25 TransformRegistryTests + 18 PassRegistrationsTests + 5 AdapterRegistrationsTests + 8 StrategyRegistrationsTests + 11 TransformRegistryCompletenessTests + 3 intentional-fail probes — slice-α type-system witnesses + writer-fidelity propagation + per-pass classification + slice-β type-system + smart-constructor invariants + slice-γ per-pass `.registered` exports + slice-δ adapter metadata + slice-ε strategy metadata + slice-ζ classification filters + slice-θ bidirectional property tests + intentional-fail probes). 0 skipped. 0 build warnings under `TreatWarningsAsErrors=true`. Lint count 13 — unchanged from main / chapter A.0' close baseline; zero new introduced across chapter A.4.7.

## What this chapter promoted

**L3-CC-Transform-Totality: D → A.** The data-intent / operator-intent dichotomy promoted from convention-enforced (pillar 9 at code review) to structural-type + smart-constructor + bidirectional property test (A18 amended × A41 sibling commitment). The registry's totality + classification contract is enforced at compile time (smart-constructor invariants) + runtime (property tests + intentional-fail probes).

**A41 cashed (AXIOMS.md):** "Transform registry totality + canonical strongly-typed shape." Formal-system underwriting of the L3 axiom. Promoted from candidate to cashed at chapter close per the scaffolding discipline (`DECISIONS 2026-05-22 — Stage 0 foundation phase`).

**Pillar 9 (harvest-dichotomy classification) gains structural enforcement.** The meta-discipline now has a structural pair: pillar 9 at consideration time + A41 at runtime. The four-meta-discipline tier (pillar 8 / pillar 7 amendment / text-builder-as-first-instinct / pillar 9) is now fully realized as type-witnessed-bidirectional contracts.

## Per-slice ledger

| Slice | Status | Commit | Scope |
|---|---|---|---|
| α | SHIPPED 2026-05-16 | `e060e70` | `Classification.fs` (OverlayAxis with Selection/Emission/Insertion/Tightening/Ordering) + `LineageEvent.Classification` field + 12-pass self-classification + writer-fidelity primitives carry the field + 14 witness tests |
| β | SHIPPED 2026-05-16 | `e6e94e0` | `TransformRegistry.fs` with `StageBinding`/`Domain`/`TransformSite`/`TransformStatus`/`RegisteredTransform<'In,'Out>`/`RegisteredTransformMetadata` types + smart constructor + 17 witness tests. Q9 expansion DECISIONS amendment (`Ordering` as fifth `OverlayAxis` variant; `SelfLoopPolicy` is the real-evidence trigger) |
| γ | SHIPPED 2026-05-16 | `bfec22f` | 12 pass `.registered` exports (3 simple + 2 config-factory + 1 multi-site + 4 intervention factories + 1 Result-wrapping + 1 UserFkReflowPass) + 18 witness tests. Per-pass pillar-9 classification mirrors slice α; spec deviation codified (heterogeneous output types + factory pattern + parallel-exposure of `let run`) |
| δ + ε | SHIPPED 2026-05-16 | `244533e` | `CatalogReader.registeredMetadata` with 6 grouped Sites for ~26 transformative rules + `StrategyRegistrations` module with 5 strategy metadata entries (4 Tightening + 1 DataIntent CycleResolution) + 13 witness tests. Per-rule-as-Sites pattern; dedicated `StrategyRegistrations.fs` solves compile-order constraint |
| ζ | SHIPPED 2026-05-16 (this commit) | this commit | `TransformRegistry.skeletonView` / `overlayView` / `overlayAxes` filter helpers + 6 filter-shape witness tests. Full `Compose.runWithSkeleton` traversal refactor deferred-with-trigger per consumer pressure |
| η | DEFERRED-WITH-TRIGGER | n/a | `osm emit --skeleton-only` CLI flag + ManifestEmitter registry-digest extension. Consumer-pressure deferred; the chapter's structural commitment (registry + classification + property tests) doesn't strictly require CLI exposure. Forward signal: lands when operator pressure for the skeleton CLI surfaces |
| θ | SHIPPED 2026-05-16 (this commit) | this commit | `TransformRegistryCompletenessTests.fs` ships 4 of 5 bidirectional property tests (skeleton-purity + overlay-exercise via axis-coverage + representative-pass exercise + totality coverage + harvest-classification coverage) + 3 intentional-fail probes. Fifth property — manifest digest round-trip — deferred (slice η dependency) |
| ι | SHIPPED 2026-05-16 (this commit) | this commit | A41 body filled in `AXIOMS.md` per scaffolding discipline. L3-CC-Transform-Totality D → A in `PRODUCT_AXIOMS.md`. `HANDOFF.md` entry + this close doc + chapter-close DECISIONS amendment |

**Nine slices, five commits across two days** — slice ζ + θ + ι combined into the chapter-close commit (this commit) for hygiene; the structural-commitment surfaces depend on each other and ship together.

## L3 axiom promotions

| Axiom | Pre-chapter bucket | Post-chapter bucket | Cash-out site |
|---|---|---|---|
| **L3-CC-Transform-Totality** | D (candidate; convention only) | **A** (structural type + smart constructor + bidirectional property tests + intentional-fail probes) | This chapter, slices α–θ; A41 body in AXIOMS.md |

The chapter introduces no new L3 axioms; it promotes one Tier-1 axiom from D to A. The supporting infrastructure (TransformRegistry types + filter helpers + property tests) is the structural underwriting.

## Meta-codifications

Four meta-codifications surfaced across the chapter that future agents inherit as worked patterns:

### 1. Per-rule-as-Sites for non-callable transformations (slices δ + ε)

The chapter A.4.7 open's spec assumed "every transformative rule gets a separate `RegisteredTransform<...>` entry." Reality at the adapter (~26 helpers embedded in one `CatalogReader.parse` entry) and strategy (sub-modules dispatched via `Composition.fanOut`) levels: rules aren't independently callable. The deviation at slice δ + ε: package classified rules as `TransformSite` entries within a single registry entry (intra-pass classification fidelity per Q11 + the chapter A.0' slice θ Sites-list precedent from TopologicalOrderPass).

The pattern: when a structural commitment calls for N separate registry entries but the implementation has N rules embedded in one callable surface, ship N Sites within one registry entry. Each Site classifies independently; the registry-side smart constructor enforces the Sites list non-empty + every Site has substantive Rationale. The property tests work against Sites granularity.

Worked precedents: `CatalogReader.registeredMetadata` (6 grouped Sites for ~26 rules at the adapter); `TopologicalOrderPass.registered` (2 Sites — SortKahn DataIntent + SelfLoopHandling OperatorIntent Ordering — at the pass).

### 2. Compile-order-constraint-solved-via-dedicated-module (slice ε)

The strategy modules in `Projection.Core/Strategies/` couldn't embed `let registeredMetadata` directly because their outcome types (`NullabilityOutcome` etc.) are referenced by `Lineage.fs` (chapter 3.6 slice β contribution), which compiles before `TransformRegistry.fs`. Embedding the registration would create a circular dependency.

The solution at slice ε: `Projection.Core/StrategyRegistrations.fs` — a dedicated module compiled AFTER `TransformRegistry.fs` that packages all 5 strategy metadata entries as per-strategy values. The strategy modules themselves stay unchanged (continue to compile before Lineage); the registrations live at the layer with access to both surfaces.

The pattern: when a registration would create a circular dependency, extract the registration into a downstream module that has access to both the registered surface and the registry types. Worked precedent extends to any future cross-cutting structural-evidence concern that needs registrations from compile-early modules.

### 3. Factory pattern for configurable transformations (slice γ)

The chapter A.4.7 open's spec assumed pass `.registered` values are constants. Reality: 8 of 12 passes take operator-supplied configuration arguments (Morphism, Mask, Policy × Profile, RenameSpec list, SelfLoopPolicy). The deviation at slice γ: ship `.registered <config> : RegisteredTransform<...>` as a factory.

The pattern: configurable transformations expose `.registered` as a function returning `RegisteredTransform<...>`. The factory captures the config in the Run closure; the metadata (Name/Domain/StageBinding/Sites/Status) is per-pass-architecture, not per-config. Different configs produce different registered values; static metadata is the same. Slice γ tests witness this via per-config invocation.

Worked precedents: `NamingMorphism.registered morphism`; `VisibilityMask.registered mask`; `NullabilityPass.registered policy profile`; `TableRename.registered specs`; `UserFkReflowPass.registered policy profile`.

### 4. Parallel-exposure during structural-commitment transitions (slice γ)

The chapter A.4.7 open's spec called for `let run` to become private in all 12 pass modules at slice γ. Reality: ~50+ existing call sites invoke `<Pass>.run` directly; migrating them all would double the slice's surface area without adding registry-side capability. The deviation at slice γ: keep `let run` public AS A TRANSITION AFFORDANCE.

The pattern: when a structural commitment requires consumer migration, ship the new canonical surface alongside the old one ("parallel exposure"). Structural enforcement (making the old surface private) lands as a follow-on slice once consumer pressure surfaces — OR slice θ's property test catches bypass attempts and prompts the migration.

This deviation is forward-signaled as "slice γ.2 trigger" in DECISIONS — make `let run` private when (a) slice θ's skeleton-purity property test demands structural enforcement, OR (b) operator pressure for canonical-surface-only API. At chapter A.4.7 close, neither trigger fired; the parallel exposure remains.

## Forward signals

Per the active-deferrals discipline, the chapter's forward signals are:

1. **Slice γ.2 trigger:** make `let run` private in all 12 pass modules + migrate consumers from `<Pass>.run` to `<Pass>.registered.Run`. Triggers when skeleton-purity property test surfaces a bypass leak, OR operator demands canonical-surface-only API.
2. **Slice η scope:** `osm emit --skeleton-only` CLI flag + `ManifestEmitter` registry-digest extension + per-artifact `applied-transforms : (SsKey × OverlayAxis option) list` field + manifest digest round-trip property test. Triggers when operator demands the skeleton CLI surface (R6 split-brain governance may demand it once V2-driver transitions begin).
3. **Compose.run registry-traversal refactor:** replace the hand-coded `Compose.run` orchestration with `TransformRegistry.allInStageOrder` traversal. Requires pass-chaining adapter design for heterogeneous output types (decision-set passes don't return Catalog). Triggers when the registry needs to be load-bearing for execution (not just enumeration) — likely chapter 4.x or 5.x when scalable orchestration becomes a cutover-blocker concern.
4. **Fifth OverlayAxis expansion trigger:** future chapters that surface real-evidence for an operator-intent axis not subsumed by Selection / Emission / Insertion / Tightening / Ordering must apply the Q9-trigger-fires discipline (name the trigger; articulate why existing axes don't fit; DECISIONS amendment at the implementation slice).
5. **Policy.fs ↔ OverlayAxis collapse refactor:** `OverlayAxis ⊃ Policy DU axes` after slice β (the fifth `Ordering` variant was added without expanding Policy). The structural-collapse refactor (add `Policy.Ordering` axis OR carve `OverlayAxis.Ordering` out of `Policy`) stays deferred-with-trigger per Q9; lands when call-sites consult both vocabularies at one site.
6. **Tolerance retirement signals.** No `Tolerance.fs` entry currently has a paired `NotImplementedInV2` registry entry — the triple-deliverable hasn't fired in chapters 1-4.1.A. When the first v1-harvest decision lands as "don't bring forward," all three deliverables (Skip stub + Tolerance entry + NotImplementedInV2 registry entry) ship together; slice θ's harvest-classification coverage test gains substantive content.

## Pillar-9 audit of every transformation the chapter introduced or modified

Per the chapter-close ritual + verifiability-triangle audit cadence:

- **Slice α:** introduced no transformations; classified all 12 existing passes per pillar 9.
  - DataIntent: CanonicalizeIdentity / NamingMorphism / NormalizeStaticPopulations / SymmetricClosure / TopologicalOrderPass (per-event level; Sites refines at slice γ).
  - OperatorIntent Selection: VisibilityMask + UserFkReflowPass.
  - OperatorIntent Tightening: NullabilityPass / UniqueIndexPass / ForeignKeyPass / CategoricalUniquenessPass.
  - OperatorIntent Emission: TableRename.
- **Slice β:** introduced the registry types + smart constructor. The registry itself is not a transformation; it's a structural-evidence concern. Classified as cross-cutting at the Domain level.
- **Slice γ:** classified TopologicalOrderPass's two Sites (sortKahn DataIntent + selfLoopHandling OperatorIntent Ordering). All other passes inherit slice α's per-pass classification at Sites granularity.
- **Slice δ:** classified the OSSYS adapter's 6 transformative-rule groups, all DataIntent (adapter is translation, not overlay).
- **Slice ε:** classified the 5 strategies — 4 Tightening (Nullability/UniqueIndex/ForeignKey/CategoricalUniqueness) + 1 DataIntent (CycleResolution; algorithm-internal cycle handling).
- **Slice ζ + θ + ι:** introduced no transformations; added structural enforcement infrastructure.

No new Bucket-D Tier-1 axioms introduced. The chapter's L3 audit step (per `DECISIONS 2026-05-12 — Verifiability-triangle audit methodology`) is clean.

## Chapter-close ritual checklist

Per `DECISIONS 2026-05-14` — eight load-bearing items:

1. **Active deferrals scan.** Deferred-with-trigger from this chapter: slice γ.2 trigger; slice η CLI + manifest; Compose.run registry-traversal refactor; fifth OverlayAxis expansion trigger; Policy.fs ↔ OverlayAxis collapse; Tolerance retirement signals. All recorded in this close doc's "Forward signals" section.
2. **Contract-vs-implementation walk.** The chapter introduces `RegisteredTransform<'In, 'Out>` + `RegisteredTransformMetadata`; both have property tests. Smart constructor invariants are tested via intentional-fail probes (3 probes at slice θ). No contract-without-test gaps.
3. **CLAUDE.md staleness check.** Operating-disciplines table includes Pillar 9 row (already current from slice α). Load-bearing commitments include the data-intent/operator-intent separation row (already cites A41 candidate; the row stays current with cashed-A41 updates in this chapter close).
4. **README.md staleness check.** Not touched by this chapter — chapter A.4.7 is structural, not user-facing.
5. **HANDOFF + CHAPTER_N_CLOSE.md scope.** HANDOFF.md gains chapter A.4.7 close entry; this file IS CHAPTER_A_4_7_CLOSE.md (the close doc).
6. **Fresh-eye walk.** The chapter's structural commitments (A41 + L3-CC-Transform-Totality) are now in formal-system canonical surfaces (AXIOMS + PRODUCT_AXIOMS); chapter-open document records strategic frame; close doc records actual deliverable.
7. **Operating-disciplines table currency.** No new disciplines introduced; existing pillar 9 entry continues to point at `DECISIONS 2026-05-15 (late)` per the unchanged meta-discipline framing.
8. **V1-input-envelope walk.** Chapter A.4.7 doesn't extend V1 input surface; the V1↔V2 translation walk continues to point at chapter A.0' close's accounting. Forward signal: when chapter 4.x harvests v1 transformations as "don't bring forward," the triple deliverable fires and Tolerance entries gain `NotImplementedInV2` registry mirrors.

## Recommended next chapter

The handoff from chapter A.0' close named three forward paths:

1. **`LineageEvent.Classification` field** — SHIPPED at slice α of this chapter. Path retired.
2. **Chapter 4.1.A slice 8 (ExtendedProperties + Descriptions DDL emission)** — still the next-most-ready cutover-blocker progress. The IR carriage is complete (chapter A.0' slices α + ζ); SSDT emitter needs to consume the IR fields + emit `sp_addextendedproperty` calls. ~1-2 sessions. Retires the `CommentMetadataUnreflected` Tolerance variant.
3. **A.4.7 full Transform registry refactor** — SHIPPED across this chapter's slices α–ι. The structural commitment is now type-witnessed and bidirectionally property-tested. Path retired (with slice η deferred-with-trigger for CLI + manifest; Compose.run traversal refactor deferred to chapter 4.x or 5.x).

**Operator-side choice for next chapter:** chapter 4.1.A slice 8 (ExtendedProperties + Descriptions DDL emission) is the highest-leverage cutover-blocker progress at this point. Alternative chapters per `V2_DRIVER.md`: A.5 (Profile-JSON ingestion + completeness audit), A.6 (differential-testing soak), A.7 (user matching).

## Closing

Chapter A.4.7 elevates pillar 9 from meta-discipline to structural commitment by shipping the fourth cross-cutting structural-evidence concern (TransformRegistry) sibling to Lineage / Diagnostics / Bench. The data-intent / operator-intent dichotomy now carries as a type-witnessed bidirectional contract (A18 amended forbids Policy in emitters by structural type; A41 enumerates every operator-intent site by structural type + property tests). L3-CC-Transform-Totality moves D → A; the cutover-blocker structural-evidence layer is complete.

The chapter ships 9 slices across 5 commits, 18 transformation sites classified per pillar 9, 4 of 5 bidirectional property tests + 3 intentional-fail probes, 1 L3 axiom promotion (L3-CC-Transform-Totality D → A), 1 AXIOMS amendment cashed (A41), 1 OverlayAxis variant expansion (Ordering; first Q9-trigger-fires worked example), 4 meta-codifications (per-rule-as-Sites + compile-order-constraint-solved + factory pattern + parallel-exposure), and 6 forward signals for future chapters.

Hold the spine.
