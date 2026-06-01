# WAVE 6 MORPHOLOGY — Amino Acids, Proteins, and the Concern-Movement Field

> **What this is.** The *concrete grounding* the ontology and algebra were missing. `WAVE_6_ONTOLOGY.md` named the
> entities, `WAVE_6_ALGEBRA.md` cast them as a torsor calculus — both written **over** the codebase. This document
> is written **from** it: a four-agent structural research pass (2026-06-01) that maps (a) the **amino acids** —
> the irreducible building blocks that exist *now*, with maturity and file:line; (b) the **proteins** — the
> concrete use cases assembled from them; (c) the **concern-movement field** — how the five concerns move through
> *emission space* and *episode-time*, and where that movement is observable vs **dark**; and (d) the **future
> state** required to observe concern movement during *multi-episodic recombination*. It is the source for the
> sharpened `WAVE_6_ALGEBRA.md` §12 (the concern-movement field; the latent/activated distinction) and the
> `EXECUTION_PLAN.md` 6.G/6.H route. Sibling to the ontology (interpretation) and the algebra (equation); this is
> the **territory** the map was drawn over.
>
> **On the vocabulary (pillar-8 guard).** "Amino acid" / "protein" / "morphology" are *expository* framing for
> this document, not proposed type names — do **not** reify an `AminoAcid` or `Protein` type. The durable,
> concept-shaped vocabulary is the calculus's: *carriers* and *operator-verbs*, `Episode`, `LifecycleStore`, the
> *concern-movement field*. The biology is the lens; the algebra is the language.

---

## 0. The central finding — the calculus is *latent*, not *activated*

The four agents converge on one structural truth, in four projections:

- **Carriers reified; verbs not.** 7 of 8 amino-acid families are production-grade — `Catalog`/`PhysicalSchema`
  (State), `CatalogDiff` (the schema δ), `Lifecycle` (Time), `DecisionOverlay` (Decision), the writer trinity
  (Lineage/Diagnostics/Certificate), the realization stack (`Pass`/`Emitter`/`Statement`/`Deploy`). **But the
  algebra's *operators-as-types* have no code home:** there is no `Move`, no abstract `Delta`, no norm `‖·‖`, no
  channel projection `π`, no `Torsor`. The codebase types the **nouns** of change and leaves the **verbs** as
  functions (`between`/`applyDiff`) and test-witnessed laws.
- **The diff-machinery is orphaned.** `CatalogDiff.between`/`applyDiff`, `SchemaMigrationEmitter`,
  `RefactorLogEmitter`, and the whole of `Lifecycle` have **zero production callers** outside provenance-replay
  *in tests* — "amino acids with no protein." Every shipping use case is **single-leg** (moves a subset of the
  five concerns; none composes schema+data+verify); no orchestrator exists; renames flow into emit but **never
  into transfer**.
- **The engine ships `realize(B)`, not `emit(B ⊖ A)`.** The integrated path (`Pipeline.projectFromChainWithState`)
  is `Catalog → Outputs` with **no `CatalogDiff` input**. The displacement δ is computed by no wired path; its
  norm `‖δ‖` is in no artifact; **the differential leg of T16 is dark.**
- **There is no durable episode to integrate over.** `between`/`applyDiff`/`reconstructLatest` (the discrete FTC
  `genesis ⊕ Σδ`) run **only over in-memory values in tests.** `CatalogSnapshot` is constructed only in
  fixtures, never serialized; it is **schema-only, single-plane** (a Catalog — no Profile, no rows, no CDC);
  the refactorlog is **time-erased** (`ChangeDateTime` pinned to a constant) and **recomputed per episode**; the
  CDC log is **ephemeral**. The *only* concern that survives an episode boundary is **Identity** (`V2.SsKey`
  round-trips through the substrate) — and only as a current value, not a chain.

**Update (2026-06-01, prework landed).** The first activation prework shipped: `CatalogDiff.compose` (the `+`),
`CatalogDiff.norm`/`channelCounts` (the concrete schema-side ‖·‖/π), and `Lifecycle.netDiff` (the integral ∫δ) —
so the schema-side measurement layer and the derivative algebra are no longer dark (A-Lifecycle-4 flipped to a
live witness). The *remaining* latency is the durable substrate (no persisted `Episode`) and the wiring of the
differential leg into `migrate`. The finding below stands as the research snapshot it was.

**Update (2026-06-01, the persistence keystone landed).** The "no durable episode" finding's hardest
prerequisite is now met: `CatalogCodec` (`Projection.Targets.Json`) is a **total / deterministic /
re-validating** `Catalog↔JSON` round-trip (`deserialize (serialize c) = Ok c`) — the schema-plane persistence
primitive the durable `Episode`/`LifecycleStore` rest on. "Schema-only, never serialized" is now "schema-only,
*serializable*"; the residual is the multi-plane envelope (Profile + refactorlog reference + CDC handle) and the
δ-chain framing (6.H.1/6.H.2, both unblocked). Two reusable takeaways from building it — *totality is verified
against the IR inventory, not asserted* (the `{ X.create … with … }` default-substitution hazard), and *test
inputs are declarative edits of the producer's own output, never hand-authored wire format* — are codified at
`DECISIONS 2026-06-01` and apply to every remaining 6.H/6.D slice that reconstructs or round-trips the IR.

**The synthesis:** the engine has built every *carrier* and proven every *law in isolation*, but the *proteins
that would move concerns through emission space and across episodes are unbuilt.* The calculus is **correct and
latent**; *activation* = wiring the differential leg + reifying the measurement verbs (where evidence now
justifies) + persisting the episode. This is precisely the gap between an algebra written *over* the code and one
read *from* it.

---

## 1. The morphology — amino acids → proteins

### 1.1 The eight amino-acid families (what exists now)

| Family | Representative amino acids (file:line) | Calculus role | Maturity |
|---|---|---|---|
| **Identity** | `SsKey` (`Identity.fs:45`, 4-variant + codec); `Name` (`Catalog.fs:18`); `TableId` (`Coordinates.fs:151`); `Schema/Table/ColumnName` VOs (`Coordinates.fs:46-58`) | the conserved charge + 3 name-spaces (Identity/Designation/Realization) | **mature**; Realization VOs **underused** (fields still raw `string`) |
| **Structure (State)** | `Catalog/Module/Kind/Attribute/Reference/Index` (`Catalog.fs:889…420`); `PhysicalSchema` (`PhysicalSchema.fs:212`) | the torsor's *points* (model + substrate) | mature |
| **Delta** | `CatalogDiff` (`CatalogDiff.fs:88`, `between`=⊖ `:207`, `applyDiff`=⊕ `:384`); `AttributeFacet/Change/Diff` (`:27-55`); `RenameRecord` (`:11`); `PhysicalSchemaDiff` (`:236`) | the displacement δ | **mature for schema; the data δ is not a value** |
| **Temporal** | `Lifecycle` (`Lifecycle.fs:56`); `Version/Timeline/CatalogSnapshot` (`:9-45`); `evolutionChain` (`:115`), `reconstructLatest` (`:152`, the FTC) | Time as a torsor; ∫ over episodes | mature *in-memory*; **never persisted** |
| **Decision** | `DecisionOverlay` (`DecisionOverlay.fs:20`); `*Outcome` DUs; `Classification/OverlayAxis` (`Classification.fs:78/24`); `ApprovalWorkflow` | discharged operator intent | mature |
| **Data** | `StaticRow` (`Catalog.fs:77`); `CellValue` (`Statement.fs:234`); `SurrogateRemapContext` (`SurrogateRemap.fs:95`); `DataLoadPlan` (`DataLoadPlan.fs:49`); `Reconciliation` | Reidentify + Move plan | mostly mature; **cells stringly-typed in IR** |
| **Writer / Observability** | `Lineage`/`LineageEvent`/`TransformKind` (`Lineage.fs:312/287/228`); `Diagnostics`/`DiagnosticEntry`; `Certificate`; `TransformRegistry`; `Bench`; optics (`Prism`/`Lens`) | provenance + the observability integral | mature; optics/lattice **integration-light** |
| **Realization** | `Pass<'a,'b>` (`Diagnostics.fs:464`); `Emitter`/`EmitterOverDiff`/`ArtifactByKind` (`Types.fs:50-63`); `Statement` (`Statement.fs:269`); `Render`; `Deploy.executeStream` (`Deploy.fs:710`); the sibling emitters | `emit` / `realize` / `run` (T16) | mature |

### 1.2 The proteins (the concrete use cases) and what they move

| Protein | CLI → Pipeline → emit/deploy | Concerns moved | Leg |
|---|---|---|---|
| **full-export** | `runFullExport` → `Compose.runWithConfig` → `projectWithState` → SSDT+JSON+Distributions+diagnostics | Schema (full CREATE) + Decision (overlay) | single (schema/decision) |
| **emit / deploy** | `Compose.run`/`project` → `SsdtBundle` / `Deploy.executeStream` | Schema (+ Decision on `--config`) | single |
| **canary** | `Deploy.runWideCanary` → source-phase → `emit` → target-phase → `PhysicalSchema.diff` | Schema (the T16 schema sub-square) + Identity (SsKey round-trip) + Decision (overlay survives) | single (schema readback) |
| **transfer** (incl. Dev→UAT rekey) | `runTransfer` → `runThroughConnections` → `runCore` → `reconcileAgainstSink` → `writePlan` (two-phase) | **Data** (two-phase bulk + FK repoint) + **Identity** (Reidentify: ReconciledByRule / AssignedBySink) | single (data) |
| **verify-data** | `runVerifyData` → `DataIntegrityChecker.compare` (row+null counts) | Data (count deltas) | single |
| **policy-diff / approve** | `PolicyDiff.diffConfigs` (two chain runs, joined by SsKey) / `ApprovalWorkflow` | Decision | single |
| **migrate A B** | — | (schema+data+identity+time+decision) | **does not exist** |

**The noun/verb asymmetry (the morphological law).** The codebase reifies *carriers* eagerly and leaves
*operators* as functions — which is *correct discipline* (`WAVE_6_ALGEBRA.md` §11: "state it in the native
algebra first"; the two-consumer threshold). But it has a cost the research makes concrete: the **measurement
verbs** (`π` channel, `‖·‖` norm) and the **cross-plane `Delta`** have no structural home, so concern-movement
*cannot be observed or enforced* except in isolated tests. **The verbs earn reification exactly at the second
consumer** — when the data leg becomes the second `comparison→apply→emit` and `migrate` composes the channels —
not before. (This is the spine-holding rule of `EXECUTION_PLAN.md` §6.G, now grounded in *why*: the verbs are
absent because the second consumer hasn't been built, not by oversight.)

---

## 2. The concern-movement field (the novel calculus inference)

A concern κ (Schema/Data/Identity/Time/Decision) occupies a position in a **2-D field**: an *emission coordinate*
(which artifact) and an *episode coordinate* (which time-step). Its movement is read by **two partial
derivatives**, each with an **integral**:

- **∂κ/∂(emission)** — how κ distributes/changes across artifacts at a fixed episode. **Integrated by the
  manifest** (`∫ over emission space`).
- **∂κ/∂(episode)** — how κ changes across episodes. **Integrated by the provenance** (`∫ over time` = the
  refactorlog + CDC log + snapshot chain = a durable `LifecycleStore`). The FTC (T13) is this integral recovering
  state: `reconstructLatest = genesis ⊕ Σδ`.
- **∂²κ/∂(emission)∂(episode)** — how κ's emission-distribution changes *across* episodes = the **change-manifest
  series** (the manifest-of-δ over time). Today the *state*-manifest exists at one episode; the **series does
  not**.

"Observe the movement of concerns during multi-episodic recombination" = observe **∂κ/∂(episode)** and the
**cross-concern recombination** (κ₁ at episode i × κ₂ at episode j) — which requires a *multi-plane episode* that
co-records all five concerns, so recombination is a *join* over the provenance store.

### 2.1 ∂κ/∂(emission) — the concern × artifact observability matrix (Y / partial / **dark**)

| κ ↓ / artifact → | CREATE bundle | refactorlog | ALTER lens | data scripts | manifest (∫) | diag channels | JSON | Distributions |
|---|---|---|---|---|---|---|---|---|
| **Schema** | **Y** state | P (rename axis) | **Y** (unwired) | — | **Y** Coverage/Predicate | P | **Y** | — |
| **Data** | P (seeds) | — | **D** | P (silence floor only) | P (profiles) | P | **D** | **Y** |
| **Identity** | P (logical name) | **Y** (rename) | P | P (reconciled key) | P (`rootOriginal` drops `[derived]`) | P | **Y** (`display` keeps `[derived]`) | P |
| **Time** | **D** | **D** (`At` pinned) | **D** | **D** (no CDC count) | **D** (`At` excluded; version=genesis) | **D** | **D** | **D** |
| **Decision** | P (applied, no rationale) | — | **Y** (refusals, unwired) | P | **Y** AppliedTransforms (axis, not outcome) | **Y** (the channel) | **D** | **D** |

*Time is wholly dark across emission space.* Decision is observable but the manifest records the overlay *axis*,
not the *outcome/evidence* (`AppliedTransforms : (SsKey * OverlayAxis option)` discards the `TransformKind`
payload `LineageEvent` carried). The differential artifacts (refactorlog, ALTER lens) are computed-but-unwired.

### 2.2 ∂κ/∂(episode) — the per-concern temporal carrier (today)

| κ | temporal carrier | observable across episodes? |
|---|---|---|
| **Identity** | `V2.SsKey` ext-prop round-trip (`SsdtDdlEmitter.fs:566` ↔ `ReadSide.fs:854`) | **partial** — current substrate value only; no chain |
| **Schema** | `CatalogSnapshot.Catalog` in an in-memory `Lifecycle` | **no** — in-memory, tests only; never serialized |
| **Decision** | `VersionedPolicy` + `ApprovalStore` (`ApprovalStore.fs` — the *only* durable across-runs store) | **yes, durably** — but policy approvals only, not linked to schema/data episodes |
| **Data** | the CDC capture log (the §12.6 integral) | **no** — ephemeral test containers; never read back as provenance |
| **Provenance** | `reconstructLatest` (FTC) + refactorlog (time-erased) + Lineage trails | **no** — in-memory; refactorlog time-pinned; trails not persisted |

### 2.3 The manifest as the emission-integral — what it integrates vs what it lacks

The `SsdtManifest` (`ManifestEmitter.fs`) is a faithful integral over **state** (`Coverage`, `PredicateCoverage`,
`DeploymentBatches`) and over the **decision overlay applied to that state** (`AppliedTransforms`,
`PolicyConflicts`, `RegistryDigest`). It is **not** an integral over the **displacement δ** (no per-move counts,
no refactorlog cross-reference, no `‖δ‖`) nor over **time** (`PolicyVersion` is always genesis; `At` excluded for
determinism; tolerance emitted as the static *vocabulary*, not the per-run *residual*). It integrates *what the
emitted model is and which intents shaped it*, **not what moved to get here.**

---

## 3. The dark movements (the unified gap list)

1. **Time / Accumulate is wholly dark** — `Lifecycle` is invoked nowhere in `src/`; no artifact accumulates
   across runs; the refactorlog is time-erased + recomputed; the manifest excludes `At`. P-PROV has **no emitted
   witness** in any wired path.
2. **The schema displacement δ and its norm `‖δ‖` are dark** — refactorlog + ALTER-lens unwired; the engine emits
   target *state*, never *displacement*. No artifact answers "what did this sprint touch" — the exact surface the
   premise's SSIS consumer needs.
3. **The data δ is not even a value** — no `RowDiff`; the data delta is computed *at the substrate* by the
   private MERGE `changeDetectionPredicate` (`ScriptDomBuild.fs:785`); the capture count `k` is in no artifact
   (only the `=0` silence floor is witnessed).
4. **Decision rationale is dark in the deploy artifacts** — CREATE/dacpac *apply* the overlay but carry no *why*;
   the manifest records the *axis*, not the *outcome*.
5. **Identity-derivation reason is partially dark** — `rootOriginal` drops `[derived]` everywhere except JSON's
   `display`; a *reidentified* node's WHY is observable nowhere.
6. **Tolerance is emission-thin** — the manifest emits the static `allKnown` enumeration, not the per-run
   exercised residual `tolerate(A,B)`.
7. **No episode is durable, multi-plane, or co-recorded** — recombination across episodes is unanswerable.

---

## 4. The future state — observing concern movement during multi-episodic recombination

Grounded in existing types (the research's R1–R6), framed as the field of §2. **None is research; each reuses a
proven amino acid.**

- **F1 — Materialize the episode (the point of integration).** Promote `CatalogSnapshot` to a **multi-plane,
  persisted `Episode`**: one `Version` (extended with a boundary-supplied clock + `(environment × release-time)`
  cell — Core stays clock-free, the Pipeline boundary stamps it as `ApprovalRecord.At` already does) pairing the
  schema `Catalog`, the `Profile`, the emitted refactorlog, and the CDC capture handle/count. This is the type
  that makes "Identity in episode i × Data in episode j" *expressible* (both planes co-recorded per episode).
- **F2 — A `LifecycleStore` (the persisted time-integral).** A durable JSON store for `Episode`/`Lifecycle`,
  modeled on `ApprovalStore.fs` (deterministic `Utf8JsonWriter`, T1-stable order, structured failures — the
  codebase's only proven across-runs persistence). Turns `reconstructLatest` from an in-memory fold into the
  **FTC over durable provenance**.
- **F3 — `CatalogDiff.compose` (close the derivative algebra).** Add `compose : CatalogDiff → CatalogDiff →
  CatalogDiff` (the `+`, the cross-episode `δ₁ + δ₂`). Flips A-Lifecycle-4 from Bucket-C to operational; earns
  T13's `⬚ compose`; makes "a move in episode i recombining with episode j" a *fold* rather than a single diff.
- **F4 — The data δ stays substrate-fused; its observable form is the realized CDC series.** *Correction of an
  earlier over-claim (this refinement pass): do **not** build a model-plane `RowDiff` value.* Per the ontology's
  policy (§12.2 there) the data plane's **emission** is the at-target MERGE — the substrate computes the delta at
  apply (the change-detection predicate IS the comparison; the tolerance is applied by the comparable-column
  set). The data δ's **value-for-observability** is the **realized CDC capture series read back** (the post-hoc
  delta), recorded into the change-manifest (F5) — not a pre-computed diff. **Consequence for reification:** the
  data leg is therefore **not** a value-level second consumer of `between`/`apply`; the value-level torsor verbs
  (`Move`/abstract `Delta`/`π`) reify, *if anywhere*, at the **temporal multi-version schema** use (6.H —
  composing `CatalogDiff`s via `compose`), and even there reification stays **concrete** (`CatalogDiff` + a
  measured norm), not a generic `Torsor`. What the data plane *does* reify is the **norm** `‖·‖` — as a
  measurement carrier over the realized delta (the CDC capture count), the data analog of the schema move-count.
  *(This sharpens, not weakens, the schema∥data isomorphism: the moves and the norm are analogous one plane
  apart; the **delta-representation** is not — schema δ is a value, data δ is substrate-fused + CDC-observed.)*
- **F5 — The change-manifest (the emission-integral of δ + the mixed partial).** Make the manifest integrate the
  **displacement**: per-move counts (`‖δ‖` by channel — added/removed/renamed/reshaped; the CDC capture series
  `k`), the refactorlog cross-reference, the per-run tolerance residual, the `AppliedTransforms` *outcome* (not
  just axis), and the episode linkage. A manifest-of-δ at each episode is the **change-manifest series**
  (∂²κ/∂emission∂episode).
- **F6 — Refactorlog against-prior, with real episode time.** Accumulate (read prior `.refactorlog`, dedup by
  `OperationKey`, append) and thread the episode clock into `ChangeDateTime` (today pinned to a constant). The
  schema-plane time-integral, durably episode-ordered (this is `EXECUTION_PLAN.md` 6.F.1, now also the F6 of the
  temporal substrate).

---

## 5. The critical-path weave — what must be done, holding the spine

The activation order (refines `EXECUTION_PLAN.md` §6.G with the morphology findings; full slice specs there and
in the new **6.H — Multi-episodic observability substrate**):

1. **6.F.1 / F6** — refactorlog against-prior + episode time → activates the schema time-integral (Accumulate /
   T13 / A43 provenance). *The only durable-provenance amino acid (`ApprovalStore`) is the template.*
2. **6.F.3-data / F4** — the CDC-aware MERGE over arbitrary deltas + the `‖δ‖=k` canary + reading the realized
   CDC capture series → activates T15 (data isometry, general). The data δ stays substrate-fused (no model-plane
   `RowDiff` value); what reifies here is the **norm** `‖·‖` as a measurement carrier over the CDC series — *not*
   a value-level `between`/`apply` (that is the schema plane's, §12.4 of the algebra).
3. **F5** — the change-manifest → the manifest integrates δ (not just state) + the per-run tolerance residual +
   the `AppliedTransforms` outcome → activates **∂κ/∂emission** observability for the displacement.
4. **6.H / F1+F2+F3** — the `Episode` + `LifecycleStore` + `CatalogDiff.compose` → activates **∂κ/∂episode** and
   the cross-episode recombination *join*. This is the genuinely-new buildable family the research surfaced.
5. **6.D.1** — `migrate A B` composes the wired differential leg (`between → {refactorlog, ALTER/DacFx, CDC-MERGE}
   → transfer → verify`) under P-ORD/P-CH → activates **T16** end-to-end (the master equation green).

**Holding the spine (the load-bearing discipline, grounded in the morphology):**
- **The verbs reify at the second consumer, never on speculation.** The absence of `Move`/`Delta`/`‖·‖`/`π`/
  `Torsor` types is *correct*. The value-level `between`/`apply`/`compose` reify (if at all, and concretely as
  `CatalogDiff`) only at the **temporal multi-version schema** use (6.H), not the data leg (whose δ is
  substrate-fused, §F4). The data plane reifies only the **norm** (over the CDC series). **Refuse the speculative
  torsor refactor** (renaming `between`→`⊖`, a `Torsor` typeclass) — the algebra is the spec the witnesses
  check, not a shape to force the code into.
- **Persist, don't compute-anew.** The temporal gap is *persistence*, not algebra — the FTC is proven. Reuse
  `ApprovalStore` as the template; do not invent a parallel store.
- **Per-slice rent unchanged:** the discriminating witness named to its matrix substring; the `AxiomTests.fs`
  theorem entry flipped (T13/T15 from latent-citation to durable-witness; A-Lifecycle-4 Skip→Fact when `compose`
  lands); the matrix regenerated; a `DECISIONS` cash-out; A18 / pure-Core / writer-fidelity / pillar-9 held; the
  premise re-prioritization (PROD-gates deferred) held.
- **Each slice closes one named residual of one equation.** If a slice doesn't move a term of T13/T14/T15/T16/
  A43 — or light a dark cell of the §2 field — question whether it is on the path.

---

## 6. Novel inferences fed back into the calculus (`WAVE_6_ALGEBRA.md` §12)

This research sharpened three things now folded into the algebra:
1. **The concern-movement field** (§2 here): concern-movement is a 2-D partial-derivative field over
   emission-space × episode-time; the manifest is the emission-integral, the `LifecycleStore` the time-integral,
   the change-manifest series the mixed partial. → `WAVE_6_ALGEBRA.md` §12.
2. **Latent vs activated** (§0 here): a law is *latent* (proven in isolation) until its operations are wired and
   its substrate persisted; *activation* closes the residual. The calculus's status table is re-read through
   "latent" — T13/T15 are latent (no durable substrate / no norm carrier), not merely "⬚ trigger." → algebra §9
   status sharpened; AXIOMS T13/T15 status notes amended.
3. **The noun/verb reification principle** (§1.2 here): carriers reify eagerly; operator-verbs (`Move`/`Delta`/
   `‖·‖`/`π`/`Torsor`) reify at the second consumer — which is the **temporal multi-version schema** use (6.H),
   *not* the data leg (whose δ is substrate-fused; §F4 corrects the earlier over-claim). → algebra §12.3–§12.4 +
   the §6.G spine rule, grounded.

— Recorded for the receiving agent. The ontology is the interpretation; the algebra is the equation; **this is
the territory** — the amino acids that exist, the proteins that don't yet, and the field of concern-movement that
is mostly dark. The route that lights it is `EXECUTION_PLAN.md` §6.G + 6.H; the bullseye is `NORTH_STAR.md` L3.
