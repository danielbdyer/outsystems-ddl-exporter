# CRYSTALLINE_FORM.md — The Codebase at Rest

**Date:** 2026-06-04
**Status:** Vision document, adversarially hardened. Companion to
`AUDIT_2026_06_04_BLINDSPOT_COMPRESSION.md` (the empirical basis) — read that
first for the findings; read this for the shape they imply.
**Method:** The naive "crystalline" claims (one IR, one writer, derived codec)
were each handed to an adversarial agent tasked with *falsifying* them by
finding a discriminating input. Two of three were substantially falsified. The
surviving, sharpened claims are what this document records. Every "do" and
"do NOT" below is backed by a discriminating input, not an aesthetic.

> A vision that survives an honest attempt to break it is worth building toward.
> One that doesn't is a trap dressed as a simplification. The two collapses this
> document *rejects* would each have damaged the system — the IR fusion would
> have blinded the canary; the writer fusion would have dragged mutable lock
> state into the pure core. Recording the rejections is the point.

---

## Contents

- [1. The crystal, in one sentence](#1-the-crystal-in-one-sentence)
- [2. The governing principle the adversarial wave revealed](#2-the-governing-principle-the-adversarial-wave-revealed)
- [3. The irreducible structure (five surfaces, none fusible)](#3-the-irreducible-structure-five-surfaces-none-fusible)
- [4. What collapses, what must NOT, and why](#4-what-collapses-what-must-not-and-why)
- [5. The honest size story](#5-the-honest-size-story)
- [6. The reachability path](#6-the-reachability-path)
- [7. Adversarial appendix — the three probes, verbatim verdicts](#7-adversarial-appendix--the-three-probes-verbatim-verdicts)

---

## 1. The crystal, in one sentence

> **The entire system is one commuting square — `read(emit(project A)) ≅ read A`
> — where `≅` is *equality in a deliberately lossy quotient*, and the crystalline
> codebase is the minimal set of functionally-distinct surfaces that makes that
> square typecheck with the fidelity relation holding by construction rather than
> by test.**

The repo already names this (T16, the master equation; `NORTH_STAR.md`'s
"fidelity as a theorem the engine proves about itself"). What the adversarial
wave added is the precise content of `≅`: **it is not equality.** It is
commutativity *after projecting through a quotient that erases exactly the
distinctions SQL Server's catalog cannot symmetrically return* (§3.2). The
canary is green when source and target agree *in that quotient* — and the
quotient's coarseness is load-bearing, not a defect.

## 2. The governing principle the adversarial wave revealed

All three falsifications share one shape:

> **Shared vocabulary ≠ shared type. Functional distinction is load-bearing.**

| Surface pair | Shared *vocabulary* | NOT shared | Naive fusion would… |
|---|---|---|---|
| codec write ↔ read | the JSON field names | the *direction* — they are not mechanical inverses (`writeOnly`, legacy non-injective decode, normalization bypass) | silently break round-trip on the asymmetric fields |
| `Attribute` ↔ `PhysicalColumn` | the facet names (Type, Length, …) | the *type* — Intent (`SqlStorageType`, fine) vs Quotient (`PrimitiveType`, coarse) | blind the canary (BIGINT→INT invisible) OR fail every faithful redeploy |
| `Lineage` ↔ `Diagnostics` ↔ `Bench` | the writer *syntax* (`tell`/`write`) | monoid (append vs statistical fold), lifecycle (pure vs lock-protected sink), consumer (audit vs triage vs perf-gate) | drag the one piece of module-level mutable state in Core into the pure carrier |

The codebase's actual disease, then, is **not duplication** — it is **false
symmetry**: it reaches for collapse (and builds speculative algebra to justify
it) on the assumption that surfaces sharing a vocabulary share semantics. The
`AUDIT_2026_06_04` framing ("over-abstracted on symmetry, under-abstracted on
duplication") is correct; this document supplies the *why*: the symmetry it
over-abstracted on is **false** symmetry.

Therefore the crystalline move is a **four-verb autophagy**, not a fusion:

1. **DELETE** the false-symmetric algebra built on assumed symmetry that has no
   consumer (`Prism`, `PassContext`, `LineageTree`, `Pass.product`/`&&&`,
   `Certificate`, `DiagnosticLattice`, `SqlStorageType.to/ofPrimitiveType`). This
   is the single most defensible prize: **−746 LOC of property-tested-but-unconsumed
   code** (§5), deletion not refactoring.
2. **SINGLE-SOURCE** the genuinely-shared *vocabulary* (the `ColumnType` facet
   tuple across the 5 logical-IR sites; the codec field-name pairing) — without
   merging the *types* that vocabulary describes.
3. **SEAL** the leaks where one surface's content misroutes into another (the 3
   `*.computed` Info "status strings" that belong in `Bench`, not `Diagnostics`).
4. **NAME** each functional distinction the codebase currently leaves implicit
   (Intent vs Quotient IR; the 3-shape emitter output taxonomy; the 3 disjoint
   writer sinks) so the next agent recognizes the surface instead of re-fusing it.

## 3. The irreducible structure (five surfaces, none fusible)

The crystal is **not** "one noun, one verb." It is exactly these surfaces, each
distinct *by function*, sharing vocabulary at named seams:

### 3.1 Catalog — the IR, with one *vocabulary* but two *types*

`Catalog`/`Module`/`Kind`/`Attribute`/`Reference`/`Index`/`Sequence` is the
emission-**intent** surface: what V2 *means* to write, in full fidelity
(`SqlStorageType` carries `BIGINT` vs `INT`; `Default : SqlLiteral`;
`Computed : ComputedColumnConfig`; `OnDelete`/`OnUpdate`/`IsUserFk` on
references; ~18 fields on `Index`). The single crystalline edit here is to lift
the facet *vocabulary* `(Type, Length, Precision, Scale, IsIdentity, SqlStorage,
ExternalDatabaseType, Default, Computed)` into **one `ColumnType` VO**, killing
the five-site hand-enumeration (`Attribute`, `changedFacets`, `applyFacet`,
`renderColumn`, and the dead `SqlStorageType.to/ofPrimitiveType`).

### 3.2 PhysicalSchema — the readback **quotient** (do NOT fuse with 3.1)

`PhysicalColumn`/`PhysicalForeignKey`/`PhysicalIndex` is a **deliberately lossy
projection** of the Catalog: `PhysicalColumn.Type` is the *coarse* `PrimitiveType`,
**not** `SqlStorageType`, and `PhysicalColumn` has *no* `SqlStorage` field at all.
This is the load-bearing crystal the naive vision missed:

> Both legs of the canary round-trip through `INFORMATION_SCHEMA` / the ScriptDom
> AST, which can only *symmetrically* recover the coarse type category. `ReadSide`
> hard-sets `SqlStorage = None` on every readback. So `PhysicalSchema.ofCatalog`
> is **total but not faithful** — it is the quotient map `π : Intent → Quotient`,
> and `≅` in §1 is *equality after `π`*. The named `Tolerance` entries
> (`CharAnsiPaddingTolerated`, `DecimalScaleTolerated`, `IndexOptionsUnreflected`)
> and the `normalizeDefault` string-canonicalization (`0` ≡ `((0))`) are the
> quotient's defining relations, made explicit.

**Discriminating input (why this must stay distinct):** a `BIGINT` column
round-trips to `Integer` on both legs → green; mutate the target to emit `INT`
(a real data-truncating regression) → *still green*, because the quotient erases
`BIGINT` vs `INT` by design. Give `PhysicalColumn` the `SqlStorage`-bearing
`ColumnType` and the *first faithful redeploy* fails (source `Some BigInt` vs
readback `None`). The coarseness is the canary's equivalence relation; fusing the
types either blinds it or breaks it. **`Attribute` and `PhysicalColumn` share a
facet vocabulary, never a facet type.**

### 3.3 Profile / Policy — evidence and intent (already clean)

`Profile` (empirical evidence; A34 independent of Catalog/Policy) and `Policy`
(operator intent; the `OverlayAxis` = `Selection | Emission | Insertion |
Tightening`). These are the two inputs `enrich` consumes. They are already
crystalline; the only debt is the `*Binding` resolution duplication (orchestration,
not Core — §5).

### 3.4 The pass — one Kleisli `enrich`, with `GraphView` threaded

`Pass<'a,'b> = 'a -> Lineage<Diagnostics<'b>>` is the right arrow and already
exists. The crystalline edits are mechanical, not structural: (a) one
`Analytics.touchAll` combinator absorbing the 6-site `Touched`-events + status
epilogue; (b) a `GraphView` (forward/reverse/undirected adjacency) computed once
on `TopologicalOrder` and threaded, instead of rebuilt in 5 passes; (c) the
correctness fix — `SchemaComplexityPass` must receive `state.TopologicalOrder`,
not `TopologicalOrder.empty` (it currently computes every FK metric over an empty
graph). The `*Rules.evaluate` skeleton (4× `gate → structural → profile-probe`)
collapses to one parameterized `DecisionSkeleton` or active patterns.

### 3.5 Emit — `perKind` + a *named* 3-shape taxonomy

`emit.perKind : (Kind -> 'e) -> Emitter<'e>` is the missing combinator that
collapses the 6-site `allKinds → map → ofList → create` walk. But the crystal is
*not* "every emitter is per-kind": there are genuinely **three output shapes** —
`PerKindArtifact` (the 5 T11-commuting emitters), `FlatStatementStream`
(SchemaMigration — a flat ALTER stream from a `CatalogDiff`), and `CatalogSummary`
(Manifest — a catalog-wide summary). The crystalline edit is to *name* this
closed taxonomy in Core so T11's scope (it holds for `PerKindArtifact` only) is a
documented type fact, not an undocumented surprise.

### 3.6 The three writer **sinks** — distinct by construction (do NOT fuse)

- `Lineage<'a>` — pure, order-sensitive (A24 chronological), `LineageEvent list`
  under `@`; consumed by the **manifest/audit** trail.
- `Diagnostics<'a>` — pure, `DiagnosticEntry list` under `@`; consumed by the
  **operator triage** surface. Already `WriterT`-stacked with Lineage as
  `Lineage<Diagnostics<'a>>` — these two *do* share one writer algebra and may be
  DRY'd behind a generic `Writer<'log,'a>` (a small win, two pure writers).
- `Bench` — **impure**, the *only* module-level mutable state in Core (lock-protected
  `Dictionary<string,ResizeArray<int64>>`), a statistical fold (P50/P95/P99) not a
  free monoid, **no value channel at all** (`string × int64 → unit`); consumed by
  `scripts/perf-gate.sh`'s μ+σ baseline.

**Discriminating fact:** the audit's "misrouted status-strings" evidence (3
analytics passes emitting `Info "*.computed"` traces through `Diagnostics`) does
*not* prove the channels are one — it proves they are **distinct and leaking**. If
they were one channel there would be nothing to misroute. The crystalline move is
to **seal the leak** (route the 3 traces to `Bench.recordSample`) and **keep three
sinks**. Fusing Bench into the pure writer would forfeit the equational-reasoning
carve-out the entire purity-first discipline is built on.

## 4. What collapses, what must NOT, and why

| Proposed collapse | Verdict | The discriminating reason |
|---|---|---|
| `CatalogDiff` 4 channels → `ChannelDiff<'facet>` | **DO** (~250 net LOC) | the 4 records + 4 `applyXDiff` skeletons are mechanical copies; facet tables + per-channel `Change` records genuinely differ and survive |
| Codec 60 pairs → derived `Codec<'a>` | **DO, +2 escape hatches** | ~58/60 collapse and the `{create…with…}` hazard becomes structurally impossible; but `codecVersion` is **write-only**, `Reference` constraint-state **bypasses the normalizing setter** for byte-fidelity, and **`Index.Uniqueness`** is a non-injective legacy decode — needs `writeOnly` + `iso`/`legacy` primitives |
| Dead algebra → **delete** | **DO (−746 LOC, the real prize)** | `Prism`/`PassContext`/`LineageTree`/`Pass.product`/`&&&`/`Certificate`/`DiagnosticLattice` have **zero production callers** (grep-verified); built on symmetry, not demand |
| `Attribute` ⊕ `PhysicalColumn` → one `Column` | **DO NOT** | they are Intent vs Quotient; the coarse `PhysicalColumn.Type` *is* the canary's equivalence relation (§3.2). Single-source the *vocabulary* (`ColumnType`), never the type |
| `Lineage`/`Diagnostics`/`Bench` → one `Witness` | **DO NOT** | Bench is an impure sink with no value channel, a statistical monoid, and a separate consumer. Seal the leak, keep three (§3.6). Optionally DRY the two *pure* writers |
| 6 analytics epilogues → 1 combinator | **DO (−75 LOC)** | pure mechanical repetition |
| 5 adjacency rebuilds → 1 `GraphView` | **DO** | DP/memoization; also fixes a latent perf cliff at 300-table scale |
| ReadSide 15 loops → `readRows<'T>` kernel | **DO (~400 LOC)** | the combinator (`readResultSet<'T>`) already exists in a sibling adapter, unused here; also closes the retry gap |

## 5. The honest size story

The naive vision implied a dramatically smaller codebase. The measured floor
**corrects this**, and the correction is itself crystalline:

> **The algebraic heart (`Projection.Core`) is already ~95% crystalline.** Measured
> reduction from all Core collapses: **~1,086 LOC of 20,913 ≈ 5%** — and ~746 of
> that is *deleting dead algebra*, not compressing live code. Core is dense and
> good. The bloat is **entirely in the periphery**, exactly where the optimization
> disciplines were never turned on:

| Region | Crystalline pressure | Rough reclaim |
|---|---|---|
| `Projection.Core` | delete dead algebra (−746), `ChannelDiff` (−250), analytics combinator (−75), `ColumnType` (−15) | **~5%** (~1,086 LOC) |
| Adapters (`ReadSide`, `CatalogReader`) | `readRows` kernel (~400), CatalogReader JSON/rowset path unification + decompose | **large** |
| Pipeline (`*Binding`/`*Run`/`*Diagnostics`) | resolution module, diagnostic registry, `MigrationRun` 2^N collapse (~250) | **large** |
| Targets | `emit.perKind`, codec derivation, shared `CatalogDiff.foldByKind` | medium |
| **Tests (3× Core)** | fixture centralization (`mustOk`×63 → 1; ~300-500 LOC), property-collapse, typed-AST comparison instead of golden strings, **plus deletion of dead-algebra tests** | **largest** |
| **Docs (~5.7 MB ≈ code)** | archive ~1.96 MB sediment; prune ~300-440 KB DECISIONS narrative; fix 3 stale numeric claims; one entry point | **~45-55%** |

The deepest size insight: **the test suite is 3× Core because the invariants are
convention-enforced, so the tests carry the proof the types don't.** `AxiomTests`
is ~30% skipped; ~19 emitter tests assert byte-exact golden strings; the codec
round-trip property has coverage holes (no SsKey-variant coverage; the lossy
`Index.Uniqueness` arm untested). In the crystal, proof obligations migrate from
tests into types — typed-AST comparison replaces golden strings, the codec
combinator makes drift a compile error, the `ColumnType` VO makes the facet list a
single fact — and the test mass shrinks *toward the genuinely combinatorial*. The
3×-Core test bulk is not coverage; it is the measurable debt of invariants not yet
pulled into the types. **This is "fidelity as a theorem" applied to the engine's
own source instead of its output.**

## 6. The reachability path

Ordered by *defensibility × irreversibility-toward-the-crystal*, cheapest first:

1. **Delete the dead algebra** (`Prism`/`PassContext`/`LineageTree`/`Pass.product`/
   `&&&`/`Certificate`/`DiagnosticLattice` + their tests; `SqlStorageType.to/ofPrimitiveType`).
   −746 Core LOC + tests. Pure deletion of grep-verified-unconsumed code — the
   safest large move, and it removes the false-symmetry temptation at its source.
2. **Fix the two correctness bugs** (`SchemaComplexityPass` empty-topology;
   `pickLabel` self-comparison) and **seal the writer leak** (3 `*.computed` traces
   Diagnostics → Bench). Small, verifiable.
3. **Extract the three kernels**: `emit.perKind`, `Analytics.touchAll`,
   `readRows<'T>` (+ retry). Each closes a half-applied discipline.
4. **`ColumnType` vocabulary VO** (§3.1) + the `ChannelDiff<'facet>` collapse it
   unlocks. Single-source the facet *vocabulary*; leave `PhysicalColumn` a distinct
   quotient (§3.2).
5. **Derived `Codec<'a>`** with the `writeOnly` + `iso`/`legacy` primitives; close
   the two property-test coverage holes in the same slice.
6. **Name the distinctions**: the 3-shape emitter taxonomy; the Intent/Quotient IR
   cleavage (promote the `Coordinates.fs` comment to a type-level statement); the
   three writer sinks.
7. **Periphery + tests + docs autophagy** — the largest reclaim, lowest algebraic
   risk: fixture centralization, golden-string → typed-AST, doc archival + prune.

Each step is independently shippable and verifiable against the existing suite.
Step 1 is the recommended first move: it is deletion, it is safe, and it ends the
false-symmetry pattern that produced the speculative algebra in the first place.

## 7. Adversarial appendix — the three probes, verbatim verdicts

The hardened claims above rest on three falsification attempts. Their verdicts,
condensed (full transcripts in session history):

**Probe A — "unify logical + physical IR":** *PARTIAL.* Extract `ColumnType`
vocabulary (sound, ~5 sites); do **not** collapse `PhysicalColumn` into `Attribute`
— it is a deliberate lossy quotient and `PhysicalColumn.Type` (coarse
`PrimitiveType`) *is* the canary's equivalence relation. Sharpest input: BIGINT→INT
is invisible to the canary by design; either fusion direction breaks fidelity.
`SqlStorageType.to/ofPrimitiveType` separately dead.

**Probe B — "derive the codec to one source of truth":** *PARTIAL → leaning
SOUND.* ~58/60 pairs collapse and the default-substitution hazard becomes
structurally impossible; needs `writeOnly` (for `codecVersion`) and `iso`/`legacy`
(for `Index.Uniqueness`, the irreducible seam). `Reference` constraint-state read
deliberately bypasses the normalizing setter for byte-fidelity — a derived codec
must preserve that. Two property-test coverage holes found (SsKey variants; the
lossy uniqueness arm).

**Probe C — "fuse the writer trinity to one `Witness` carrier":** *UNSOUND on the
strong claim.* Bench is an impure lock-protected sink with no value channel, a
statistical (not free-monoid) fold, and a separate consumer (`perf-gate.sh`).
Lineage + Diagnostics already share one algebra (WriterT-stacked) — a small DRY win
is available there, but Bench must stay separate. The misrouted status-strings
prove the channels are *distinct and leaking*, not identical. Measured Core floor:
~1,086 LOC reduction ≈ 5%; the −746 LOC dead-surface deletion is the defensible
prize.

---

*Three of three boldest collapses were tested; two were substantially rejected.
The crystal is more subtle than "make it one of everything" — it is a small number
of surfaces, each distinct by function, sharing vocabulary at named seams, with the
false-symmetric scaffolding between them removed. Hold the distinctions; delete the
symmetry that has no consumer; single-source only what is genuinely one thing.*
