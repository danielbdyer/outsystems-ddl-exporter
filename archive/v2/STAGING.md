# V2 — Staging (Foundation Phase / Stage 0)

**Date:** 2026-05-08; **chapter-3.1-close update:** 2026-05-30
**Status:** **Stage 0 shipped before chapter 3.1 opened (sessions 26 prework). Chapter 3.1 closed at session 36.** This document is preserved for the foundation-phase rationale + sequencing record. The Stage-0 deliverable status is in the "Stage 0 inventory" section; chapter-3.1 added a note per item where the chapter advanced or cashed the Stage-0 commitment.

**Purpose:** Stage 0 — the foundation phase before chapter 3.1 opens. Per `SPINE.md` (six structural inferences + seven primitives + seven tessellating patterns), a small set of foundation work landed *before* the first emitter chapter compounds across every subsequent chapter. This document names that work, sequences it, and quantifies the payoff.

**Companion documents:** `VISION.md`, `SPINE.md`, `PLAYBOOK.md`, `BACKLOG.md`.

---

## Contents

- [Why a foundation phase](#why-a-foundation-phase)
- [Stage 0 inventory](#stage-0-inventory)
- [Sequencing within Stage 0](#sequencing-within-stage-0)
- [What Stage 0 unlocks](#what-stage-0-unlocks)
- [What follows — the path to chapter 3.1](#what-follows--the-path-to-chapter-31)
- [LOC estimate and budget](#loc-estimate-and-budget)
- [Closing](#closing)

---

## Why a foundation phase

`SPINE.md` revealed that the V2 system is a category in the technical sense: typed values are objects, pure functions between them are morphisms, function composition is composition. Seven patterns tessellate (Π emitter / Adapter / Pass / Render / Compare / Property / Diff); seven primitives recur (SsKey-keyed Map / Writer-monad accumulation / Ordered linearization / Smart-constructor invariants / Origin tagging / Erasure declaration / Closed DUs with structured rationale).

The chapter pre-scopes are *concrete morphism constructions* — instantiations of the seven patterns with chapter-specific type variables. Every chapter delivers exactly one new tessellation instance.

**The foundation insight:** if the patterns and primitives are codified *first* — as F# types in `Projection.Core` rather than as conventions implicit in eight pre-scope documents — every chapter inherits the contracts at compile time. The chapter writes the *body* of the pattern; the *signature* is fixed.

Without the foundation phase, every chapter re-derives the pattern shape under chapter-local pressure. With it, the chapter writes itself.

Stage 0 is **not** chapter 3.1. Stage 0 is **everything that ships before chapter 3.1 opens**. Per the `CHAPTER_3_PRESCOPE_ARTIFACTBYKIND_REFACTOR.md` already-written plan, the cross-cutting refactor (`ArtifactByKind` + `SsKey` four-variant + `CatalogDiff`) is *part* of Stage 0 — but Stage 0 is wider. It includes the seven type primitives, the Render module skeletons, the property combinator library, the Tolerance taxonomy, the AXIOMS amendment scaffolding, the DECISIONS governance burst, the configuration port, the test support consolidation, the documentation currency checks, and the multi-environment generator skeleton.

Stage 0 ships as one coherent unit. After it closes, chapter 3.1 opens with all primitives in place; chapter 3.1 is a *consumer* of Stage 0's types, not a co-developer of them.

---

## Stage 0 inventory

Twelve items, each a concrete deliverable. Each cites the SPINE inference / leverage / pattern that motivates early take-up.

### S0.A — Type primitives in `Projection.Core`

**What:** Codify the seven tessellating patterns as F# type aliases in `src/Projection.Core/Types.fs` (new file).

```fsharp
namespace Projection.Core

// Pattern Π — Emitter (and its Profile-consuming + Diff-consuming variants)
type Emitter<'element> =
    Catalog -> Result<ArtifactByKind<'element>, EmitError>
type EmitterWithProfile<'element> =
    Catalog -> Profile -> Result<ArtifactByKind<'element>, EmitError>
type EmitterOverDiff<'element> =
    CatalogDiff -> Result<ArtifactByKind<'element>, EmitError>

// Pattern Adapter — boundary contract
type Adapter<'source, 'internal, 'error> =
    'source -> System.Threading.Tasks.Task<Result<'internal, 'error>>

// Pattern Pass — analysis or enrichment
type Pass<'output> =
    Catalog -> Policy -> Profile -> Lineage<'output>
type PassWithDiagnostics<'output> =
    Catalog -> Policy -> Profile -> Lineage<Diagnostics<'output>>

// Pattern Render — concrete syntax
type Render<'element, 'output> =
    SsKey list -> ArtifactByKind<'element> -> 'output

// Pattern Compare — equivalence-up-to-tolerance
type Compare<'tolerance> =
    'tolerance -> Catalog -> Catalog -> Diff

// Pattern Property — universally quantified canary
type Property = Catalog -> bool
type RelationalProperty = Catalog -> Catalog -> bool

// Pattern Diff — evolution as value
type DiffOf<'value> = 'value -> 'value -> Result<CatalogDiff, EmitError>
```

**Why early:** SPINE inference I6 (chapter as tessellation instance). Without these signatures, every chapter re-derives the shape; with them, every chapter's emitter / adapter / pass / etc. matches the type alias by typing.

**Where:** `src/Projection.Core/Types.fs` (new). Compile order: after `Identity.fs` and `Catalog.fs`, before `ArtifactByKind.fs`.

**LOC:** ~50 (type aliases only; bodies live elsewhere).

**Acceptance:** every chapter pre-scope's emitter signature matches `Emitter<'element>` (or its variants) verbatim; F# compiler enforces.

### S0.B — `ArtifactByKind` + `SsKey` four-variant + `CatalogDiff` foundation

**What:** Land the structural commitment refactor per `CHAPTER_3_PRESCOPE_ARTIFACTBYKIND_REFACTOR.md`. This is the cross-cutting work the original chapter 3 plan named; in the staged plan it's Stage 0's structural backbone.

Six slices (per the pre-scope):
1. Land `ArtifactByKind<'a>` and `Emitter<'a>` types (no consumer change).
2. Migrate `RawTextEmitter` to `emitSlices`.
3. Migrate `JsonEmitter` to `emitSlices`.
4. Migrate `DistributionsEmitter` to `emitSlices`.
5. `SsKey` four-variant DU split (big-bang within Core).
6. Retire substring T11 enforcement tests.
7. (Tail) `CatalogDiff` + `RefactorLogEmitter` skeleton — moved to chapter 3.5 because it requires `OssysOriginal` SsKeys from `SnapshotRowsets`.

**Why early:** SPINE primitives P1, P5, P7 (SsKey-keyed Map; Origin tagging; Closed DUs with structured rationale) are type-encoded here. SPINE leverage L7 (future emitters drop in trivially) and L10 (AXIOMS evolves monotonically) follow.

**Where:** per `CHAPTER_3_PRESCOPE_ARTIFACTBYKIND_REFACTOR.md` files inventory.

**LOC:** ~700 source + ~520 test.

**Acceptance:** existing 631 tests stay green; new property test `T11: emitSlices key-set equals Catalog.allKinds` passes for all three emitters.

### S0.C — Render module skeletons

**What:** Add `src/Projection.Core/Render.fs` with stubs for the per-target-shape composition layer:

```fsharp
namespace Projection.Core

[<RequireQualifiedAccess>]
module Render =
    let concatSql (order: SsKey list) (a: ArtifactByKind<string>) : string = ...
    let toJsonDocument (a: ArtifactByKind<JsonElement>) : JsonDocument = ...
    let toDacpac (order: SsKey list) (a: ArtifactByKind<TSqlObjectScript>) : DacPackage = ...
    let toSsdtDirectory (a: ArtifactByKind<SsdtFile>) : Map<RelativePath, string> = ...
    let toRefactorLogXml (a: ArtifactByKind<RefactorLogEntry list>) : string = ...
```

**Initial state:** stub bodies are `failwith "Render.<name>: not yet implemented"`. Implementations land per-chapter (3.3 fills in `toDacpac`; 3.5 fills in `toRefactorLogXml`; 4.1.A fills in `toSsdtDirectory`).

**Why early:** SPINE pattern Render — the API surface is universal; stubbing it now forces consistent shape across emitters and prevents per-chapter ad-hoc rendering choices.

**Where:** `src/Projection.Core/Render.fs` (new). Compile order: after `ArtifactByKind.fs`.

**LOC:** ~80 (stubs).

**Acceptance:** the module compiles; consumers can reference signatures even before implementations land.

### S0.D — Property combinator library

**What:** Add `tests/Projection.Tests/PropertyCombinators.fs` with reusable predicate composition operators:

```fsharp
namespace Projection.Tests

[<AutoOpen>]
module PropertyCombinators =
    let (.&&.) (p1: Catalog -> bool) (p2: Catalog -> bool) : Catalog -> bool =
        fun c -> p1 c && p2 c
    let (.||.) (p1: Catalog -> bool) (p2: Catalog -> bool) : Catalog -> bool =
        fun c -> p1 c || p2 c
    let negate (p: Catalog -> bool) : Catalog -> bool =
        fun c -> not (p c)
    let conditional (cond: Catalog -> bool) (p: Catalog -> bool) : Catalog -> bool =
        fun c -> if cond c then p c else true
```

**Why early:** SPINE pattern Property; F# point-free section names this as where point-free pays. Chapter 3.4 (canary property surface) lands ~12 predicates; chapter 4.x adds ~10 more. Combinator library means each new property composes without re-derivation.

**Where:** `tests/Projection.Tests/PropertyCombinators.fs` (new).

**LOC:** ~50.

**Acceptance:** unit tests confirm `(p1 .&&. p2) c = p1 c && p2 c` etc.; combinators chain.

### S0.E — Tolerance taxonomy

**What:** Add `src/Projection.Core/Verification/Tolerance.fs` with the named flag list per `CHAPTER_3_PRESCOPE_READSIDE_ADAPTER.md` §10:

```fsharp
namespace Projection.Core.Verification

type Tolerance = {
    IgnoreIndexNames         : bool
    IgnoreCheckConstraints   : bool
    IgnoreExtendedProperties : bool
    IgnoreDefaultNames       : bool
    AttributeOrderInsensitive: bool
    NewlineNormalization     : bool
    IgnoreHeaderComments     : bool
    CrossModuleFkResolution  : bool
    IgnoreNoCheckClause      : bool
    IgnoreTriggers           : bool
    IgnoreFingerprintHash    : bool
    PostDeployForeignKeys    : bool
    IgnoreV1OnlyKinds        : Set<string>
}

[<RequireQualifiedAccess>]
module Tolerance =
    /// Permissive default — all named divergences absorbed.
    /// See DECISIONS entries per flag for V1 file:line citations.
    let permissive : Tolerance = ...

    /// Strict — no tolerated divergence; fails on any mismatch.
    let strict : Tolerance = ...
```

**Why early:** SPINE primitive P6 (Erasure declaration). Tolerance is the comparator's *erasure set*; naming the flags upfront prevents ad-hoc additions during chapter 3.1.

**Where:** `src/Projection.Core/Verification/Tolerance.fs` (new).

**LOC:** ~30.

**Acceptance:** every flag has a doc comment citing its V1 file:line; every flag has a corresponding DECISIONS.md entry (S0.G).

### S0.F — AXIOMS.md amendment scaffolding

**What:** Append to `AXIOMS.md` the headers for each pending amendment with TBD bodies:

```markdown
## Amendments scheduled (chapter close)

The following amendments are scheduled for commitment at chapter close. Each
chapter agent writes the amendment text at chapter-close ritual step 6 per
DECISIONS 2026-05-14.

### T1 amended (binary normal-form composition) — TBD chapter 3.3 close
[Body to be written: byte-determinism for text/JSON; content-determinism via
DacFx model-API equality for binary; CDC-safety is composition.]

### T11 amended (structural type encoding) — TBD chapter 3 cross-cutting close
[Body to be written: Emitter<'element> = Catalog -> Result<ArtifactByKind<'element>, EmitError>;
T11 is type theorem.]

### T11 amended again (diff-typed inputs) — TBD chapter 3.5 close
[Body to be written: CatalogDiff is Catalog-typed; ArtifactByKind smart constructor
enforces T11 over diff target.]

### A1 amended (four-variant SsKey) — TBD chapter 3 cross-cutting close
[Body to be written: SsKey = OssysOriginal | Synthesized | DerivedFrom | V1Mapped;
A1 is type-stratified.]

### A35 candidate (Π-erased axes named) — TBD chapter 3.4 close
[Body to be written: equalModuloDacpacErasure declares which Catalog axes DACPAC
round-trip cannot preserve; the function IS the axiom.]

### A36 candidate (CatalogDiff exhaustiveness) — TBD chapter 3.5 close
[Body to be written: every SsKey in source ∪ target is in exactly one of
Renamed / Added / Removed / Unchanged; smart-constructor enforces.]

### A32 cash-out — TBD chapter 4.2 close
[Body to be written: UserMatchingStrategy DU; UserRemapContext value;
UserFkReflowPass.discover; sibling Π's consume context.]
```

**Why early:** chapter agents at close-ritual step 6 ("CHAPTER_N_CLOSE.md scope includes AXIOMS amendments") are reminded *what* to amend; the TBD body is the placeholder. Without scaffolding, chapter agents may forget which amendments are pending.

**Where:** append to `AXIOMS.md`.

**LOC:** ~40 (placeholder text).

**Acceptance:** chapter close ritual references the scheduled amendments by name.

### S0.G — DECISIONS pre-chapter-3 governance burst

**What:** Five DECISIONS.md entries to write *before* chapter 3.1 opens.

1. **Cutover-window split-brain governance rule (R6).**
   > "During dual-track operation, V2 emits-but-doesn't-ship. PR pipeline ships V1's artifact; V2's artifact feeds the canary; canary asserts V1 ≈ V2 modulo tolerance. Disagreement blocks PR. V2-to-production transition is per-environment-per-artifact-type, gated on N=10 consecutive green canary runs and explicit operator sign-off."

2. **Read-side adapter promoted to chapter 3.1.**
   > "Per Appendix F dogfood reframing, the read-side adapter has two consumers from day one (V1 verification + drift detection). It ships before SnapshotRowsets and DacpacEmitter. Chapter 3 sequencing: 3.1 read-side → 3.2 SnapshotRowsets → 3.3 DacpacEmitter → 3.4 canary closure → 3.5 RefactorLogEmitter."

3. **CLAUDE.md reading order — VISION.md added.**
   > "Per chapter-3 open hygiene, fresh agents now read VISION.md as part of the canonical reading order, after HANDOFF.md and before AXIOMS.md."

4. **T-30 / T-15 cutover fallback ladder gates.**
   > "V2-driver requires (a) chapter 3 closed with green canary on full 300-table Catalog; (b) chapter 4.1 (data triumvirate) shipping; (c) chapter 4.2 (user FK reflow) shipping; (d) ≥1 full UAT dry-run with cross-environment Profile/Policy pairs producing structurally-consistent artifacts. T-30 yellow → V2-augmented. T-15 unstable → V1-only. Hard rule: V1 stays warm through cutover+30."

5. **Stage 0 foundation phase commitment.**
   > "Per SPINE.md and STAGING.md, Stage 0 ships as one coherent unit before chapter 3.1 opens. Stage 0 includes: type primitives in Projection.Core; ArtifactByKind+SsKey+CatalogDiff foundation; Render module skeletons; property combinators; Tolerance taxonomy; AXIOMS amendment scaffolding; DECISIONS governance burst (this entry and four others); configuration port; test support consolidation; documentation currency checks; multi-environment generator skeleton."

**Why early:** the governance is *blocking* for chapter 3.1 to open. Without R6, dual-track is undefined; without the chapter sequencing decision, chapter 3.1's identity is ambiguous; without T-30/T-15, cutover gates are operationally undefined.

**Where:** append to `DECISIONS.md`.

**LOC:** ~600 lines of decision text.

**Acceptance:** all five entries land before chapter 3.1's first slice opens.

### S0.H — Configuration port (`config/default-tightening.json`)

**What:** Replicate V1's 50-line `config/default-tightening.json` in V2's location. Wire via `pipeline.json` or CLI option.

**Why early:** BACKLOG item 6.17 — labeled CRITICAL gap. The canary's tolerance defaults likely consume policy-toggle defaults; without the config, canary calibration drifts. V1's config is the operator-supplied baseline V2 inherits.

**Where:** `sidecar/projection/config/default-tightening.json` (new). Reader at `src/Projection.Adapters.Sql/PolicyDefaults.fs` (new).

**LOC:** ~50 (config) + ~30 (reader).

**Acceptance:** config file mirrors V1's structure; reader produces a `Policy` value with V1-equivalent defaults; round-trip differential test against V1's config passes.

### S0.I — Test support consolidation

**What:** Lift V1's `tests/Osm.TestSupport/` patterns into F#-callable form under `sidecar/projection/tests/Projection.Tests.Support/` (new shared library).

Per BACKLOG items 6.6–6.12:
- `SqlServerFixture.fs` — Testcontainers.MsSql 3.x wrapper, IAsyncLifetime, NewDatabase().
- `DockerAvailability.fs` — skip logic.
- `EmissionOutput.fs` — SSDT artifact bundle capture.
- `ProfileFixtures.fs` — mock Profile builders.
- `ModelFixtures.fs` — Catalog builders.
- `DirectorySnapshot.fs` — recursive file tree capture for golden-file diffs.
- `SqlServerFactAttribute.fs` — xUnit skip marker.

**Why early:** chapters 3.1, 3.4, 4.1.A, 4.1.B, 4.4 all need testcontainers fixtures + golden-file harness. Consolidating now prevents each chapter re-deriving the test infrastructure.

**Where:** new shared test project `tests/Projection.Tests.Support/Projection.Tests.Support.fsproj`.

**LOC:** ~600 across the seven files.

**Acceptance:** `Projection.Tests` and (forthcoming) `Projection.Tests.Canary` reference `Projection.Tests.Support` and consume the lifted fixtures.

### S0.J — Active deferrals + ADMIRE/AXIOMS/CLAUDE currency checks

**What:** Documentation hygiene before chapter 3.1 opens.

- **Active deferrals scan** (per `DECISIONS 2026-05-13` table-scan discipline): walk `HANDOFF.md` "Active deferrals" section; confirm none have silently fired.
- **ADMIRE.md currency**: walk every entry; confirm status string is current; flag any drift.
- **AXIOMS.md currency**: confirm A32, A34, T1-amended, T2, T11, A18-amended, A33 are present and accurate.
- **CLAUDE.md currency**: operating disciplines table points at current DECISIONS entries; F# feature surface table reflects new candidates from SPINE; load-bearing commitments mirror HANDOFF.

**Why early:** SPINE inference I6 (chapter as tessellation instance) requires the canonical surfaces be accurate. Drift here surfaces as confusion later.

**Where:** documentation-only edits to ADMIRE / AXIOMS / CLAUDE / HANDOFF.

**LOC:** ~documentation hygiene; LOC unbounded but typically ~50 lines of touched text.

**Acceptance:** chapter-open ritual finds no drift on the canonical surfaces.

### S0.K — Multi-environment Profile/Policy generator skeleton

**What:** Stub a generator producing varied `(Policy, Profile)` pairs for chapter 3.4's `policyOrthogonal` predicate.

```fsharp
// In tests/Projection.Tests/CatalogGen.fs (extend at S0.K)
let genEnvironment : Gen<EnvironmentLabel * Policy * Profile> = ...
let genFourEnvironments : Gen<(EnvironmentLabel * Policy * Profile) list> = ...
```

**Why early:** SPINE inference I1 (sheaf gluing). The multi-environment property test is chapter 3.4 territory but the *generator* must be staged earlier; per-environment Profile/Policy variation is generator design that needs sketching before the predicate is written.

**Where:** `tests/Projection.Tests/CatalogGen.fs` (extend; main file lands at chapter 3.4 slice 1).

**LOC:** ~30 (generator stubs).

**Acceptance:** chapter 3.4's first slice consumes the generator without redesign.

### S0.L — VISION + BACKLOG cross-references to SPINE/PLAYBOOK

**What:** Add explicit cross-references in VISION.md and BACKLOG.md to the new strategic surfaces (SPINE, PLAYBOOK, this STAGING document).

- VISION.md: documentation map at top references SPINE, PLAYBOOK, STAGING.
- BACKLOG.md: header references SPINE patterns; per-chapter index references the chapter's tessellation instance.

**Why early:** new readers (fresh agents) must find their way through eight new strategic surfaces. Cross-references prevent re-discovery.

**Where:** `VISION.md` documentation map + new section; `BACKLOG.md` header + Stage 0 section.

**LOC:** ~30 lines of cross-references and ~150 lines of new Stage 0 section in BACKLOG.

**Acceptance:** every strategic document points at its peers; reading order is unambiguous.

---

## Sequencing within Stage 0

Stage 0 has internal ordering. Some items can ship in parallel; others have dependencies.

### Tier 1 — no dependencies (can run in parallel)

- **S0.F** AXIOMS amendment scaffolding (documentation; no code).
- **S0.G** DECISIONS governance burst (documentation; no code).
- **S0.J** ADMIRE / AXIOMS / CLAUDE currency checks (documentation hygiene).
- **S0.L** VISION / BACKLOG cross-references (documentation).

These four are **pure documentation hygiene + governance**. Land them first; they unblock the next tiers.

### Tier 2 — type primitives (depends on Tier 1; foundation for code)

- **S0.A** Type primitives in `Projection.Core`.

This is the keystone. Once `Emitter<'element>`, `Adapter<'source, 'internal, 'error>`, etc. are F# type aliases, every chapter's signature matches by typing. ~50 LOC.

### Tier 3 — structural commitment refactor (depends on Tier 2)

- **S0.B** ArtifactByKind + SsKey four-variant + CatalogDiff (per `CHAPTER_3_PRESCOPE_ARTIFACTBYKIND_REFACTOR.md`).

This is the largest Stage 0 item — six slices, ~700 LOC source + ~520 LOC test. It depends on the type primitives but consumes them; subsequent items consume `ArtifactByKind`.

### Tier 4 — primitive support modules (depends on Tier 3)

Can ship in parallel:

- **S0.C** Render module skeletons (depends on `ArtifactByKind`).
- **S0.D** Property combinator library (depends on `Catalog`).
- **S0.E** Tolerance taxonomy (depends on `Catalog` and `Diff`).
- **S0.H** Configuration port (independent; can shift earlier if desired).
- **S0.I** Test support consolidation (independent; can ship in parallel with Tier 3).
- **S0.K** Multi-environment generator skeleton (depends on `Catalog`).

### Estimated calendar

If a "session" is ~half a workday: Stage 0 totals ~12-15 sessions across Tier 1 → Tier 4. Tier 3 (S0.B) is the largest single item and runs across 4-5 sessions; the others are 1-2 sessions each.

A coherent Stage 0 push: **~2 weeks of focused work** by the maintainer + AI collaboration.

---

## What Stage 0 unlocks

Quantitatively: every subsequent chapter is ~30-40% smaller than its pre-scope LOC estimate, because the chapter consumes Stage 0 primitives rather than re-deriving them.

Qualitatively, ten payoffs:

### U1. Chapter 3.1 opens cleanly

`Projection.Adapters.Sql.ReadSide.CatalogReader` matches `Adapter<connStr * schemas, Catalog, ReadSideError>` by typing. The CatalogEquivalence comparator matches `Compare<Tolerance>`. The Tolerance flags are pre-named with citations. The C# orchestrator (Projection.Pipeline) consumes Tier 2 + Tier 3 primitives. **Chapter 3.1's slice 1 ships in days, not weeks.**

### U2. Chapter 3.4 inherits a complete predicate combinator library

`siblingChorusAgrees`, `t1ByteEqual`, `coproductPreservation`, `roundTripBySsKey`, `idempotentRedeploy`, `renameSurvives`, `policyOrthogonal` — each composes via `.&&.`, `.||.`, `negate` (S0.D). The generator (S0.K) supplies multi-environment variation. **Chapter 3.4 writes the predicate bodies, not the combinator scaffolding.**

### U3. Chapter 4.1.A's SSDT DDL emitter is one chapter, not two

Without Stage 0, chapter 4.1.A would re-derive: `SsdtDdlEmitter` signature; the `Render.toSsdtDirectory` shape; the `Tolerance` flags' V1-citations; the manifest emitter's structural commitments. With Stage 0, all primitives are in place; chapter 4.1.A is a body-only chapter. **~30% LOC reduction; ~25% session reduction.**

### U4. Chapter 4.1.B's CDC-aware data triumvirate consumes existing primitives

`StaticSeedsEmitter` matches `EmitterWithProfile<DataInsertScript>` by typing. `MigrationDependenciesEmitter` and `BootstrapEmitter` likewise. The `DataEmissionComposer` consumes `TopologicalOrder` from existing pass output. **The triumvirate ships as three small body implementations + one composer + one promoted-lane integration test.**

### U5. Chapter 4.4's RemediationEmitter is a one-line composition

Per SPINE inference I3 + leverage L4: `RemediationEmitter.emit = dacpacEmitter ∘ CatalogDiff.toRemediationCatalog ∘ CatalogDiff.between`. With Stage 0's `CatalogDiff` and Render skeletons in place, RemediationEmitter is ~360 LOC including tests. **Chapter 4.4 is a small chapter.**

### U6. Drift detection is not a chapter

Per SPINE leverage L3: `drift = compare (read-side deployed) (project-from-OSSYS source)`. Both halves are Stage 0 deliverables (read-side adapter + comparator). Drift detection ships as a CI cron job — the GitHub Action / Azure DevOps pipeline configuration to run the check on a schedule. **No chapter required.**

### U7. Future emitters cost ~100 LOC each

Per SPINE leverage L7: any new emitter is `Emitter<'NewElement>` + a `Render.toNew : SsKey list -> ArtifactByKind<'NewElement> -> NewOutput`. The body is the kind-by-kind rendering logic; everything else is inherited. **GraphQL emitter (deferred), Faker emitter (deferred), Post-IS external entity declaration emitter (deferred) — each is ~100 LOC.**

### U8. AXIOMS amendments compose monotonically

Per SPINE leverage L10: the amendments scaffolded at S0.F are *specializations* and *extensions*, not replacements. T1's binary specialization, T11's diff extension, A1's four-variant refinement — each strengthens the system without invalidating prior proofs. **No proof-rewriting overhead; chapter close ritual is straightforward.**

### U9. Multi-environment cutover is configuration

Per SPINE inference I1 (sheaf) + leverage L6: the four-environment cutover is one algebra applied to four `(Policy, Profile)` pairs. The generator (S0.K) supplies the variation; the predicate `policyOrthogonal` (chapter 3.4) proves the gluing. **No new code is required for N environments beyond the generator stub.**

### U10. Pre-cutover governance is operationally defined

Per S0.G: the five DECISIONS entries (R6 split-brain, chapter sequencing, CLAUDE reading-order, T-30/T-15 gates, Stage 0 commitment) operationalize the cutover fallback ladder. The CI configurations selecting which projection is authoritative per environment-per-artifact-type are scaffolded. **The cutover decision criterion at T-30 is a YAML edit, not a code change.**

---

## Chapter-3.1-close status update (2026-05-30)

Chapter 3.1 closed at session 36. The chapter's substantive work
advanced or cashed several Stage-0 commitments:

- **S0.A type primitives (`Types.fs`)** — chapter-3.1 retired the
  `Adapter<'source, 'inner>` alias (no consumers; dragged
  `System.Threading.Tasks` into Core). Other aliases remain
  Stage-0 reservations awaiting their cash-out chapters.
- **S0.B structural commitment refactor (`ArtifactByKind` + `SsKey`
  four-variant + `CatalogDiff`)** — `ArtifactByKind` and `SsKey`
  four-variant shipped at Stage 0; `CatalogDiff` remains
  reserved for chapter 3.5 (RefactorLog). Chapter 3.1 audit Agent 2
  flagged that `Emitter<'element>` shape is declared but unrealized
  (three Π's return `string`); chapter 3.5's Π port realization
  closes that gap.
- **S0.E Tolerance taxonomy** — still pending; `PhysicalSchema.diff`
  has eight axes (chapter-3.1 added Rows + RowDigests); the
  Tolerance taxonomy lands at chapter 3.4 (canary property surface).
- **S0.F AXIOMS scaffolding** — chapter 3.1 close cashed A35 (Π's
  output is a deterministic statement stream), A36 (bulk-vs-
  incremental is realization-layer policy), A39 (aggregate-root
  smart constructors), A40 (harmonization-via-parameterization).
  Renumbered A37/A38 (Π-erased axes; CatalogDiff exhaustiveness)
  for chapters 3.4 / 3.5.
- **S0.G five `DECISIONS.md` governance entries** — landed at Stage 0
  as scheduled. Chapter 3.1 added 13 substantive resolutions; the
  audit-driven refactor protocol (five-agent epistemic-tier shape)
  is the chapter-3.1 governance contribution.
- **S0.J currency checks** — chapter 3.1 close walked
  ADMIRE / AXIOMS / CLAUDE / HANDOFF / KICKOFF / README; all
  currents updated.
- **Other Stage-0 items (S0.C-S0.K-S0.L)** — covered through the
  chapter-3.1 work or remain reserved per Stage-0 sequencing.

The Stage-0 commitment held: **the foundation phase shipped before
chapter 3.1 opened, and the chapter compounded on it.** The
remaining Stage-0 reservations (Tolerance taxonomy; CatalogDiff
exhaustiveness; full Π port realization) route to their named
sub-chapters.

## What follows — the path beyond chapter 3.1

After Stage 0 closes, chapter 3.1 opens with:

- All seven type primitives in `Projection.Core` (S0.A).
- `ArtifactByKind`, `SsKey` four-variant, `CatalogDiff` shipped (S0.B).
- `Render` skeletons reserving the API surface (S0.C).
- Property combinator library ready for use (S0.D).
- Tolerance taxonomy with named flags + DECISIONS citations (S0.E).
- AXIOMS amendments scheduled with TBD bodies (S0.F).
- DECISIONS governance burst landed (S0.G).
- Configuration port done (S0.H).
- Test support consolidated (S0.I).
- Documentation hygiene clean (S0.J).
- Multi-environment generator stub in place (S0.K).
- Strategic-document cross-references current (S0.L).

Chapter 3.1's slice 1 (JSON round-trip canary) ships *immediately* — the JsonEmitter + CatalogReader pair already exists; it just needs an integration test invoking them through the new types.

Chapter 3.1's slice 2 (read-side adapter skeleton + queries 1-2) ships next; the adapter signature matches `Adapter<connStr * schemas, Catalog, ReadSideError>` from S0.A.

Chapter 3.1's slice 3 (read-side queries 3-6) extends the adapter; tier-1 property tests use S0.D combinators.

Chapter 3.1's slice 4 (CatalogEquivalence comparator) consumes S0.E Tolerance taxonomy.

Chapter 3.1's slice 5 (Tolerance profile calibration) writes DECISIONS entries citing the V1 file:line for each flag — using S0.G's pattern.

Chapter 3.1's slice 6 (`Projection.Pipeline` orchestrator) lands the C# host; consumes S0.I test support patterns.

**Chapter 3.1 closes with: V2 verifies V1 against ephemeral SQL Server, with named tolerances, with a triangulation comparator, with all seven type primitives in production use.** The cutover fallback ladder's V2-augmented mode is operational.

---

## LOC estimate and budget

| Item | LOC source | LOC test | LOC docs |
|---|---|---|---|
| S0.A Type primitives | ~50 | — | — |
| S0.B ArtifactByKind + SsKey + CatalogDiff | ~700 | ~520 | ~50 |
| S0.C Render skeletons | ~80 | — | — |
| S0.D Property combinators | — | ~50 | — |
| S0.E Tolerance taxonomy | ~30 | — | — |
| S0.F AXIOMS scaffolding | — | — | ~40 |
| S0.G DECISIONS burst | — | — | ~600 |
| S0.H Configuration port | ~80 | — | ~50 |
| S0.I Test support consolidation | — | ~600 | — |
| S0.J Currency checks | — | — | ~50 |
| S0.K Generator skeleton | — | ~30 | — |
| S0.L Cross-references | — | — | ~180 |
| **Total** | **~940** | **~1200** | **~970** |

**Stage 0 total: ~3110 LOC.** That's substantial, but it compounds across the eight chapter pre-scopes.

Per chapter 3 pre-scopes, total LOC is ~10,000 source + ~7,500 test. After Stage 0, *each chapter's actual delivery is ~30-40% smaller*. Net: ~2,500-3,500 LOC saved across chapters 3.1-4.4.

**Stage 0 pays back at chapter 3.3.** Every chapter beyond that is pure compounding.

---

## Closing

Stage 0 is the foundation phase. Per SPINE, the system is a category; the chapter pre-scopes are concrete morphism constructions. Stage 0 codifies the categorical structure as F# types so every subsequent chapter is a body implementation, not a co-derivation of the type signatures.

The work is real (~3,000 LOC). The payoff is real (~2,500-3,500 LOC across chapters; immeasurable cognitive load reduction; structural integrity by typing). The sequencing is concrete (Tier 1 docs first, Tier 2 type primitives, Tier 3 structural refactor, Tier 4 support modules in parallel).

**After Stage 0 closes, every chapter is a tessellation instance of one pattern with one type variable.** The chapter's slice list is the implementation; the pattern is the contract; the test is the universal property of the pattern.

V1 ships the cutover. V2 makes it verifiable through a sibling chorus over a typed algebra. **Stage 0 is the moment the algebra becomes types.**

— Recorded for the receiving agent.
