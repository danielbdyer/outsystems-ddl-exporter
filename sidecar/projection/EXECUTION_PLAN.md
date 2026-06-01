# EXECUTION_PLAN — Cross-Thread Waves, Per-Slice Specs, and the Endgame

> **Status:** living execution plan. Authored 2026-05-30 from a six-probe code-grounded
> sweep of the V2 sidecar. This document is the *actionable* sibling to `V2_DRIVER.md`
> (the destination KPI) and `BACKLOG.md` (the operational ledger): it turns every open
> thread into a buildable slice with files, signatures, acceptance criteria, and the
> governance artifact each slice owes. It is **not** a chapter — it is a cross-thread
> map a fresh agent can execute against directly.
>
> **How this was produced.** Six parallel research probes (core-loop, Transfer, registry/
> policy-intelligence, IR-fidelity/DacFx, cutover/operator-surface, Lifecycle/hygiene)
> read the tree and returned decision-ready backlogs; the highest-stakes claims were
> spot-verified against source before this plan was committed (see §0.1). Where a probe's
> count or framing drifted from the live tree, the verified number is used and the drift
> is noted inline.
>
> **Companion surfaces:** `V2_DRIVER.md` (why), `BACKLOG.md` (what/when), `PRODUCT_AXIOMS.md`
> (the L3 contract), `AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md` (the bucket model),
> `CUTOVER_READINESS_BRIEF.md` (the six-axis verdict), `PRESCOPE_TRANSFER.md` (the epic).

---

## Contents

- [0. The meta-finding: doc–reality drift](#0-the-meta-finding-docreality-drift)
- [I. The envisioned endgame — where even this is headed](#i-the-envisioned-endgame--where-even-this-is-headed)
- [II. How to read a slice spec (conventions + inherited house rules)](#ii-how-to-read-a-slice-spec)
- [III. The waves](#iii-the-waves)
  - [Wave 0 — Truth reconciliation](#wave-0--truth-reconciliation)
  - [Wave 1 — Restore verification integrity](#wave-1--restore-verification-integrity)
  - [Wave 2 — Close the core decision→emission loop](#wave-2--close-the-core-decisionemission-loop)
  - [Wave 3 — Cutover critical path](#wave-3--cutover-critical-path)
  - [Wave 4 — Bidirectional frontier + capability](#wave-4--bidirectional-frontier--capability)
  - [Wave 5 — Blocked / defer-with-trigger](#wave-5--blocked--defer-with-trigger)
  - [Wave 6 — Substantiating the isomorphism (the A→B Total-Migration epic)](#wave-6--substantiating-the-isomorphism-the-ab-total-migration-epic)
- [IV. Dependency graph, critical path, sequencing](#iv-dependency-graph-critical-path-sequencing)
- [V. The endgame backlog — closing *that* gap](#v-the-endgame-backlog--closing-that-gap)
- [VI. Decisions owed + open external gates](#vi-decisions-owed--open-external-gates)

---

## 0. The meta-finding: doc–reality drift

The single most important result of the sweep is not any slice. It is that **the canonical
surfaces mis-state reality in four places**, and that corrupts every downstream cutover
decision because the team steers by a false readiness map.

| Surface | Claims | Verified reality |
|---|---|---|
| `BACKLOG.md` / `CUTOVER_READINESS_BRIEF.md` (snapshot 2026-05-18) | RemediationEmitter, SummaryFormatter, LiveProfiler "deferred to ch 5+" | **Shipped, wired, registered, tested.** `RemediationEmitter.fs`, `SummaryFormatter.fs`, `LiveProfiler.fs` exist; both diagnostics emitters are invoked in `Pipeline.fs` and write `manifest.remediation.sql` + `manifest.summary.txt`. |
| `CHAPTER_A_4_7_PRIME_OPEN.md` (still OPEN) | registry-driven execution is the "next keystone" | **Done.** `CHAPTER_A_4_7_PRIME_CLOSE.md` exists (CLOSED 2026-05-17); `Compose.project` already folds `RegisteredTransforms.allChainSteps`; `--skeleton-only` ships; skeleton-purity is true-execution. |
| Cutover brief | DIAGNOSTICS axis 🟡; CLI = 4 verbs | DIAGNOSTICS should be 🟢; CLI dispatches ~7 verbs (emit/deploy/canary/skeleton/approve/transfer/full-export; `extract`/`profile` strings also present; `analyze` absent). |
| `AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md` (~:1200) | L3-S2 DACPAC round-trip = "Bucket A modulo A37" | **No witness exists.** `DacpacRoundTripTests`, `Catalog.equivalent`, `equalModuloDacpacErasure` are absent from `src/` and `tests/`. A claimed-verified axiom with no executable test. |

Three places the code is **ahead** of its docs; one place it is **behind**. "Doc–reality
reconciliation" surfaced independently as the recommended first slice in **three of six
probes**. This is why Wave 0 leads with truth reconciliation — and why the endgame (§I, §V)
treats *making the docs derive from the code* as a first-class structural goal rather than a
recurring ritual the chapter-close keeps failing to enforce.

### 0.1 Verification log (spot-checks run before commit)

- `ReadSide.fs`: confirmed `Triggers=[]` (~L683), `Sequences=[]` (~L1020), `ColumnChecks=[]`
  (~L684), `ExtendedProperties=[]` (~L382/685/1013), `DefaultValue=None` (~L379),
  `Computed=None` (~L381), `Description=None` (~L360/675). **The hollow round-trip leg is real.**
- `Pipeline.fs` ~L163-164: comment *"emitters do not yet consume (decision-set consumption is
  a future-chapter concern)"* — **verbatim.**
- DACPAC predicate / `Catalog.equivalent`: **absent** (zero hits). The phantom-axiom defect is real.
- AsyncStream call-sites (outside the def file): `empty/ofList/map/mapAsync/iter/fold` = **0
  callers each** (6 dead); `bufferUpTo` = 1, `batchesOf` = 1 (single-consumer, keep); `toList`
  = 3, `probe` = 3. **Prune target is 6 functions, not the "8/9" the audit estimated.**

---

## I. The envisioned endgame — where even this is headed

The waves below close *known* gaps. But read together they reveal a deeper truth about the
project, and a destination beyond the cutover that is worth naming explicitly so the work
bends toward it.

### The pattern under the gaps

Four of the most consequential findings are the *same shape* in different clothes:

1. **The canary is hollow for six features** (Wave 1) — the proof apparatus claims to verify
   what it cannot observe.
2. **A claimed-Bucket-A axiom has no test** (Wave 1) — the verifiability triangle has a
   false node.
3. **The decision→emission loop is open** (Wave 2) — the engine *decides* correctly and then
   emits something its decisions never touched; the two halves are unproven to agree.
4. **The docs drift from the code** (Wave 0) — the "single source of truth" surfaces are not
   themselves verified against the system they describe.

Every one of these is a **completeness/soundness gap in a proof engine that believes it is
already total.** V2's whole thesis (per `VISION.md`) is to convert V1's *implicit* correctness
into *explicit, type-witnessed, falsifiable* correctness. The sweep shows the conversion is
~80% done and, crucially, that the **remaining 20% is exactly the part that makes the proof
engine trustworthy about its own coverage.** A proof engine with unverified coverage is a
strictly weaker artifact than V1's "trusted by experience" — because it adds the *illusion*
of proof. The highest-ROI work is therefore not new capability; it is making the existing
proof *total and self-aware.*

### The destination: **the Total Adjunction / self-verifying engine**

The codebase already proves one adjunction — `Ingestion ∘ Projection = id` (H-050) — at the
schema level, and the Transfer epic extends it to data. The envisioned endgame generalizes
this into the organizing principle of the whole system. Call it **the Total Adjunction**: a
state in which *every axis the engine owns round-trips through the adjunction, every claim is
executable, every input is operational, and the documentation derives from the proof.* Four
completeness conditions:

**C1 — Round-trip totality (the adjunction covers every axis).**
Today the adjunction is proven for Columns + ForeignKeys (schema) and is being extended to
rows (Transfer). The endgame closes it over **all eight `PhysicalSchema` axes plus the
decision axis**: triggers, sequences, defaults, computed, checks, extended properties (Wave 1
un-hollows these), *and* the tightening decisions themselves (Wave 2 emits them; the endgame
**proves the emitted artifact read-back reproduces the `DecisionOverlay`** — see §V E3). When
this holds, "canary green" is a total statement: there is no axis the engine emits that the
canary cannot see.

**C2 — Executable-axiom totality (no claim without a witness).**
Every numbered axiom (A1–A42+, T1–T11) and every L3 product axiom has a green `AxiomTests.fs`
entry, a convention-witness, or a `Skip` carrying its promotion trigger — and **a CI gate
refuses to let the audit record a bucket the tests don't support** (§V E1). The phantom
DACPAC-A2 class becomes structurally impossible. The verifiability triangle stops being a
periodically-audited document and becomes a continuously-checked invariant.

**C3 — Input totality (the four-input algebra is complete).**
`Project = Π ∘ E` declares four inputs — `Catalog × Policy × Profile × Lifecycle` — but
`Lifecycle` is named-only; the type does not exist. The endgame operationalizes it (§V E4),
which is the **precondition that gives the policy-intelligence substrate real consumers**:
speculative multi-policy execution, policy diffing, and approval workflows only become
load-bearing once there is a *timeline* to diff across and version against. Lifecycle is not a
side quest; it is the missing fourth leg that turns a one-shot compiler into a
history-aware engine and unlocks the `LineageTree`/`PolicyDiff`/`VersionedPolicy` machinery
that was built ahead of its consumers.

**C4 — Documentation totality (the docs derive from the system).**
The doc-drift meta-finding is not a discipline failure to be scolded; it is a *missing
derivation*. The endgame generates the readiness map, the six-axis verdict, and the
BACKLOG status **from `AxiomTests.fs` bucket counts + the `RegisteredTransform` registry**
(§V E2, E5), so the surfaces *cannot* drift because they are projections of the code — the
same move the codebase already made for emission (typed AST over string-building) applied to
its own governance. The registry, which already drives execution and provenance, becomes the
single spine that also drives documentation.

**C5 — Orthogonality (the axes are a basis, not a bundle).** *(Added by the 2026-05-31 red-team; NORTH_STAR T-V.)*
Round-trip totality (C1) proves each axis round-trips; it does **not** prove the axes compose without hidden
coupling. The red-team found two that secretly couple: a `Decision` NOT-NULL tightening breaks the `Data` transfer
mid-load (no pre-flight), and an `Identity` rename diverges the physical coordinates `Data` matches on (no
RefactorLog consumed by Transfer). *Closed when* each cross-axis dependency is a named pre-flight or typed input —
**Wave 6.B**.

**C6 — Spanning (the basis covers the operation).** *(Red-team; NORTH_STAR T-VI.)* The five axes do not span the
operator's real A→B migration: three load-bearing dimensions live in **no** axis — Permissions/Security (a
write-denied sink silently transfers zero rows), Transactionality/Rollback (a mid-transfer failure corrupts the
target), Connection pre-flight. *Closed when* the missing dimensions are axes or pre-flight gates and the composed
`migrate A B` is atomic-or-resumable — **Wave 6.C / 6.D**.

The deeper red-team reading: C1's witnesses are the **L1 floor** (reached 5/5 on 2026-05-31), but the round-trips
are *adjunctions, not equivalences* — partial isos with silent erasures (**L2** faithfulness is the open work), and
the axes do not yet **compose** into the one-command migration (**L3**). The full buildable ladder is **Wave 6**;
C5/C6 are its orthogonality and spanning legs.

### What it would take to close *that* gap

The endgame is reachable from the waves with **five additional slices (§V E1–E5)**, none of
which is research:

- **E1 Verifiability CI gate** — a test that parses the audit's bucket assignments and fails
  the build if any axiom's claimed bucket exceeds its `AxiomTests.fs` evidence. ~M. Closes C2
  and the phantom-axiom class permanently.
- **E2 Generated readiness map** — derive the cutover six-axis table + BACKLOG per-feature
  status from `AxiomTests.fs` + registry metadata; the hand-maintained tables become generated
  artifacts. ~M. Closes C4 for readiness.
- **E3 Decision-layer adjunction** — once Wave 2 emits decisions and Wave 1 un-hollows
  read-back, add the property `ReadSide(deploy(emit(C, overlay)))` reproduces `overlay` on the
  tightening axes. ~M. Closes C1 for the decision axis — the deepest unification, because it
  proves the engine's *opinions* are faithfully transmitted, not just its structure.
- **E4 Lifecycle operationalization** — the `Lifecycle`/`Version`/`Timeline` types + the
  `evolutionChain`/`replayTo` algebra (full spec in §5.3), wired to its first real consumer
  (refactor-log baseline from a stored prior version). ~L. Closes C3 and unlocks
  policy-intelligence consumers.
- **E5 Registry-as-documentation** — emit a `registry.manifest.md` / readiness fragment from
  `RegisteredTransform` metadata + `AxiomTests` buckets; wire it into the chapter-close ritual
  as a generated (not authored) surface. ~M. Closes C4 structurally.

**The thesis in one line:** the waves make the engine *correct*; the endgame makes the engine
*provably and self-describably correct about its own correctness* — which is the only thing
that makes "replace V1" a stronger claim than "trust V1." Everything in §V is the difference
between a tool that is right and a tool that can *show* it is right, continuously, without a
human re-auditing it.

---

## II. How to read a slice spec

Each slice is a single commit. Fields:

- **Wave / Effort / Status / Deps** — Effort is S (≤½ session), M (~1 session), L (multi-session).
  Status ∈ {buildable-now, blocked:`<gate>`, defer:`<trigger>`}.
- **Goal** — the one-sentence why.
- **Files** — real paths (symbols are cited rather than line numbers where line drift is likely).
- **Types & signatures** — the F# surface to add/change.
- **Acceptance** — the test(s) (backtick-named per house rule) and the canary/round-trip assertion.
- **Governance** — the `DECISIONS.md` entry gist and any `AXIOMS.md`/`PRODUCT_AXIOMS.md` amendment + ID.
- **Risks** — the subtleties that bite.

**Inherited house rules (every slice obeys, stated once):**
slices are separate commits; every resolved question gets a `DECISIONS.md` entry; axioms are
scaffolded at chapter open and cashed at close; property/example tests cite the axiom in
backtick names; the operator-reality canary (`scripts/perf-gate.sh`) is the forcing function;
`Projection.Core` is pure (no I/O, no time — `PRJ001` analyzer); A18-amended — emitters consume
`Catalog × Profile`, never `Policy`; pillar 9 — every transform site is classified `DataIntent`
vs `OperatorIntent of OverlayAxis`; two-consumer threshold for primitive extraction; D9 —
no credentials in `Config`; R6 — V2 owns no production write path during dual-track; the tiered
runner `scripts/test.sh` (never one `dotnet test`); TRX-first failure capture.

---

## III. The waves

### Wave 0 — Truth reconciliation
*Stop the codebase lying about itself; pay down pre-authorized debt. All S-effort, near-zero risk. ~1 session.*

#### 0.1 — Reconcile the stale canonical surfaces
- **Wave/Effort/Status/Deps:** 0 / S / buildable-now / none
- **Goal:** Make `BACKLOG.md`, `CUTOVER_READINESS_BRIEF.md`, and the stale OPEN chapter reflect
  what actually shipped, so cutover decisions steer by reality.
- **Files:** `BACKLOG.md` (Phase-8 cash-out table — strike RemediationEmitter/SummaryFormatter/
  LiveProfiler rows as shipped); `CUTOVER_READINESS_BRIEF.md` (§2 Axis-4 + §3 composite table:
  DIAGNOSTICS 🟡→🟢; CLI verb count 4→7); `CHAPTER_A_4_7_PRIME_OPEN.md` (header: "SUPERSEDED — see
  `CHAPTER_A_4_7_PRIME_CLOSE.md`"); `V1_PARITY_MATRIX.md` (rows 81/83/85/102 → 🟢 PARITY);
  `DECISIONS.md` (one reconciliation entry + Active-deferrals scan).
- **Types & signatures:** none.
- **Acceptance:** an Active-deferrals scan shows zero stale "deferred to ch 5+" references to the
  three shipped components; the two stale OPEN/CLOSE docs no longer contradict each other; a grep
  gate (`grep -rn 'RemediationEmitter.*defer' *.md` returns nothing) added to the chapter-close
  checklist.
- **Governance:** `DECISIONS` gist — *"Doc reconciliation: RemediationEmitter + SummaryFormatter +
  LiveProfiler shipped after the 2026-05-18 ledger snapshot; DIAGNOSTICS axis flip-eligible; PRIME
  refactor confirmed closed. Canonical surfaces corrected."* This entry is also the seed for §V E2
  (generate, don't author, these tables).
- **Risks:** none beyond getting the verb list exactly right (verify `Program.fs` dispatch arm, not
  help strings — `extract`/`profile` strings exist but confirm they dispatch).

#### 0.2 — Prune `AsyncStream` to its real surface
- **Wave/Effort/Status/Deps:** 0 / S / buildable-now / none
- **Goal:** Retire the 6 zero-consumer combinators (the two-consumer-threshold retraction the audit
  pre-authorized as item 28).
- **Files:** `src/Projection.Adapters.Sql/AsyncStream.fs` only.
- **Types & signatures:** delete `empty`, `ofList`, `map`, `mapAsync`, `iter`, `fold` (0 callers
  each, verified). **Keep** the `AsyncStream<'a>` type, `toList` (3 callers), `probe` (3 callers),
  `bufferUpTo` (1 caller), `batchesOf` (1 caller). Update the file-header LINT-ALLOW comment to the
  reduced surface.
- **Acceptance:** build green; full pure pool green via `scripts/test.sh fast`; no test referenced a
  pruned function (verified). `` ``AsyncStream surface is consumer-justified`` `` optional guard test
  asserting the module exposes only consumed combinators.
- **Governance:** `DECISIONS` gist — *"AsyncStream pruned to {type, toList, probe, bufferUpTo,
  batchesOf}; 6 zero-consumer combinators retired per two-consumer-threshold retraction. bufferOf/
  batchesOf retained at single-consumer (used, below extraction threshold)."*
- **Risks:** **Correction vs the audit's "8–9 unused":** the live count is **6 dead**; `bufferUpTo`
  and `batchesOf` each have exactly one caller (likely `Deploy.executeStream` chunking) — confirm
  before deleting; do **not** delete them.

#### 0.3 — Delete dead `TransformRegistry` stage-order helpers
- **Wave/Effort/Status/Deps:** 0 / S / buildable-now / none
- **Goal:** Remove `allInStageOrder`/`inStage`/`stageOrdinal` — written for an execution loop that
  never materialized (the load-bearing order is the `PassChainAdapter` chain, not `stageOrdinal`).
- **Files:** `src/Projection.Core/TransformRegistry.fs`; `tests/Projection.Tests/TransformRegistryTests.fs`.
- **Acceptance:** no unused public function remains in `TransformRegistry`; pure pool green; lint
  rule-count unchanged.
- **Governance:** `DECISIONS` gist — *"stageOrdinal helpers deleted; chain order is the load-bearing
  order. Two-consumer threshold."* **Conditional keep:** if §V E5 (registry-as-docs) or §5.5
  (`applied-transforms`) ends up grouping by stage, keep `inStage` only — decide at that slice, not now.
- **Risks:** trivial; subtractive.

#### 0.4 — `Catalog.create` triple-walk → single fold (perf D4+D5)
- **Wave/Effort/Status/Deps:** 0 / S / buildable-now / none
- **Goal:** Collapse the three passes (count, kind-key set, duplicates) into one `foldBack`; hoist
  per-kind `attrKeys` once. The only Core-pure, trivially-safe, fired-bench-label perf item.
- **Files:** `src/Projection.Core/Catalog.fs` (`Catalog.create` body, ~L1223/1236/1268).
- **Acceptance:** `Catalog.create` tests green (A39 invariants unchanged); bench label
  `ir.catalog.create` ≤ baseline with before/after captured per the bench-driven protocol
  (3-candidate/2-refuted/1-confirmed if any candidate underperforms).
- **Governance:** `DECISIONS` gist — *"Catalog.create single-fold; A39 invariants preserved; bench
  ir.catalog.create improved/flat."* Pair the before/after data with the entry.
- **Risks:** must preserve A39 duplicate-key validation order (first-occurrence order stable).

---

### Wave 1 — Restore verification integrity
*Make the canary actually verify what it claims; remove the false node from the triangle. Prereq for Wave 2's proofs.*

#### 1.1 — DACPAC round-trip equality predicate + A37 erasure axes
- **Wave/Effort/Status/Deps:** 1 / M / buildable-now / none
- **Goal:** Make L3-S2's claimed mechanism real: a `Catalog`-level DACPAC round-trip with a named
  erasure predicate, replacing the per-`Table` spot-checks and correcting the audit's phantom Bucket-A.
- **Files:** new `tests/Projection.Tests/DacpacRoundTripTests.fs`; `Catalog.equivalent` in
  `src/Projection.Core/Catalog.fs`; `equalModuloDacpacErasure` + `DacpacReadSide.toCatalog` in
  `src/Projection.Targets.SSDT/` (reuse the DacFx `GetObjects` enumerations the existing
  `DacpacEmitterTests` already walk); `tests/Projection.Tests/AxiomTests.fs`; `AXIOMS.md`;
  `AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md` (correct the ~:1200 row).
- **Types & signatures:**
  ```fsharp
  // Catalog.fs
  val equivalent : Catalog -> Catalog -> bool          // structural, SsKey-keyed
  // Targets.SSDT
  val equalModuloDacpacErasure : Catalog -> Catalog -> bool
  module DacpacReadSide = val toCatalog : TSqlModel -> Result<Catalog, EmitError>
  ```
- **Acceptance:** `` ``L3-S2: DACPAC round-trips modulo named erasure`` `` over `sampleCatalog` +
  `indexedCatalog`: `equalModuloDacpacErasure C (DacpacReadSide.toCatalog (load (DacpacEmitter.emit C)))`.
  Flip the `AxiomTests.fs` L3-S2 entry from absent/Skip → `[<Fact>]` citing this test.
- **Governance:** **A37 (named DACPAC erasure axes)** cashed from candidate → formal in `AXIOMS.md`:
  index auto-naming, `Origin.xml` wall-clock, constraint auto-naming, computed-column normalization.
  `DECISIONS` gist — *"L3-S2 underwritten by a real Catalog-level round-trip; A37 erasure set declared;
  audit bucket corrected from phantom-A to verified-A."* This is the canonical worked example for §V E1.
- **Risks:** the erasure set must be *closed and named* — an unnamed erasure is a silent tolerance.
  Keep `equivalent` (strict) and `equalModuloDacpacErasure` (erasure-aware) as **two** functions so the
  erasure is always explicit at the call site.

#### 1.2 — Un-hollow ReadSide, part 1: defaults + extended properties + descriptions
- **Wave/Effort/Status/Deps:** 1 / L / buildable-now / none
- **Goal:** Make `emit → deploy → ReadSide` round-trip DEFAULT constraints, extended properties, and
  `MS_Description` — so the canary can *catch a regression* that drops them (today it reads them back
  as empty and is blind).
- **Files:** `src/Projection.Adapters.Sql/ReadSide.fs` (the `DefaultValue=None`/`Description=None`/
  `ExtendedProperties=[]` sites at ~L360/379/382/675/685/1013 — replace with reads from
  `sys.default_constraints`, `sys.extended_properties`); thread the probes through the existing
  `EvidenceCache` discover-then-derive pattern (one discovery pass, pure derivations).
- **Types & signatures:** extend the ReadSide result rowsets with default/ext-prop/description maps
  keyed by `(schema,table[,column])`; populate `Attribute.DefaultValue/DefaultName`,
  `*.ExtendedProperties`, `Kind/Attribute.Description`.
- **Acceptance:** new `` ``L3-S6: DEFAULT round-trips via ReadSide`` `` and
  `` ``L3-S9: ExtendedProperties round-trip via ReadSide`` `` (sibling to `FkRealityRowsetRoundTripTests`):
  deploy a catalog carrying defaults + ext-props, assert `ReadSide.toCatalog` recovers them; extend
  `CanaryRoundTripTests` with a fixture carrying these. Flip the `AxiomTests.fs` L3-S6/S9 round-trip entries.
- **Governance:** L3-S6 / L3-S9 round-trip leg **D→A**; retire the `CommentMetadataUnreflected`
  Tolerance variant (named in `Catalog.fs` and `CHAPTER_A_0_PRIME_CLOSE.md`). `DECISIONS` gist —
  *"ReadSide reconstructs defaults + ext-props + descriptions; canary no longer blind to these axes."*
- **Risks:** Docker-gated (testcontainers) — runs in the `docker` pool, not pre-commit; keep the probes
  inside `EvidenceCache` so round-trip count stays bounded (per the B.3 discipline).

#### 1.3 — Un-hollow ReadSide, part 2: triggers + sequences + checks
- **Wave/Effort/Status/Deps:** 1 / L / buildable-now / 1.2 (same mechanism)
- **Goal:** Round-trip the remaining three SSDT-emitted-but-unobserved features.
- **Files:** `src/Projection.Adapters.Sql/ReadSide.fs` (the `Triggers=[]`/`Sequences=[]`/
  `ColumnChecks=[]` sites ~L683/684/1020 — read `sys.triggers` + `sys.sql_modules`, `sys.sequences`,
  `sys.check_constraints`).
- **Acceptance:** `` ``L3-S4: triggers round-trip via ReadSide`` ``, `` ``L3-S5: sequences round-trip`` ``,
  `` ``L3-S8: CHECK constraints round-trip`` ``; flip the three `AxiomTests.fs` entries.
- **Governance:** L3-S4/S5/S8 round-trip leg **D→A**. `DECISIONS` gist — *"ReadSide reconstructs
  triggers/sequences/checks; the six-feature hollow-canary class is closed."* This slice + 1.2 together
  satisfy **C1 (round-trip totality)** for the schema axes — the precondition for §V E3.
- **Risks:** trigger `Definition` text normalization (whitespace/casing) — compare modulo a named
  normalization, recorded as a Tolerance entry, not silently.

#### 1.4 — `writeBack`-correctness drift-guard
- **Wave/Effort/Status/Deps:** 1 / S / buildable-now / none
- **Goal:** Close the one residual registry-execution drift risk: a future `liftDecisionPass` with a
  mismatched `writeBack` setter compiles but writes to the wrong `ComposeState` field.
- **Files:** `tests/Projection.Tests/PassChainAdapterComposeTests.fs` (or `RegisteredTransformsTests.fs`).
- **Acceptance:** `` ``every decision-set pass populates its own distinct ComposeState field`` `` —
  run `allChainSteps` end-to-end on a representative catalog; assert each `ComposeState.with*` field is
  populated iff its producing pass fired, and no two passes write the same field. Extends the existing
  Kleisli-law suite.
- **Governance:** `DECISIONS` gist — *"writeBack-correctness as structural witness against chain-step
  registration drift."*
- **Risks:** none; additive test.

---

### Wave 2 — Close the core decision→emission loop
*The central evidence-gated-tightening promise: V2 currently decides what to tighten, then emits the untightened schema. Highest-leverage feature gap. Strict slice chain.*

> **The A18 question, resolved once for the whole wave.** Consuming decisions in an emitter is **not**
> an A18 violation. `Catalog`/`Profile` are evidence; `Policy` is intent; passes interpret evidence
> under intent to produce *decisions*. By the time a decision reaches the emitter the operator's intent
> has been **discharged into evidence** — a decision is a fact ("this attribute was decided NOT NULL
> under the registered intervention given the observed null count"). The structural guardrail: keep
> `Emitter<'element> = Catalog -> Result<…>` `Catalog`-only and pass `DecisionOverlay` as a **curried
> prefix argument**, never folded into the `Emitter` alias and never a `Policy` parameter. Precedent:
> `RemediationEmitter` already consumes all three decision sets and is correctly classified `DataIntent`.

#### 2.1 — `DecisionOverlay`: the evidence-keyed projection
- **Wave/Effort/Status/Deps:** 2 / S / buildable-now / none
- **Goal:** One typed VO collapsing the three `Option<DecisionSet>` in `ComposeState` into
  emitter-consumable `Set<SsKey>` lookups, with an `empty` identity. Pure plumbing; no output change.
- **Files:** new `src/Projection.Core/DecisionOverlay.fs` (after `ComposeState.fs` in the fsproj);
  `Projection.Core.fsproj`.
- **Types & signatures:**
  ```fsharp
  type DecisionOverlay =
      { EnforceNotNull : Set<SsKey>   // AttributeKey where NullabilityOutcome = EnforceNotNull _
        EnforceUnique  : Set<SsKey>   // IndexKey where UniqueIndexOutcome = EnforceUnique _
        DropFk         : Set<SsKey>   // ReferenceKey where ForeignKeyOutcome = DoNotEnforce _
        NoCheckFk      : Set<SsKey> } // ReferenceKey where evidence = ScriptWithNoCheck _
  module DecisionOverlay =
      val empty          : DecisionOverlay
      val ofComposeState : ComposeState -> DecisionOverlay   // None fields → empty contribution
  ```
- **Acceptance:** `tests/Projection.Tests/DecisionOverlayTests.fs`:
  `` ``A18: DecisionOverlay carries evidence-derived decisions, never Policy`` `` (type-witness: ctor
  takes `ComposeState`, no `Policy`); `` ``DecisionOverlay.empty is observable identity on empty policy`` ``
  (`ofComposeState (ComposeState.initial c) = empty`); FsCheck
  `` ``ofComposeState is total over decision-set membership`` ``.
- **Governance:** `DECISIONS` gist — *"DecisionOverlay is the emitter-consumable projection of the
  tightening decision sets; evidence (A18-safe), not Policy; empty preserves observable-identity-on-empty."*
  Scaffold **A42** candidate (decision→emission fidelity) in `AXIOMS.md` "Amendments scheduled."
- **Risks:** none; identity-preserving foundation.

#### 2.2 — Thread `DecisionOverlay` through the SSDT emitter (byte-identical)
- **Wave/Effort/Status/Deps:** 2 / M / buildable-now / 2.1
- **Goal:** Add `DecisionOverlay` as a curried prefix arg to `emitSlices`/`statements`/per-kind helpers,
  defaulting to `DecisionOverlay.empty` at every call site — output **byte-identical** to today (the T1
  safety net proving the seam is open without changing bytes).
- **Files:** `src/Projection.Targets.SSDT/SsdtDdlEmitter.fs` (`emitSlices`, `statements`,
  `kindToSsdtFile`, `createTableStatement`, `indexStatements`, `fkDef`/`untrustedFkAlters` signatures);
  `src/Projection.Pipeline/Pipeline.fs` (`project` passes `empty`; `projectFromChainWithState` passes
  `DecisionOverlay.ofComposeState composedState`); callers `src/Projection.Cli/Program.fs`
  (`Deploy.runWideCanary … statements`), `src/Projection.Targets.SSDT/DacpacEmitter.fs`.
- **Types & signatures:** `emitSlices : DecisionOverlay -> Emitter<SsdtFile>`;
  `statements : DecisionOverlay -> Catalog -> seq<Statement>`.
- **Acceptance:** all existing `SsdtDdlEmitterPropertyTests` (P1 byte-determinism) + golden files pass
  **unchanged** with `empty` threaded; add `` ``T1: emitSlices with empty overlay is byte-identical to
  pre-overlay emission`` ``.
- **Governance:** `DECISIONS` gist — *"SSDT emitter takes DecisionOverlay (empty=identity); curried-prefix
  shape keeps the Emitter port Catalog-only (A18-amended holds)."* Add a `TransformSite.dataIntent
  "applyTighteningDecisions"` so pillar-9 classification stays `DataIntent` (decision-as-evidence rationale).
- **Risks:** call-site fan-out; if any golden file shifts, the threading has a bug — use TRX-first capture.

#### 2.3 — Apply NOT NULL + UNIQUE tightening at emission (first behavior change)
- **Wave/Effort/Status/Deps:** 2 / M / buildable-now / 2.2, **1.2** (canary must observe nullability/uniqueness round-trip to *prove* it landed)
- **Goal:** In `columnDef`, set `Nullable = a.Column.IsNullable && not (Set.contains a.SsKey overlay.EnforceNotNull)`;
  in `indexStatements`, `IsUnique = idx.IsUnique || Set.contains idx.SsKey overlay.EnforceUnique`. **Additive-only**
  — a non-enforce decision leaves source truth untouched.
- **Files:** `src/Projection.Targets.SSDT/SsdtDdlEmitter.fs` (`columnDef`, `indexStatements`).
- **Acceptance — the canary proves it:** `` ``A42: emitted DDL reflects EnforceNotNull tightening decision`` ``
  — build a catalog where a column is source-NULL but a registered Nullability intervention enforces;
  emit → deploy → read-back → assert `PhysicalSchema` shows `NOT NULL`. FsCheck
  `` ``A42: every EnforceNotNull decision NOT-NULLs the emitted column, and only those`` `` (the converse
  pins additive-only). Mirror for `EnforceUnique`.
- **Governance:** `DECISIONS` gist — *"Nullability + UniqueIndex tightening decisions reach emitted DDL;
  additive-only (never loosens source truth)."*
- **Risks:** the `emittedCatalog` (platform-auto-index-filtered) vs `composedState.Catalog` split — a
  decision keyed to a filtered-out index is a harmless **named** no-op; add a defensive test. Encode as
  `field && not enforce` / `field || enforce`, never `field = decision` (prevents a future DoNotEnforce
  un-tightening a source `NOT NULL`).

#### 2.4 — Apply FK decision gating + NOCHECK at emission
- **Wave/Effort/Status/Deps:** 2 / M / buildable-now / 2.3
- **Goal:** Suppress an inline FK when `r.SsKey ∈ overlay.DropFk`; route `r.SsKey ∈ overlay.NoCheckFk`
  through the existing `AlterTableNoCheckConstraint` path.
- **Files:** `src/Projection.Targets.SSDT/SsdtDdlEmitter.fs` (`fkDef` filtering, `untrustedFkAlters`).
- **Acceptance:** `` ``A42: DoNotEnforce FK decision suppresses the inline constraint`` `` (read-back: no FK);
  `` ``A42: ScriptWithNoCheck FK decision emits WITH NOCHECK`` `` (read-back: constraint exists, untrusted).
- **Governance:** `DECISIONS` gist — *"FK creation decisions reach emission; DoNotEnforce drops,
  ScriptWithNoCheck emits untrusted. Third tightening axis closed."*
- **Risks:** order vs the cross-DB-FK work (4.3) — keep the overlay filter orthogonal to the three-part-name logic.

#### 2.5 — Cash A42 + close the FK silent-drop witness
- **Wave/Effort/Status/Deps:** 2 / M / buildable-now / 2.3, 2.4
- **Goal:** (a) Promote decision→emission fidelity to numbered **A42** in `AXIOMS.md` + `AxiomTests.fs`
  + a new `PRODUCT_AXIOMS.md` L3 entry. (b) When `fkDef` returns `None` for an *unresolved target* (not
  an overlay decision), emit a `Diagnostic` witness (retires the slice-μ deferral).
- **Files:** `AXIOMS.md`; `tests/Projection.Tests/AxiomTests.fs`; `PRODUCT_AXIOMS.md`;
  `src/Projection.Targets.SSDT/SsdtDdlEmitter.fs` (route the unresolved-target drop through a sibling
  `Diagnostics` output — the witness rides the dual-writer, not a `Policy` param).
- **Acceptance:** `AxiomTests.fs` A42 entry green; `` ``L3-X7: unresolved-target FK drop emits a Diagnostics
  witness`` `` (Code `emitter:ssdtDdlEmitter.foreignKey.unresolvedTargetDropped`); chapter-close AXIOMS↔AxiomTests
  bucket alignment holds.
- **Governance:** **A42** body cashed; **L3-Boundary-NoSilentDrop** promoted for the FK case; `DECISIONS`
  gist — *"A42: emitted DDL is a faithful projection of the tightening decision sets; FK-resolution-failure
  drops carry an L3-X7 witness; slice-μ retired."* This slice is the schema-axis half of §V E3.
- **Risks:** decide the witness channel in the DECISIONS entry (sibling `Diagnostics` output vs routing
  through `ComposeState`/manifest) — prefer the dual-writer to keep the `Emitter` port pure.

---

### Wave 3 — Cutover critical path
*The single open T-30 condition is the UAT dry-run; this wave makes it runnable and legitimate.*

#### 3.1 — R6 execute-path amendment + CDC pre-flight
- **Wave/Effort/Status/Deps:** 3 / M / buildable-now (amendment needs operator sign-off, OPEN-4) / none
- **Goal:** Author the **missing formal R6 authorization** for the Transfer `--execute` write path (only
  an env-var gate `PROJECTION_ALLOW_EXECUTE=1` / exit-7 exists today — no superseding DECISIONS entry), and
  add a CDC pre-flight that refuses execute against a CDC-tracked sink.
- **Files:** `DECISIONS.md` (the R6 amendment — scope = UAT-preview-only, dry-run default, CDC precondition,
  per-run sign-off; supersedes the implicit "scoped around R6" prose); `src/Projection.Pipeline/TransferRun.fs`
  (CDC pre-flight: query `sys.tables.is_tracked_by_cdc` on the sink; in `Execute` mode refuse unless
  `--allow-cdc`); `AXIOMS.md` (scaffold the **data-level adjunction axiom candidate**: for
  `PreservedFromSource` against a blank sink, `Ingestion(Projection(rows)) = rows` on the row-digest axis).
- **Acceptance:** `` ``CDC pre-flight refuses --execute against a CDC-tracked sink`` `` (ephemeral DB with a
  CDC table); `AxiomTests.fs` data-level-adjunction entry (Bucket A, citing the existing `TransferCanaryTests`
  data canary).
- **Governance:** **the R6 amendment is the load-bearing deliverable** — R6 is non-negotiable without it.
  `DECISIONS` gist — *"R6 amended: Transfer --execute authorized for UAT-preview only, dry-run default,
  CDC-precondition, per-run operator sign-off; production write path remains V1's."*
- **Risks:** depends on OPEN-4 (operator confirms the UAT-preview framing) — organizational but cheap. The
  CDC check is defensive code buildable now regardless.

#### 3.2 — Close the R6 approval loop (persist `ApprovalRegistry`)
- **Wave/Effort/Status/Deps:** 3 / M / buildable-now / none
- **Goal:** Give the `approve` CLI verb a durable home so operator sign-off is *recorded and consultable*
  — R6 *requires* sign-off as a flip gate, but `approve` currently constructs a record and discards it.
- **Files:** new boundary module `src/Projection.Pipeline/ApprovalStore.fs` (JSON via `Utf8JsonWriter`/
  `JsonNode`, Tier-3 typed-AST — **not Core**, PRJ001); `src/Projection.Cli/Program.fs` (`approve` loads,
  `ApprovalRegistry.record`, persists); then gate `src/Projection.Targets.OperationalDiagnostics/
  SuggestConfigEmitter.fs` on `ApprovalRegistry.isSuppressed` (default `empty`, sibling-wrapper idiom).
- **Types & signatures:**
  ```fsharp
  module ApprovalStore =
      val load : path:string -> Result<ApprovalRegistry, ApprovalError>
      val save : path:string -> ApprovalRegistry -> Result<unit, ApprovalError>
  // SuggestConfigEmitter
  val emitWith : ApprovalRegistry -> (existing args) -> ...   // emit = emitWith ApprovalRegistry.empty
  ```
- **Acceptance:** `` ``approval round-trips through the JSON store`` `` (record→save→load→tryFind);
  `` ``rejected digest suppresses its suggested-config hint; approved/unknown does not`` ``; default-empty
  path byte-identical to today (T1). `Projection.Core` stays I/O-free (audit clean).
- **Governance:** `DECISIONS` gist — *"Approval loop closed; R6 operator-sign-off has a structural home;
  SuggestConfig consults approvals (second consumer confirms the ApprovalRegistry primitive)."* Pillar 9:
  the store is `CutoverSafety` cross-cutting, not a transform site.
- **Risks:** keep all I/O in Pipeline/CLI; the `ApprovalRegistry` algebra in Core is already pure.

#### 3.3 — User-FK reflow as opt-in behavior of `full-export` + `transfer` (NOT a standalone verb) — DONE
- **Wave/Effort/Status/Deps:** 3 / S / **LANDED 2026-05-30** / none
- **Decision (operator directive; supersedes the original standalone-verb plan).** There is no
  `osm uat-users` verb, no `UatUsersArgs.fs`, no `InventoryCsvReader.fs`, no sample CSV. The Dev→UAT
  user-FK reflow is a *configurable, opt-in behavior* of the two verbs that already own its inputs.
  See `DECISIONS 2026-05-30 — uat-users is NOT a standalone verb`.
- **`transfer` — the live re-key (canonical).** `transfer --reconcile <UserTable>:<emailColumn>`
  reconciles Source users to the *live* Sink (UAT) by the match column. The Sink connection IS the
  target-user inventory, so the planned CSV reader is **obviated and dropped** (live-only).
- **`full-export` — config-driven reflow, OPT-IN.** `UserFkReflowPass` is in the chain but now
  opt-in (off by default): `policy.transformGroups: [{ name: "UserReflow", enabled: true }]` enables it;
  absent ⇒ `userFkReflowPass` excluded from the chain (`TransformGroupsBinding.fromConfig` injects
  `UserReflow = false`). `policy.userMatching` configures the strategy when opted in.
- **Acceptance (landed):** `TransformGroupsBindingTests` — UserReflow defaults OFF (opt-in); enabled only
  when the operator names it; the existing `--reconcile` path covers the transfer re-key. Full pure pool
  green (the pass was a no-op-by-default, so excluding it changed nothing except the toggle semantics).
- **Risks retired:** the ~400-1500 LOC estimate vanishes — the machinery was already in both verbs; the
  delta was a one-line opt-in default flip + test updates + this decision. The UAT *dry-run* itself stays
  access-gated (Wave 5 §5.x), but no engineering remains to make it runnable.

#### 3.4 — Tolerance per-environment config, fail-closed
- **Wave/Effort/Status/Deps:** 3 / S-M / buildable-now / none
- **Goal:** Make the (already-complete) `Tolerance` taxonomy operator-configurable per environment, so
  "is this divergence acceptable?" is a config decision, not a recompile — and **unknown names fail closed.**
- **Files:** `src/Projection.Core/Tolerance.fs` (add `parse`); the Pipeline config type (add `Tolerances :
  string list`, D9-clean); the canary harness (consume the env's `Tolerance` instead of a hardcoded one).
- **Types & signatures:** `val parse : string list -> Result<Tolerance, ToleranceError>` — every name
  validated against `ToleratedDivergence.allKnown`; **unknown ⇒ `Error`** (never silently widen).
- **Acceptance:** `` ``every ToleratedDivergence name round-trips through parse`` ``; `` ``unknown tolerance
  name fails closed`` ``; `` ``DEV config tolerating HeaderCommentsOmitted passes; PROD strict fails the same
  divergence`` ``.
- **Governance:** `DECISIONS` gist — *"Per-environment Tolerance config; parse fails closed on unknown
  divergence (safety property)."* This is the operator decision surface R6's flip gate reads.
- **Risks:** fail-closed is load-bearing — an unrecognized name silently widening tolerance would corrupt
  the canary's gate semantics.

#### 3.5 — R6 flip operationalization (streak counter + ledger + runbook)
- **Wave/Effort/Status/Deps:** 3 / S-M (counter) + ops-owned (ledger/runbook) / buildable-now / 3.2 (sign-off mechanism)
- **Goal:** Make R6's "N=10 consecutive green canary + operator sign-off, per (env × artifact-type)" a
  machine-checkable artifact instead of documented-but-unoperationalized prose.
- **Files:** new `cutover/canary-streak.json` (per-pair `{env, artifactType, consecutiveGreen,
  lastRunDigest}`) incremented by an extension to `scripts/perf-gate.sh` (or `scripts/canary-streak.sh`);
  new `cutover/FLIP_LEDGER.md` (append-only `{env, artifactType, flippedAt, approver, toleranceDigest,
  reversibleUntil:cutover+30}`); new `cutover/RUNBOOK.md` (pre-flight → flip → rollback). **Sign-off reuses
  the `approve` verb + `ApprovalWorkflow` (3.2)** rather than a parallel mechanism.
- **Acceptance:** `` ``canary streak resets to 0 on any red and increments on green`` ``; a flip-ledger row
  is producible only with a recorded `approve` for the pair.
- **Governance:** `DECISIONS` gist — *"R6 flip operationalized: streak counter + append-only flip ledger +
  runbook; sign-off is a typed `approve` invocation keyed to (env × artifact-type)."*
- **Risks:** the counter is engineering-owned; the ledger/runbook are cutover-ops-owned (engineering seeds).

---

### Wave 4 — Bidirectional frontier + capability
*Buildable-now capability extension; none gated on OPEN-2.*

#### 4.1 — Transfer Slice A: `V2.SsKey` persistence — ✅ DONE (2026-05-30, verified)
- **Wave/Effort/Status/Deps:** 4 / M / **LANDED — commits `98fa616` (codec+tests) / `ea25b2a` (emit) / `aa7aa9a`
  (ReadSide hydrate) / `8dbcdfd` (red-branch repair)** / none (prereq for §5.2 Slice E)
- **Shipped:** `SsKey.serialize` / `SsKey.deserialize` (tag-prefixed, length-prefixed fields; total over all four
  variants; round-trips through 8 example tests in `SsKeyTests.fs`). `SsdtDdlEmitter` emits a `V2.SsKey` extended
  property at table level (sibling to `V2.LogicalName`). `ReadSide.buildKind` tries `V2.SsKey` → `deserialize` →
  falls back to `kindSsKey` synthesis on absent/malformed. **Verified end-to-end:** the Docker-gated round-trip
  `` ``4.1: V2.SsKey persistence — ReadSide recovers OssysOriginal identities (A1 across deploy->read)`` `` passes
  (deploy an `OssysOriginal`-keyed catalog → read back → recovered key equals the original GUID, not a synthesis).
- **Cautionary note for the next agent:** the part-2b commit (`aa7aa9a`) shipped **red** — a 6-wide return-type
  annotation against a 7-tuple body, plus an acceptance test referencing three non-existent helpers
  (`CanaryTestGuard.runWhenEnabled` / `CanaryHarness.deployAndReadback` / `sampleSourceCatalog`). `8dbcdfd` repaired
  both (widen annotation; rewrite the test against the real `skipIfNoDocker` + `Deploy.runWithReadback` surface).
  Lesson: **run `dotnet build Projection.sln` before every commit** — a commit that builds only its own project can
  still break the solution.
- **Goal (orig):** Persist rename-stable identity (`OssysOriginal` GUID, A1) into the frozen schema so a *later-run*
  Transfer recovers it from disk instead of `ReadSide` synthesizing `Synthesized("READSIDE_KIND",…)` from
  physical names (~`ReadSide.fs:68`).
- **Files:** `src/Projection.Targets.SSDT/SsdtDdlEmitter.fs` (emit a `V2.SsKey` extended property sibling to
  `V2.LogicalName` via `Statement.SetExtendedProperty`); `src/Projection.Core/Identity.fs` (`SsKey.serialize`/
  `deserialize` — total over all four variants); `src/Projection.Adapters.Sql/ReadSide.fs` (read the
  `V2.SsKey` batch, hydrate `SsKey` with fallback to synthesis — same pattern as the `V2.LogicalName` fallback).
- **Types & signatures:**
  ```fsharp
  module SsKey =
      val serialize   : SsKey -> string
      val deserialize : string -> Result<SsKey, IdentityError>   // round-trips all 4 variants
  ```
- **Acceptance:** `` ``A1: Catalog → SSDT artifact → ReadSide reload preserves SsKey and the FK graph`` `` —
  for `OssysOriginal`-keyed catalogs, reload yields the original key (not a READSIDE synthesis) and the
  `Reference.TargetKind` set is preserved.
- **Governance:** strengthens **H-010** (Catalog↔DDL Prism carries *identity* across a process boundary).
  `DECISIONS` gist — *"V2.SsKey persisted; later-run Transfer + AssignedBySink recover identity from disk."*
  Pillar 9: `V2.SsKey` emission is `DataIntent`.
- **Risks:** keep the physical-coordinate-keyed index (`Map<TableId,Kind>`) deferred to its first consumer
  (Ingestion, §4.2) per IR-grows-under-evidence.

#### 4.2 — Transfer C′-wire: connection apparatus + CSV loader (lands LiveOssysConnection)
- **Wave/Effort/Status/Deps:** 4 / M / **DONE (2026-05-31)** / none
- **Shipped:** `ConnectionResolver.openSubstrate : Substrate -> Task<Result<SqlConnection>>`;
  `Transfer.runThroughConnections` (opens both substrates via the apparatus, reads the contract from
  the Source, resolves reconciliation against it, runs — Source open + Sink read are the
  `ProfiledForIdentity` reads; no new per-table probes); `TransferSpec.parseUserMapCsv` /
  `resolveUserMap` (per-table `ManualOverride`) / `resolveAllReconciliation` (merge MatchByColumn +
  ManualOverride; reject a kind named by both); CLI `--user-map` + `--source-env`/`--sink-env`,
  `runTransfer` rewired to drive through `runThroughConnections`. Acceptance green: the reconcile
  canary driven through `TransferConnections` (D9 file-ref substrates) with a ManualOverride CSV
  round-trip (`TransferCanaryTests` `4.2:`); `TransferSpec` pure tests 24/0; role-mismatch +
  `ProfiledForIdentity` in `ReconciliationTests`.
- **Goal:** Route the orchestrator + CLI through the reified-but-dormant `TransferConnections` apparatus,
  enabling concurrent dual-environment profiling — **the single slice that collapses three deferrals
  (LiveOssysConnection + multi-environment config + UAT-users) and closes Phase B's functional-equivalence arm.**
- **Files:** `src/Projection.Pipeline/TransferRun.fs` (`runCore` accepts a `TransferConnections`-derived
  pair; profiling reads run concurrently with source ingest); `src/Projection.Cli/Program.fs` +
  `TransferArgs.fs` (`--environment`/named-substrate resolution; `--user-map` CSV for `ManualOverride`);
  `src/Projection.Adapters.Sql/ConnectionResolver.fs` (`openSubstrate`).
- **Types & signatures:** `val openSubstrate : Substrate -> Task<Result<SqlConnection, ConnError>>`
  (`TransferConnections.create` already exists).
- **Acceptance:** the reconcile canary stays green driven through `TransferConnections` (`ProfiledForIdentity
  = [source; sink]`); `` ``TransferConnections rejects role mismatch`` ``; a `ManualOverride` CSV round-trips
  into the reconcile path.
- **Governance:** D9 (endpoints via `ConnectionRef`, never `Config`). `DECISIONS` gist — *"C′ apparatus wired;
  LiveOssysConnection + multi-env + UAT-users deferrals subsumed; Phase B functional-equivalence arm closed."*
  Retire the subsumed Active-deferral rows.
- **Risks:** this is the convergence keystone — sequencing it before §5.x live work means the LiveOssys path
  is exercised against ephemeral/canary DBs first.

#### 4.3 — Cross-DB FK emission (three-part name or structured error)
- **Wave/Effort/Status/Deps:** 4 / M / **DONE (2026-05-31)** / none
- **Shipped:** `ScriptDomBuild.schemaObjectFromTableId` pushes a third leading `Identifier` when
  `TableId.Catalog = Some db` (three-part `[db].[schema].[table]`); `SsdtDdlEmitter.toTableId`
  carries `k.Physical.Catalog` instead of hard-coding `None`. Both additive (a `Catalog = None`
  TableId emits the byte-identical two-part name). The truly-external case (target absent from the
  catalog) still drops via `foreignKeyDropDiagnostics` — neither cross-DB path is a silent drop.
  Tests in `SsdtDdlEmitterTests` (three-part REFERENCES + CREATE TABLE; two-part additive guard).
- **Goal:** The only IR-present-but-unemitted feature: emit `[catalog].[schema].[table]` when
  `TableId.Catalog = Some db`; `schemaObjectFromTableId` currently drops `.Catalog`.
- **Files:** `src/Projection.Targets.SSDT/ScriptDomBuild.fs` (`schemaObjectFromTableId` — push a third
  `Identifier` when `Catalog = Some`); `src/Projection.Targets.SSDT/SsdtDdlEmitter.fs` (the cross-catalog FK
  path that today silently drops — replace with honored reference or structured error, per L3-S10).
- **Acceptance:** `` ``L3-S10: cross-DB FK emits a three-part name or a structured error`` `` (no silent
  downgrade); flip the `AxiomTests.fs` L3-S10/L3-I10 entries.
- **Governance:** L3-S10/L3-I10 **D→A**; ties to **L3-Boundary-NoSilentDrop**. `DECISIONS` gist —
  *"Cross-DB FK honored or rejected structurally; the cross-catalog silent-drop is closed."* Flips parity row 47.
- **Risks:** keep orthogonal to the overlay FK filter (2.4).

#### 4.4 — `osm verify-data` verb (post-deploy integrity gate)
- **Wave/Effort/Status/Deps:** 4 / M / **DONE (2026-05-31)** / overlap checked — none
- **Shipped:** Confirmed `Reconciliation.fs` does NOT overlap (identity-remap only). New
  `Projection.Adapters.Sql/DataIntegrityChecker.fs`: `IntegrityReport` (RowCountDeltas /
  NullCountDeltas / Warnings) + pure `diff` + `compare` (captures both deployments via
  `LiveProfiler.captureEvidenceCache` — exact RowCount + per-attribute NullCounts — and diffs in
  pure F#; clears ReadSide's `Static` modality so every table, including lookups, is profiled). CLI
  `verify-data` verb (`VerifyDataArgs.fs` + `runVerifyData`; contract read from the before
  deployment; fails closed at exit code 8). Tests: 5 pure-pool `diff`/`isClean` cases +
  3 Docker-gated (`verify-data flags exactly the mutated row-count delta`, clean identical pair,
  per-column null-count delta).
- **Goal:** Post-deploy per-table row-count + per-column null-count diff (the data-fidelity complement to the
  canary's structural equivalence).
- **Files:** `src/Projection.Cli/Program.fs` (`runVerifyData`); new `src/Projection.Adapters.Sql/
  DataIntegrityChecker.fs` (reuses the shipped `LiveProfiler` probes); new `src/Projection.Cli/VerifyDataArgs.fs`.
- **Types & signatures:**
  ```fsharp
  module DataIntegrityChecker =
      val compare : SqlConnection -> SqlConnection -> Catalog -> Task<Result<IntegrityReport, _>>
  type IntegrityReport = { RowCountDeltas: …; NullCountDeltas: …; Warnings: … }
  ```
- **Acceptance:** Docker-gated `` ``verify-data flags exactly the mutated row-count delta`` `` (`scripts/test.sh
  docker`).
- **Governance:** `DECISIONS` gist — *"osm verify-data post-deploy gate; LiveProfiler dependency satisfied."*
  Matrix row 114 → 🟢.
- **Risks:** **the Transfer `Reconciliation.fs` may already subsume part of this** — verify overlap before
  building; reuse rather than duplicate.

#### 4.5 — DacpacEmitter joins the T11 sibling-Π contract
- **Wave/Effort/Status/Deps:** 4 / S / **DONE (2026-05-31)** / 1.1
- **Shipped:** `SiblingEmitterContractTests` `dacpacKeyset` helper + two T11 tests (DACPAC table-set
  recovers `Catalog.allKinds` SsKey keyset; SSDT and DACPAC siblings agree). The DACPAC sibling is
  `Result<byte[]>` (not an `ArtifactByKind`), so its keyset agreement is VERIFIED at the
  binary-normal-form tier (emit → `TSqlModel.LoadFromDacpac` → recover each table's SsKey via the
  Catalog's physical-coordinate bijection) rather than structural. `AxiomTests` T11 cites it +
  notes the verified-vs-structural distinction. Pure pool (DacFx in-memory; no Docker).
- **Goal:** Bring the DACPAC sibling under structural sibling-agreement: its keyset agrees with
  `SsdtDdlEmitter`'s by SsKey (today T11 covers only SSDT + Json).
- **Files:** `tests/Projection.Tests/SiblingEmitterContractTests.fs` (+ `ArtifactByKindTests.fs`); possibly a
  per-Kind keyset projection from the DacFx model.
- **Acceptance:** `` ``T11: SSDT and DACPAC siblings agree on the SsKey keyset`` ``; confirm/cite in `AxiomTests.fs`.
- **Governance:** L3-S3 sibling-agreement now covers DACPAC; cite the shipped T1-binary-normal-form amendment.
- **Risks:** none material.

#### 4.6 — `Origin` variant rename (decouple V1 vocabulary from Core)
- **Wave/Effort/Status/Deps:** 4 / S / **DONE (2026-05-31)** / none
- **Shipped:** `OsNative → Native`, `ExternalViaIntegrationStudio → ExternalIndirect`
  (`ExternalDirect` unchanged) across the Origin DU + `toStructured`/`originString` rendered strings
  (V2-growth — V1 emits `isExternal`, not an `origin` field; ReadSide never parses it back, so the
  regold is V2-only). Closed-DU exhaustiveness fired only at the match sites (ManifestEmitter,
  JsonEmitter); construction/default/doc sites updated across Core + adapters + ~30 test files.
  Historical `OsNativeSystem` doc token preserved. Does NOT fix item-17 (`ExternalDirect` still
  unreachable from the JSON-snapshot path) — noted, not regressed.
- **Goal:** Replace V1-product-name DU variants with algebraic ones: `OsNative → Native`,
  `ExternalViaIntegrationStudio → ExternalIndirect` (`ExternalDirect` keeps its name). Separable from the
  expensive SsKey ripple.
- **Files:** `src/Projection.Core/Catalog.fs` (`Origin` def + `display` + default); the 5 match sites —
  `Lineage.fs`, `CatalogReader.fs` (construction), `ManifestEmitter.fs`, `JsonEmitter.fs`, `ReadSide.fs`;
  ~30 test/fixture files (regold Origin-string assertions).
- **Acceptance:** all match sites exhaustive-checked green (closed-DU discipline: errors fire only at the 5
  match sites); canary + differential fixtures regolded; no new `Skip`.
- **Governance:** `DECISIONS` gist — *"Origin variants renamed to algebraic names; V1 vocabulary removed from
  the Origin DU. SsKey/`Projection.Identity` rename remains deferred (DACPAC-reader trigger)."*
- **Risks:** wide *test* surface (Origin is asserted across CDC/differential/parity suites) but contained
  *source* blast radius. Does **not** fix item-17 (`ExternalDirect` unreachable from production) — note, don't regress.

---

### Wave 5 — Blocked / defer-with-trigger
*Specs kept lighter (first slice + trigger). Build only when the named trigger fires.*

#### 5.1 — Transfer D-exec (real UAT load)
- **Status:** **blocked: OPEN-2** (does OutSystems Cloud UAT expose a writable SQL connection to entity tables,
  or is it platform-API-only?). Secondary: OPEN-1 (blank vs pre-existing), OPEN-6 (CHECK/trigger collisions).
- **First action:** an **ops spike** — attempt a `Microsoft.Data.SqlClient` connection to a throwaway UAT entity
  table and test `IDENTITY_INSERT`/INSERT. If forbidden, the whole D-exec/E-real-UAT path re-architects around
  the platform API and the dry-run/preview remains the deliverable.
- **Buildable now:** add `--preview-row-cap` to `TransferArgs.fs` so the first real run is bounded. The execute
  itself needs 3.1 (R6 amendment) + the OPEN-2 resolution.

#### 5.2 — Transfer Slice E: `AssignedBySink` (sink-minted keys) — **SHIPPED 2026-05-31 (acyclic)**
- **Status:** **shipped** — operator-as-consumer trigger fired (`DECISIONS 2026-05-31 — §5.2 AssignedBySink`).
  `TransferRun.writePlan` now branches on `IdentityDisposition`: `AssignedBySink` kinds insert per-row via
  `INSERT … OUTPUT inserted.<pk>` (omitting the IDENTITY column so the Sink mints the surrogate), capture each
  Source→assigned key into a `SurrogateRemapContext` threaded through the topological Phase-1 loop, and every later
  referencer's FK targeting the kind is re-pointed via `SurrogateRemap.remapRowFks` (skip-and-diagnose surfaced in
  `report.SkippedReferences`). New `assignedKeyCapture` site registers as `OperatorIntent Insertion` (the remap is
  discovered *during* the write, unlike `DataLoadPlan.build`'s pre-supplied substitution). `PreservedFromSource` /
  `ReconciledByRule` paths are byte-identical (the re-point no-ops when no `AssignedBySink` kind is in scope) — all
  prior transfer canaries stay green.
- **Acceptance (canary, ephemeral DB):** `` ``data adjunction: AssignedBySink round-trips modulo
  SurrogateRemapContext`` `` — green against the live container (User IDENTITY PK seeded at 280/281; Sink mints
  1/2; Order FKs re-pointed; the (Order→User-by-email) relationship is identical modulo the surrogate remap).
- **Deferred follow-on (cyclic AssignedBySink):** a *self-referential* IDENTITY kind (Phase-2 deferred FK) is out
  of scope — Phase-2's `WHERE <pk> = <sourceVal>` keys on the source PK, which no longer exists in the Sink once
  minted. Trigger: a real cyclic sink-minted fixture. The acyclic headline (parent IDENTITY + child FK) is the
  shipped worked example.

#### 5.3 — Lifecycle axis (the fourth input) — **SHIPPED 2026-05-31 (L-α→L-δ)**
- **Status:** **shipped** — operator-as-consumer trigger fired (`DECISIONS 2026-05-31 — §5.3 Lifecycle axis
  operationalized`). `src/Projection.Core/Lifecycle.fs` ships `Version`/`Timeline` VOs, the monotone `Lifecycle`
  chain + `append`, `evolutionChain` (fold `CatalogDiff.between`), and `replayTo`. The §V E4 acceptance is green:
  a 2-version `evolutionChain` drives `RefactorLogEmitter` to a correct `sp_rename` (`LifecycleTests.fs`).
  A-Lifecycle-1/2/3 are Bucket A in `AxiomTests.fs`; **A-Lifecycle-4 (evolutionChain composition associativity)
  stays Bucket C** pending a `CatalogDiff` compose operator (H-007), which also lands `replayTo`'s diff-replay
  reconstruction form. The original defer rationale (no temporal-chain consumer) is preserved below for history.
- **Prior status (now superseded):** *defer — no production consumer today* (`CatalogDiff.between` is consumed only in the
  single-round-trip sense by `RefactorLogEmitter` + tests; no path composes a temporal chain).
- **Trigger:** the first time refactor-log emission needs a **stored prior deployed catalog** as the diff
  baseline, OR the cutover+30 schema-evolution-cycle sunset gate needs a stored C₀ to replay against.
- **The type (build when triggered):**
  ```fsharp
  // src/Projection.Core/Lifecycle.fs  (Core, pure — Version is an ordinal, not a clock; PRJ001)
  type Version  = private Version of ordinal:int * label:string   // create : int -> string -> Result<Version>
  type Timeline = private Timeline of string                       // "dev" | "uat" | …
  type CatalogSnapshot = { Version: Version; Catalog: Catalog }
  type Lifecycle = private Lifecycle of { Timeline: Timeline; Snapshots: CatalogSnapshot list } // head = C₀
  module Lifecycle =
      val genesis        : Timeline -> CatalogSnapshot -> Lifecycle
      val append         : CatalogSnapshot -> Lifecycle -> Result<Lifecycle>   // enforces L3-L2 monotonicity
      val evolutionChain : Lifecycle -> Result<CatalogDiff list>               // fold CatalogDiff.between — the diff∘diff law
      val replayTo       : Version -> Lifecycle -> Result<Catalog>             // L3-L1 (materialized form first)
  ```
- **Plug-in point:** Lifecycle is an **outer envelope** over `Project`, not a fifth `ProjectionInput` field —
  `Project(Catalog,Policy,Profile)` stays the inner kernel (A17 untouched); Lifecycle maps over a *chain* of
  `Project` invocations and feeds the per-edge `CatalogDiff` to the existing `RefactorLogEmitter`.
- **Thin vertical (L-α → L-δ):** α `Version`/`Timeline` VOs (S); β `Lifecycle` chain + monotonic `append` (M);
  γ `evolutionChain` = fold `CatalogDiff.between` (M); δ `replayTo` + first real consumer wiring (M/L — the
  slice that must wait for the trigger).
- **Axioms to scaffold now (cheap, as `Skip` stubs in `AxiomTests.fs`):** A-Lifecycle-1↔L3-L1 (replayability),
  -2↔L3-L2 (monotonic history), -3↔L3-L3 (per-timeline independence), -4 (evolutionChain associativity — the
  formal underwriting of "RefactorLog composition").
- **This is §V E4** — operationalizing it is the precondition for the policy-intelligence consumers (§5.6).

#### 5.4 — `SsKey` rename + `Projection.Identity` bounded context — **defer**
- **Status:** defer. **Trigger:** DACPAC-reader design (when "what is identity-origin for a non-OSSYS source"
  becomes a real question with a consumer). Also requires superseding the `OS_KIND_*` rendering commitment that
  `EndToEndPipelineTests` depends on.
- **Why deferred:** `V1Mapped` is the cross-version-identity carrier for User-FK reflow; the *rename* is cheap
  (match sites only) but the *semantic redesign* (identity-origin for DACPAC) ripples into the reader design and
  the `v2Namespace` derivation — speculative without the consumer. Effort: **L**.

#### 5.5 — `applied-transforms` per-artifact manifest field — **SHIPPED 2026-05-31**
- **Status:** **shipped** — operator-as-consumer trigger fired (`DECISIONS 2026-05-31 — §5.5 applied-transforms`).
  `ManifestEmitter.appliedTransforms : LineageEvent list -> (SsKey × OverlayAxis option) list` derives the field
  from `composed.Trail`; `Manifest.AppliedTransforms` carries it; `buildFull` threads `composed.Trail` from the
  pipeline; `toNode` serializes `{ssKey, overlay}` (overlay = OverlayAxis case name, or JSON `null` for skeleton-
  only). Per-artifact semantics: `DataIntent`-only → one `None` row (skeleton-purity witness); `OperatorIntent
  axis` → one `Some axis` row per distinct axis (overlay-exercise witness; the `None` collapses when an overlay
  also touched the artifact); sorted by `(SsKey, OverlayAxis option)` for T1. Cashed the PRIME slice-ζ forward
  signal and the CLAUDE.md load-bearing-commitment row "manifest names every applied overlay per artifact."
- **Follow-on still open:** the 5th bidirectional property test (manifest-digest + applied-transforms round-trip
  through ReadSide) — the field now exists, so the test is unblocked; it remains its own slice (needs manifest
  read-back). Per the original `DECISIONS 2026-05-11 slice θ` deferral.
- **Original first-slice spec (now realized):** in `ManifestEmitter.fs`, derive `applied-transforms : (SsKey ×
  OverlayAxis option) list` from `composed.Trail`; `DataIntent → None`, `OperatorIntent axis → Some axis`; sort by `SsKey` (T1).

#### 5.6 — Policy-intelligence consumers — **leg 1 SHIPPED 2026-05-31; legs 2/3 held (distinct triggers)**
- **`policy-diff A B` CLI verb — SHIPPED** (`DECISIONS 2026-05-31 — §5.6 policy-diff`). `projection policy-diff
  <config-a> <config-b>` reads the shared Catalog from config-a's `Model.Path`, binds a `Policy` from each config
  via `Compose.buildPolicyFromConfig` (made public — the legitimate **second consumer** of the binder), and runs
  `PolicyDiff.diffFullProjection` against `Profile.empty`. Renders the five-axis structural delta + the
  changed-kind set. Orchestrator `PolicyDiff.diffConfigs` is the testable seam (the "~30 LOC" estimate undercounted
  the catalog-load + per-axis policy binding). The operator-as-consumer fired this leg's trigger (pre-cutover "diff
  policy A vs B").
- **`LineageTree`-backed multi-policy fork — HELD.** Its trigger is **N≥3 simultaneous policy candidates** (the only
  point branching beats `diffFullProjection`'s run-twice); the operator-as-consumer fires "diff A vs B" (N=2), not
  N≥3. Run-twice is correct at N=2. Build when a third candidate materializes.
- **`VersionedPolicy.evolve` + manifest history — HELD.** Infrastructure exists (`VersionedPolicy.evolve` + the 3.2
  `ApprovalStore`), but its trigger is a real *SemVer-history* consumer (evolve against a persisted prior so bumps
  are real, not genesis-1.0.0). The `policy-diff` verb does not need it; surfacing it would be speculative SemVer
  plumbing. Build when a version-history reader demands it.
- **All of these become load-bearing once Lifecycle (5.3) gives them a timeline to operate over** — that is the
  unification (§V E4).

#### 5.7 — Remaining perf opportunities — **ASSESSED with bench evidence 2026-05-31; all four deferred (triggers unfired)**
Bench-driven assessment against the operator-reality canary (150 tables × 6.25k rows; `DECISIONS 2026-05-31 — §5.7 bench-driven assessment`). The hot paths are **deploy / IO / data-emission** (container warmup ~8.2s; static-data emission ~6.25s; `deploy.executeStream` ~4.6s) — none of the four candidates. Forcing any optimization without hotness evidence violates the bench-driven protocol (3-candidate / refutation-with-data) and risks T1 determinism for zero measured gain. Each item's trigger restated with the measured reason it has not fired:

| Item | First slice | Trigger | Measured (2026-05-31) |
|---|---|---|---|
| `parseRowsetBundle` 8 sequential `Map.ofList` | `Array.Parallel.map` the independent groupings (`CatalogReader.fs`) | bench `adapter.osm.parse.*` materially hot at 300-table scale (must refute "parallelism loses on small input") | **Not exercised** — zero `adapter.osm.parse.*` labels in the canary (it uses generated DDL → ReadSide, not the OSSYS rowset adapter). No evidence; the path needs a rowset-scale fixture first. |
| `internalEdgesOf` O(\|scc\|²) | precompute adjacency `Map<SsKey,(SsKey×EdgeStrength) list>` (`TopologicalOrderPass.fs`) | real-world SCC sizes grow (2-cycle-dominated graphs see no change) | **Refuted with data** — `pass.topologicalOrder.kind` = 2 ms total / 300 kinds; no `.scc` label fires (SCCs are 2-cycle-dominated). `internalEdgesOf` is cold. |
| schema-side level grouping → parallel schema deploy | mirror `composeRenderedLeveled` in `SsdtDdlEmitter` (~50 LOC) | schema deploy (~14s/132s) becomes visible | **Invisible** — deploy time is dominated by container warmup + static-data emission, not schema DDL. Parallel schema deploy would not move the needle at this scale. |
| OSSYS 22-rowset single-cursor extraction | split the carbon-copied SQL into parallel `SqlCommand`s on multiple connections (1.1s→~280ms) | **dedicated slice** — HIGH win, HIGH (architectural) cost; the `SequentialAccess` cursor is single-threaded by construction | **Out of scope** — extraction-side, upstream of the projection canary; needs its own measurement harness + operator decision (the connection-pool/sync cost vs the back-of-napkin 1.1s→280ms). |

#### 5.8 — `osm extract`/`profile`/`analyze` verbs — **defer (minimal-CLI posture)**
- `extract` (wraps shipped `MetadataSnapshotRunner`, ~50 LOC, "High" relevance) and `profile` (wraps shipped
  `LiveProfiler`, ~30 LOC) are the most cutover-relevant; `analyze` (~300 LOC) is nice-to-have. **Trigger:** named
  operator demand. (Note: `extract`/`profile` arg-strings already exist in `Program.fs` — confirm whether they
  dispatch or are stubs before scoping.)

#### 5.9 — Computed-column round-trip (L3-S7) — **defer (no source)**
- **Trigger:** a source (DACPAC reader / rowset) that actually populates `Attribute.Computed` (none today). Its
  ReadSide leg can ride 1.2/1.3's mechanism opportunistically when built.

---

### Wave 6 — Substantiating the isomorphism (the A→B Total-Migration epic)

*Source: the 2026-05-31 six-axis red-team — **full-fidelity record in `AUDIT_2026_05_31_FIVE_AXIS_REDTEAM.md`**
(every per-axis finding, file:line, failure scenario, severity ranking, and the complete acceptance catalog §6);
`DECISIONS 2026-05-31 — Five-axis red-team` is its summary. Read the audit before opening a slice. The NORTH_STAR matrix
reached **L1 (witness-present) = 5/5** on 2026-05-31. This wave is the buildable climb to **L2 (faithful)** on
every axis and **L3 (composed)** for the operator's one-command A→B migration — i.e. it substantiates NORTH_STAR
§1's isomorphism ladder and closes totalities T-I (faithfulness), T-V (orthogonality), T-VI (spanning).*

**The red-team verdict in one line.** The five round-trips are **adjunctions, not equivalences**: every axis is a
*partial* iso with ≥1 **silent** erasure; two axis pairs **couple** (Decision→Data, Identity→Schema); the basis
does **not span** the migration (no Permissions / Transactionality / Connection dimension); and the composed
`migrate` operation **does not exist** (five verbs, manually sequenced; renames never reach Transfer). Each slice
below names the gap, the first slice, the **acceptance witness** (a named test the matrix generator can see), and
the **rung/totality** it raises. The governing discipline is unchanged: *every erasure becomes a `Tolerance` entry,
a structured diagnostic, or a fail-loud refusal — never a silent drop* (T-I faithfulness).

#### 6.A — Faithfulness: close the silent erasures (L1 → L2)

##### 6.A.1 — Transfer fail-loud on the drop-set (Data → L2) — **the quick win**
- **Gap (red-team Data #1):** `transfer` drops FK-orphan rows (`SkippedReferences`) but the CLI **exits 0**; a
  refresh script sees "complete" while rows vanish. Violates *total decisions, named skips*.
- **First slice:** `Program.fs runTransfer` returns a distinct non-zero exit (new code, e.g. `9` —
  `transfer.droppedReferences`) when `report.SkippedReferences` (or write-time `UnmatchedIdentities`) is non-empty,
  with a per-kind count to stderr. Add a `--allow-drops` override (mirrors `--allow-cdc`) for the operator who has
  declared the drops acceptable. `TransferReport` already carries the data; this is wiring + a refusal.
- **Acceptance:** `` ``data canary: transfer with an unmatched FK exits non-zero (drop is fail-loud, not exit-0)`` ``
  — seed a referencer whose FK target has no match; assert non-zero exit + the diagnostic; assert `--allow-drops`
  downgrades to exit 0. **~S.**

##### 6.A.2 — Cyclic `AssignedBySink`: fail-loud or correct (Data → L2)
- **Gap (red-team Data #2):** a self-referential IDENTITY kind's Phase-2 `UPDATE … WHERE <pk>=<sourceVal>` keys on
  the **source** PK, which no longer exists after the sink mints → the deferred FK is left **silently wrong**.
- **First slice:** detect the case in `TransferRun.writePlan` (an `AssignedBySink` load with a non-empty
  `DeferredFkColumns`) and **refuse** with `transfer.cyclicAssignedBySink` rather than emit a no-op UPDATE. The
  correct fix (re-point Phase-2 via the captured remap AND key the WHERE on the *assigned* PK) is the follow-on;
  fail-loud first so the L2 erasure is named.
- **Acceptance:** `` ``data canary: cyclic AssignedBySink is refused, not silently mis-keyed`` `` (self-ref IDENTITY
  fixture). **~S** (refusal) / **~M** (correct re-point). **Closes the 5.2 named follow-on.**

##### 6.A.3 — Composite-identity capture or fail-loud (Data → L2)
- **Gap (red-team Data #3):** `insertCaptureRow` captures one `IsPrimaryKey && IsIdentity` column; `SourceKey`/
  `AssignedKey` are single-string. A composite surrogate is silently truncated to one leg.
- **First slice:** fail-loud (`transfer.compositeSurrogateUnsupported`) when an `AssignedBySink` kind's PK has >1
  column. Representing composite keys (tuple `SourceKey`) is the follow-on under a real fixture.
- **Acceptance:** `` ``data canary: composite-IDENTITY AssignedBySink is refused, not half-captured`` ``. **~S.**

##### 6.A.4 — Empty-string ↔ NULL fidelity (Data → L2)
- **Gap (red-team Data #4):** `toCellRows` maps deferred/missing → `""`; `Bulk.parseRaw` maps `""` → `DBNull`; so a
  genuine empty-string Text value round-trips as NULL. Three distinct meanings collapse onto `""`.
- **First slice:** decide the rule and make it explicit — either a sentinel distinguishing *absent* from
  *empty-string* in `CellValue.Raw`, or a named `Tolerance` ("empty-string Text normalizes to NULL") so the
  erasure is *closed*, not silent. Trace `RawValueCodec` to confirm the read-side already distinguishes them.
- **Acceptance:** `` ``data canary: empty-string Text round-trips faithfully (or names the tolerance)`` ``. **~M.**

##### 6.A.5 — Un-hollow `ReadSide`: indexes + FK-trust (Schema + Decision → L2) — **keystone for Decision**
- **Gap (red-team Decision #1a/1b, Schema):** `ReadSide` hardcodes `Indexes = []` (M3 MVP) → unique indexes never
  read back; FK recovery does not populate `IsConstraintTrusted` → a deployed `WITH NOCHECK` FK reads back trusted.
- **First slice:** `ReadSide` reads `sys.indexes`/`sys.index_columns` → `Kind.Indexes`, and `sys.foreign_keys.is_not_trusted`
  → `Reference.IsConstraintTrusted`. Extend `PhysicalSchema` to carry index + FK-trust facets (or name them as
  closed tolerances if deferring). Note: pairs with the known A42 2.4 container FK-readback gap — fixing FK readback
  here is the same surface.
- **Acceptance:** `` ``schema round-trip: a UNIQUE index + a NOCHECK FK survive emit/deploy/ReadSide`` ``. **~M.**
  **Unblocks 6.A.8.**

##### 6.A.6 — Name the remaining schema erasures (Schema → L2)
- **Gap (red-team Schema):** user-defined extended properties are in the IR but the emitter drops them; IDENTITY
  seed/increment is hardcoded `(1,1)`. Today these are **silent**.
- **First slice:** for each remaining silent facet, either emit it (ext-props via `ALTER … ADD EXTENDED PROPERTY`)
  or land a `Tolerance` entry + a `Skip` witness naming the erasure (Wave 1's A37 erasure-axis mechanism is the
  precedent). The goal is *closed*, not necessarily *zero* — L2 is "no silent loss," not "no loss."
- **Acceptance:** the schema-canary erasure set is fully enumerated in `Tolerance` + `AxiomTests`; no facet is
  dropped without a named home. **~M.**

##### 6.A.7 — `Synthesized`-key rename: fail-loud or persist (Identity → L2)
- **Gap (red-team Identity #4):** a first-import (non-V2 source) gives every kind a `Synthesized` SsKey from
  `(schema, table)`; a rename changes the key → A1 identity is **silently** not preserved. The codec round-trips all
  4 variants (✅), but the witness only tests `OssysOriginal`.
- **First slice:** when a `migrate`/transfer renames a `Synthesized`-keyed kind, surface it
  (`identity.synthesizedRenameUnstable`) — the operator is told identity cannot be threaded for a non-V2 source
  without a reconciliation rule. Persisting a `V2.SsKey` on first import (so subsequent renames are stable) is the
  follow-on. Add the missing `Synthesized`-variant rename witness either way.
- **Acceptance:** `` ``A1: a Synthesized-key rename is surfaced, not silently re-keyed`` ``. **~M.**

##### 6.A.8 — Decision uniqueness + FK-trust round-trip witnesses (Decision → L2) — *depends 6.A.5*
- **Gap (red-team Decision):** the decision adjunction is witnessed on **nullability only**; `EnforceUnique` and
  `DropFk`/`NoCheckFk` emit but never read back (1 of 3 sub-axes).
- **First slice:** once 6.A.5 lands index + FK-trust readback, extend the §V E3 decision-adjunction witness to the
  other two sub-axes — an overlay that `EnforceUnique` on a non-unique index and `DropFk` on a present FK, read back,
  reproduces the overlay.
- **Acceptance:** `` ``decision adjunction: read-back reproduces EnforceUnique and DropFk`` `` (raises the Decision
  cell from 1/3 to 3/3 sub-axes). **~M.**

##### 6.A.9 — `DropFk` audit trail (Decision → L2)
- **Gap (red-team Decision #2b):** dropping an FK the source enforced is a safety change, applied **silently** at
  emission (no diagnostic). `DropFk` is structurally a removal — it must surface.
- **First slice:** emit a `DiagnosticEntry` (`decision.fkDropped`, Warning) per `DropFk` so the manifest/logs name
  every constraint the engine removed. (Pairs with the existing `DecisionLogEmitter`.)
- **Acceptance:** `` ``every DropFk decision surfaces a Warning diagnostic`` ``. **~S.**

##### 6.A.10 — Attribute-level `CatalogDiff` (Time + Schema → L2) — **the structural keystone**
- **Gap (red-team Schema + Time, independently):** `CatalogDiff` is **kind-level only** (`CatalogDiff.fs:26-32`,
  explicitly deferred) → a column type/nullability/default change produces **no diff signal**. Without this there is
  no "minimum viable touches" at all.
- **First slice:** extend `CatalogDiff.between` to descend into attributes — per-kind `AttributeDiff`
  (Added/Removed/Renamed/Changed columns, with the changed facet named). Closed-DU expansion; the prescope
  (`CHAPTER_3_PRESCOPE_REFACTORLOG_AND_CATALOG_DIFF.md §2.1`) already specifies the shape.
- **Acceptance:** `` ``CatalogDiff: a column type change surfaces as an attribute-level Changed entry`` ``. **~M.**
  **Unblocks 6.A.12 and 6.D.1 — nothing in the A→B story is real without it.**

##### 6.A.11 — `applyDiff` + the evolution round-trip law (Time → L2; H-007)
- **Gap (red-team Time #1/2):** `replayTo` is a snapshot *fetch*, not a `fold applyDiff`; `applyDiff` does not exist;
  `applyDiff (between A B) A = B` is unproven. The Time axis is a store, not an evolution algebra.
- **First slice:** define `CatalogDiff.applyDiff : Catalog -> CatalogDiff -> Catalog` (the `between` peer; H-007),
  consuming the attribute-level diff from 6.A.10. Prove the round-trip law as a property test; re-base `replayTo` to
  `fold applyDiff genesis` so the Time witness becomes a real reconstruction.
- **Acceptance:** `` ``Time: applyDiff (between A B) A = B (evolution round-trip law)`` ``. **~M.** *depends 6.A.10.*

##### 6.A.12 — `diff → ALTER` minimal-touch emitter (Time → L3-precursor) — *depends 6.A.10*
- **Gap (red-team Time #3):** the SSDT emitter emits **full CREATE TABLE only**; there is no ALTER path, so
  "minimum viable touches" is structurally unsupported — the engine redeploys whole tables and leans on DacFx for
  idempotence (tool-level, not engine-level).
- **First slice:** an `EmitterOverDiff` that turns an attribute-level `CatalogDiff` into minimal
  `ALTER TABLE … ADD/ALTER COLUMN` + the existing `RefactorLogEmitter` renames. Scope first slice to the safe,
  additive ALTERs (add column, widen type, add DEFAULT); destructive/narrowing changes are fail-loud or
  tolerance-gated.
- **Acceptance:** `` ``migration: a column type change emits an ALTER, not a CREATE`` ``. **~L.**

##### 6.A.13 — Schema-level CDC-silence on idempotent redeploy (Time → L2) — *depends 6.A.12*
- **Gap (red-team Decision #3, Time #4):** CDC-silence is witnessed for **data** (`CdcSilenceTests`) but not
  **schema** — an unchanged schema still redeploys via full CREATE, churning CDC. Promise 3 (zero-surprise redeploy)
  needs engine-level schema idempotence.
- **First slice:** with 6.A.12's diff→ALTER path, an empty `CatalogDiff` emits **zero** schema DDL. Add the
  schema-side analog of the CDC pre-flight to `Compose`/`migrate` (the gate today guards only `transfer`).
- **Acceptance:** `` ``CDC-silence: redeploying an unchanged schema emits zero DDL (engine-level, not DacFx)`` ``. **~M.**

#### 6.B — Orthogonality: surface the couplings (T-V)

##### 6.B.1 — Decision↔Data pre-flight (T-V)
- **Gap (red-team Composition orthogonality #1):** a `Decision` NOT-NULL/UNIQUE tightening on a column whose source
  data violates it makes the `Data` transfer fail mid-load; nothing validates schema-vs-data compatibility before
  `--execute`.
- **First slice:** a pre-flight that, given the tightened sink schema (`DecisionOverlay`) and a source-data probe
  (`LiveProfiler` null-counts / uniqueness), reports incompatibilities (`migrate.dataViolatesTightening`) **before**
  any write. The coupling becomes a named gate, not a mid-transfer crash.
- **Acceptance:** `` ``migrate pre-flight: EnforceNotNull on a NULL-bearing column refuses before writing`` ``. **~M.**

##### 6.B.2 — RefactorLog-aware Transfer (Identity↔Schema; T-V)
- **Gap (red-team Identity #3 + Composition #2):** RefactorLog renames are applied at **project time** and never
  reach Transfer; a renamed table/column diverges the physical coordinates Transfer matches on → silent column
  mis-map risk. RefactorLog (in-place rename) and Transfer (cross-DB move) are unreconciled strategies.
- **First slice:** thread the rename map (`CatalogDiff.Renamed` + attribute renames from 6.A.10) through the
  `TransferConnections` apparatus so Transfer matches source-old ↔ sink-new by SsKey **and** projects columns through
  the rename. Document the two strategies' composition (when each applies) in `PRESCOPE_TRANSFER`.
- **Acceptance:** `` ``transfer: a renamed column is re-pointed by the rename map, not matched by ordinal`` ``. **~M.**

#### 6.C — Spanning: the missing dimensions (T-VI)

##### 6.C.1 — Connection + permission pre-flight (T-VI)
- **Gap (red-team Spanning #1/#2):** no axis carries grants; a write-denied sink silently transfers zero rows; no
  "both endpoints live + credentialed" check before mutation.
- **First slice:** a `migrate`/`transfer` pre-flight that probes source `SELECT` + sink `INSERT`/`CREATE` (a no-op
  round-trip against a temp object) and refuses (`migrate.insufficientGrants` / `connection.unreachable`) before any
  write. Permissions as a full IR axis is the larger follow-on; the pre-flight gate is the T-VI floor.
- **Acceptance:** `` ``migrate pre-flight: a write-denied sink refuses before transferring, not silently zero rows`` ``. **~M.**

##### 6.C.2 — Transactional / resumable transfer (T-VI) — **production-critical**
- **Gap (red-team Spanning #4):** `transfer` has no transaction boundary, no idempotent upsert, no checkpoint; a
  mid-transfer failure leaves a half-populated target with no rollback or safe retry.
- **First slice:** wrap the per-kind load in an explicit transaction with a checkpoint after each kind; make the
  insert idempotent (upsert / logical dedup keyed on the surrogate) so a re-run resumes rather than duplicates. At
  minimum, fail-loud with a precise "rolled back to kind K" position so retry is safe.
- **Acceptance:** `` ``transfer: an injected mid-load failure leaves the target unchanged (atomic) or resumable`` ``. **~L.**

##### 6.C.3 — Cross-database FK ordering (T-VI) — **defer-with-trigger**
- **Gap (red-team Spanning #3):** cross-DB FKs (DACPAC scenarios) have no ordering support; `SsKey` is
  `schema.table`-scoped, not `database.schema.table`.
- **Trigger:** a real multi-database source (rides Wave 4.3 cross-DB FK emission). Single-DB (the OutSystems common
  case) is unaffected. **Deferred.**

#### 6.D — The composition: `migrate A B` (the L3 bullseye; Promise 8)

##### 6.D.1 — `migrate` orchestrator + the A→B canary — *depends 6.A.10, 6.A.12, 6.B.*, 6.C.1/6.C.2*
- **Gap (red-team Composition #1):** there is no single orchestrator — the operator manually sequences five verbs,
  and renames never reach Transfer. "Nearly one command" is today five commands with a seam.
- **First slice:** `projection migrate --source-conn <A> --target <B-config> [--execute]` that chains, in one
  call: (i) `CatalogDiff.between A B` (attribute-level, 6.A.10); (ii) the Decision↔Data + permission + connection
  pre-flights (6.B.1, 6.C.1) — refuse before any write; (iii) `diff→ALTER` minimal-touch deploy, CDC-silent on the
  empty delta (6.A.12/6.A.13); (iv) RefactorLog-aware sink-minted transfer (6.B.2), transactional (6.C.2); (v)
  `verify-data` + the round-trip canary. Each stage is an existing capability; `migrate` is the *composition* + the
  pre-flight gates that make it safe.
- **Acceptance (the headline):** `` ``migrate A B: one command moves A→B with minimum viable touches; B reproduces A
  modulo the declared changes (atomic-or-resumable, fail-loud on violation)`` `` — the operator's stated use case as
  the L3 bullseye canary. **~M once its dependencies land** (the orchestration is wiring; the dependencies are the work).

#### 6.E — The self-report: matrix reports the ladder level (T-IV extension)

##### 6.E.1 — `matrix-status.sh` emits L1/L2/L3 per axis
- **Gap:** the generator reports witness-presence (L1) only; the red-team's L2/L3 distinction lives in prose.
- **First slice:** extend the generator so each axis cell carries its rung — L1 (witness exists), L2 (a faithfulness
  witness exists + the axis's erasures are all named in `Tolerance`/`AxiomTests`), L3 (the axis participates in the
  green `migrate` canary). The vision then measures its *substantiation* distance, not just its witness floor.
- **Acceptance:** `NORTH_STAR.matrix.generated.md` shows a per-axis ladder column; a regression (a new silent
  erasure) drops a cell from L2 → L1 on regeneration. **~M.** *Closes the loop: the matrix self-reports the climb.*

**Sequencing.** 6.A.1 / 6.A.9 are immediate quick wins (fail-loud + audit). **6.A.10 (attribute-level diff) is the
critical-path keystone** — 6.A.11, 6.A.12, 6.A.13, 6.B.2, and 6.D.1 all depend on it. 6.A.5 unblocks 6.A.8. The
orthogonality + spanning pre-flights (6.B, 6.C.1) and transactionality (6.C.2) are the safety floor for 6.D.1. The
honest critical path to Promise 8: **6.A.10 → 6.A.12 → {6.B.1, 6.B.2, 6.C.1, 6.C.2} → 6.D.1**, with the per-axis
L2 faithfulness slices (6.A.*) landing in parallel as confidence-builders and matrix-rung raisers.

#### 6.F — Publication & Provenance (premise-driven; source: `WAVE_6_ONTOLOGY.md`)

*The 2026-06-01 reasoning session pinned the operator's concrete premise (`WAVE_6_ONTOLOGY.md` §2): this is a
**publication-and-provenance engine for an evolving relational model**, consumed by an external team's SSIS jobs
and terminating in a schema-freeze **eject** — not a deployment engine for a live regulated PROD (PROD has no
data yet). The deploy artifact is the **declarative SSDT triple** — adjusted CREATE TABLE + refactorlog
appended-against-prior + pre/post-deployment data scripts — with DacFx computing the schema ALTER at publish
(the `WAVE_6_ONTOLOGY.md` §4 DacFx seam). CDC is the operator's **ruler** for minimal data movement (§6). This
sub-wave is the buildable projection of those moves (`WAVE_6_ONTOLOGY.md` §5).*

##### 6.F.1 — RefactorLog appended-against-prior (the Accumulate move; provenance)
- **Gap:** `RefactorLogEmitter.emit` produces the full rename set from a diff each run; it does not reconcile
  against the *prior committed `.refactorlog`*. The operator's artifact requires "appended refactor logs that
  compare themselves to the prior refactor log" — append only genuinely-new operations, never duplicate, never
  delete (a fresh-environment deploy replays all of them; handbook §"Never Delete Entries").
- **First slice:** read the prior `.refactorlog` (an emit-time input — see decision owed #1 below), compute this
  version's rename operations, emit the union deduped by `OperationKey`. The accumulated log IS the physical
  `Lifecycle.evolutionChain` (`WAVE_6_ONTOLOGY.md` P-PROV).
- **Acceptance:** `` ``refactorlog: a re-emit appends only new renames and never duplicates a prior OperationKey`` ``. **~M.**

##### 6.F.2 — Two-mode emission: fresh-replacement ⊕ incremental
- **Gap:** the engine emits one shape; the operator needs *both* — **fresh replacement** (drop all schema+data,
  load schema, load data; always correct, never minimal — the safety baseline) and **incremental** (CREATE +
  refactorlog-against-prior + data scripts; minimal, CDC-measured). `WAVE_6_ONTOLOGY.md` §4.
- **First slice:** an explicit operator-chosen `EmissionMode` (`FreshReplacement | Incremental`) threading the
  compose/CLI surface; fresh-replacement composes the existing full CREATE + full data load; incremental
  composes 6.F.1 + 6.A.12-lens + the data scripts (6.F.3).
- **Acceptance:** `` ``emission mode: fresh-replacement reloads whole; incremental touches only the delta`` ``. **~M.**

##### 6.F.3 — Data movement as post-deployment script + CDC-count enforcement (the Move move's ruler)
- **Gap:** CDC-silence is witnessed at `|delta| = 0` (`CdcSilenceTests`). The operator *enforces* the general
  property — *the capture-row count equals the true data delta* — to track "minimum viable data movements to
  arrive at current-A from an earlier-A" (`WAVE_6_ONTOLOGY.md` §6, P-DM). The minimal data plan must also be
  emitted as a **post-deployment script** (data changes as close to publish as possible).
- **First slice:** generalize the silence canary to a known small delta `k`: deploy earlier-A, apply the
  incremental data plan, assert `cdc.<table>_CT` captured exactly `k` rows (0 for unchanged). Route the data
  plan into a post-deployment script artifact.
- **Acceptance:** `` ``cdc ruler: applying the incremental data plan captures exactly the changed rows (k), zero for unchanged`` ``. **~M.** *builds on the shipped change-detecting MERGE.*

##### 6.F.4 — The published-model + provenance surface (the SSIS consumer / the eject)
- **Gap:** the external SSIS team consumes the *evolving relational model* to keep their legacy→our-shape
  mappings current; the eject freezes it. There is no consumer-facing rendering of "the model now + what changed
  this sprint" distinct from the deploy artifact (`WAVE_6_ONTOLOGY.md` P-IF / §2 / decision owed #2).
- **First slice (gated on decision owed #2):** a consumer-facing projection of `CatalogDiff` — at minimum a
  per-sprint changelog (the moves §5, human-readable) alongside the dacpac; optionally a machine-readable diff
  the SSIS team can drive mappings from.
- **Acceptance:** `` ``provenance: the published model + per-sprint move changelog regenerate from the diff`` ``. **~M.**

**Premise re-prioritization (per `WAVE_6_ONTOLOGY.md` §10).** This premise reorders the audit's generic critical
path. **Deferred-with-trigger** (PROD has no data): **6.C.2** (transactional/resumable) and **6.C.1**
(permission/connection gates) — *trigger: PROD gains data, or a write-denied environment enters a real flow.*
**Repositioned:** **6.A.12** explicit ALTER → a preview/verify/measure *lens*, not the deploy artifact (the
deploy artifact is the declarative triple; DacFx computes the schema ALTER). **Held:** 6.B.1's live form is the
**Dev→UAT user-rekey** compatibility (not PROD tightening); **6.D.1 `migrate`** remains the composition, gated by
data-compat + rekey under the ordering/partition laws (`WAVE_6_ONTOLOGY.md` P-ORD/P-CH), *not* PROD
permission/atomicity. **Decisions owed** (resolve before the dependent slice opens; full text in
`WAVE_6_ONTOLOGY.md` §10): (1) is the prior `.refactorlog` an engine input or a repo-merge concern (gates 6.F.1);
(2) what does the SSIS team consume — dacpac / changelog / machine-diff (gates 6.F.4); (3) at eject, is the
deliverable the frozen state + full refactorlog history, or the state alone (decides P-PROV append-forever vs
collapsible).


---

## IV. Dependency graph, critical path, sequencing

```
Wave 0 (truth) ─────────────────────────────► correct steering for everything
  0.1 doc-reconcile ───► 3.5 (flip-ledger needs a real readiness verdict) ───► §V E2 (generate it)

Wave 1 (integrity)
  1.1 DACPAC predicate ──────────► 4.5 (T11 DACPAC sibling) ; ──► §V E1 (the worked example for the CI gate)
  1.2 + 1.3 un-hollow ReadSide ──► 2.3/2.4 canary proofs ; ──► §V E3 (decision-layer adjunction)

Wave 2 (core loop):  2.1 ─► 2.2 ─► 2.3 ─► 2.4 ─► 2.5   (strict chain; 2.3 also needs 1.2)

Wave 3 (cutover)
  3.2 approval store ─► R6 sign-off ─► 3.5 flip-ledger ; ─► 5.6 VersionedPolicy.evolve
  3.1 R6 amendment ──► 5.1 D-exec
  3.3 user-reflow opt-in (transfer --reconcile / full-export transformGroups) ─► UAT dry-run (access-blocked)

Wave 4
  4.1 V2.SsKey persistence ─► 5.2 Slice E
  4.2 C′-wire ─────────────► Phase B functional-equivalence ─► V1 sunset

Wave 5 → Endgame
  5.3 Lifecycle ───────────► 5.6 policy-intelligence consumers ─► §V E4
```

**The one cross-cutting prerequisite to internalize:** Wave 1's un-hollowing (1.2/1.3) **must precede** Wave 2's
canary proofs (2.3+). You cannot prove "the NOT NULL decision reached the emitted DDL" via the canary if read-back
returns empty for nullability's neighbors. Integrity enables the core-loop proof.

**Critical path to V2-driver cutover:**
`0.1` (steer by reality) → `1.2`+`1.3` (canary actually verifies) → `2.1→2.5` (V2 emits *tightened* schema) →
`3.3` (user-reflow opt-in, landed) +`3.1`+`3.2` (dry-run runnable + legitimate) → **UAT dry-run** (access-blocked, last ⏳ T-30 item) → `4.2`
(Phase B functional-equivalence → V1 sunset). Everything else is capability extension or hygiene.

**Recommended opening wave (~2 sessions, all buildable-now, zero-to-low risk):**
1. `0.1` doc reconciliation — flips a readiness axis green, exposes the real gap.
2. `0.2` AsyncStream prune (6 functions) — highest-confidence move; zero blast-radius.
3. `1.1` DACPAC round-trip predicate — corrects the governance-integrity defect; builds `Catalog.equivalent`
   that Wave-1 reuses; the worked example for the §V E1 CI gate.
4. `2.1` `DecisionOverlay` — opens the highest-leverage feature thread by resolving its A18 question
   structurally, with zero output change.

---

## V. The endgame backlog — closing *that* gap

These five slices convert the waves' point-fixes into the **Total Adjunction / self-verifying engine** (§I).
None is research; each has a named completeness condition it discharges.

#### E1 — Verifiability CI gate (closes **C2**: executable-axiom totality)
- **Goal:** A test that parses the audit's per-axiom bucket assignments and **fails the build** if any axiom's
  claimed bucket exceeds its `AxiomTests.fs` evidence (e.g., "Bucket A" with no `[<Fact>]` citation). The
  phantom-DACPAC-A class becomes structurally impossible.
- **Files:** new `tests/Projection.Tests/VerifiabilityGateTests.fs`; a small parser over
  `AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md`'s bucket table + `AxiomTests.fs` decorators.
- **Acceptance:** `` ``no axiom claims a bucket its AxiomTests evidence does not support`` ``; wire into the
  pure pool so it runs every commit. 1.1 is the first axiom it would have caught.
- **Effort:** M. **Governance:** `DECISIONS` — *"Verifiability triangle is CI-enforced, not periodically audited."*

#### E2 — Generated readiness map (closes **C4** for readiness)
- **Goal:** Derive the cutover six-axis table + BACKLOG per-feature status **from** `AxiomTests.fs` bucket counts
  + `RegisteredTransform` metadata, so the surfaces cannot drift (the doc-drift meta-finding, structurally fixed).
- **Files:** new `scripts/gen-readiness.fsx` (or a CLI subcommand) emitting a `cutover/READINESS.generated.md`;
  the hand-maintained brief table becomes a generated include.
- **Acceptance:** `` ``generated readiness map matches AxiomTests bucket distribution`` ``; chapter-close ritual
  regenerates it (a diff = a drift, caught).
- **Effort:** M. **Governance:** *"Readiness verdict is generated from the proof, not authored."*

#### E3 — Decision-layer adjunction (closes **C1** for the decision axis — the deepest unification)
- **Goal:** With Wave 2 emitting decisions and Wave 1 un-hollowing read-back, prove the adjunction over the
  *decision* axis: `ReadSide(deploy(emit(C, overlay)))` reproduces `overlay` on the tightening axes — the engine's
  *opinions* are faithfully transmitted, not just its structure.
- **Files:** `tests/Projection.Tests/` new property; reuse `DecisionOverlay`, `ReadSide`, the canary harness.
- **Acceptance:** `` ``decision adjunction: emitted-then-read-back schema reproduces the DecisionOverlay`` `` —
  for every catalog + overlay, the round-tripped catalog's nullability/uniqueness/FK state equals the overlay's
  enforce sets. Promote a numbered axiom (A43 candidate) underwriting it.
- **Effort:** M. **Deps:** Wave 2 + 1.2/1.3. **Governance:** A43 candidate; this is the formal statement that
  V2's tightening is *provably faithful* — the strongest "stronger than V1" claim in the system.

#### E4 — Lifecycle operationalization (closes **C3**: input totality; unlocks policy intelligence)
- **Goal:** Build the `Lifecycle` axis (§5.3) to its first real consumer, completing `Catalog × Policy × Profile ×
  Lifecycle` and giving the `LineageTree`/`PolicyDiff`/`VersionedPolicy` substrate a timeline to operate over.
- **Files / types:** per §5.3 (L-α → L-δ) + the first consumer (refactor-log baseline from a stored prior version).
- **Acceptance:** the four A-Lifecycle axioms flip Skip→Fact; a 2-version `evolutionChain` → `RefactorLogEmitter`
  produces correct `sp_rename` entries end-to-end.
- **Effort:** L. **Deps:** the §5.3 trigger fires. **Governance:** cashes A6/A17 temporal-axis amendments;
  this is the gateway slice for §5.6.

#### E5 — Registry-as-documentation (closes **C4** structurally)
- **Goal:** Emit a `registry.manifest.md` / readiness fragment from `RegisteredTransform` metadata + `AxiomTests`
  buckets; wire it into the chapter-close ritual as a **generated** (not authored) surface — the registry, which
  already drives execution + provenance + classification, also drives documentation.
- **Files:** new generator (CLI subcommand or `.fsx`); chapter-close ritual checklist update.
- **Acceptance:** `` ``the registry manifest is regenerable and matches the live registry`` ``; a stale doc =
  a failing regeneration diff.
- **Effort:** M. **Governance:** *"Docs derive from the registry; drift is a build failure."* Completes the
  self-describing half of the self-verifying engine.

**The endgame in one line:** E1+E2+E5 make the system *self-describing* (docs cannot drift); E3+E4 make the
adjunction *total* (every axis the engine owns round-trips, including its own decisions and its own history). At
that point "replace V1" is not a promise — it is a continuously-checked theorem.

---

## VI. Decisions owed + open external gates

**Decisions owed (yours to make; blocking the slices that cite them):**
1. **The R6 execute-path amendment (3.1).** The Transfer `--execute` path ships gated by an env var with **no
   formal authorization**. This is a latent governance gap independent of OPEN-2; the amendment should be authored
   before any UAT execute. *Owner: principal-PO sign-off (OPEN-4).*
2. **Doc-drift root cause.** Three "shipped but docs say deferred" cases + one "claimed-A with no test" indicate the
   chapter-close ritual's staleness check is not firing reliably. Recommend adopting **E1 + E5** (make it structural)
   rather than re-exhorting the ritual. *Decision: do we invest in the generated-surface endgame now, or keep
   reconciling by hand?*
3. **`osm verify-data` vs `Reconciliation.fs` overlap (4.4).** Decide reuse vs. new module before building.

**Open external gates (organizational, not engineering):**
- **OPEN-2** (the big one): does OutSystems Cloud UAT expose a writable SQL connection to entity-backing tables, or
  is it platform-API-only? Gates Transfer D-exec (5.1) + real-UAT Slice E. **Resolve first** via the ops spike (5.1).
- **UAT access + a live UAT database connection:** gates the T-30 dry-run itself (the reflow capability — slice 3.3 — is LANDED as opt-in in transfer/full-export; the run against a real UAT sink is access-blocked). No CSV inventory needed: transfer reads the live UAT sink.
- **OPEN-1 / OPEN-3 / OPEN-6:** disposition mix (blank vs pre-existing), UAT CDC-tracking, CHECK/trigger collisions
  — all secondary to OPEN-2.

---

*Recorded for the executing agent. This plan is a projection of the code as of 2026-05-30; regenerate the readiness
and bucket claims against `AxiomTests.fs` before acting on any §III status (and build §V E2/E5 so that regeneration
is automatic). Hold the spine.*
