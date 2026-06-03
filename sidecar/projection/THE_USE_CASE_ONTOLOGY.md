# THE USE-CASE ONTOLOGY — The Masterwork Matrix

> **What this is.** The single, complete map of *what we are building toward* — the engine's ideal
> end state in full complexity, captured as a matrix of **amino acids** (the atomic moves of
> change-over-time) folded into **proteins** (the operator's real workflows), crossed by every axis
> the problem genuinely has. It is **target-first**: it describes the bullseye, not the present
> build. (A later pass maps current code onto this matrix to expose the ⬚ gaps; that pass is not
> here. Wherever this document says "the engine does X," read "the end-state engine does X.")
>
> **The promise.** An agent who reads only this should close it and know everything they need about
> the end state to execute: the verb alphabet, the flows those verbs compose into, the physical
> SQL-Server/DacFx mechanic of every move, the substrate's hard limits, the gate that refuses first,
> the artifact emitted, the proof that measures it, the failure it prevents, and — above all — the
> *discriminating predicate* that separates a right-by-function implementation from a
> plausible-but-wrong one. This is the central place. Every other north-star-ish document in the
> corpus is now **provenance**, indexed in §6; this is the **index**.
>
> **Discipline.** Per the corpus's own rule (`NORTH_STAR.md`): every claim reduces to a checkable
> predicate or a named design fork. Where the target genuinely forks (the declared-loss gate's
> granular-vs-coarse impedance against DacFx's global levers; the two data-comparison regimes; the
> eject's append-forever-vs-collapsible decision), the intricacy is **held in the cell**, not
> smoothed away.

---

## Table of contents

1. [The thesis](#1--the-thesis)
2. [The amino-acid alphabet](#2--the-amino-acid-alphabet)
3. [The protein catalog](#3--the-protein-catalog)
4. [The master matrix](#4--the-master-matrix)
5. [The laws and invariants](#5--the-laws-and-invariants)
6. [Glossary and corpus map](#6--glossary-and-corpus-map)
7. [The completeness checklist](#7--the-completeness-checklist)

---

## 1 — The thesis

**This engine is a publication-and-provenance engine for an evolving relational model that
terminates at an eject — not a live-PROD deployment engine.** It takes a logical model that changes
over time (sourced today from an OutSystems estate), and on each sprint's demand it *publishes* that
model's new state to on-prem SQL Server databases and to an external SSIS team that maps a legacy
database against the schema, while *accumulating* an exact, replayable provenance of every change —
so that at the terminal **eject**, when the model stops being OutSystems-derived and becomes the
operator's own forever, the published schema and its complete history are provably, exactly right
with no upstream left to re-derive them from. Its formal soul is one adjunction —
`Ingest ∘ Project = identity` modulo *named, closed* erasures — lifted from states to the
displacements *between* states: a change computed in the Model and realized on the Substrate lands
at the same point, in both directions, on every plane (schema · data · identity · provenance),
across time, with the engine proving this about *itself* continuously. Minimality is not aesthetic:
it is measured. On the schema plane, DacFx computes the minimal `ALTER` from the engine's declarative
`CREATE`+`refactorlog`; on the data plane — which has no such crutch — the engine computes the
minimal row delta itself and **CDC is the ruler** (`capture-row count = |true delta|`), so "minimum
viable touches" is a checkable equation, not a slogan. The engine never loses anything in silence:
every erasure is a `Tolerance` entry, a structured diagnostic, or a fail-loud refusal, because a
**silent erasure is strictly worse than no claim** — it manufactures the illusion of fidelity.

If you have read this paragraph and the seven sections below, you know the end state: **you now know
all you need.**

---

## 2 — The amino-acid alphabet

The closed, deduplicated set of atomic operations of change-over-time. Two families: the **structural
moves** (the generators of a `Delta` — *what* changes, organized by the plane it acts on) and the
**cross-cutting operational verbs** (the plane-independent atoms — *how* a change is observed,
classified, realized, proven, and remembered). Every protein in §3 is a folded chain drawn entirely
from this alphabet; every cell in §4 is one of these verbs at one (context).

### 2.1 Structural moves — the generators of change

Each generator is **invertible** in the abstract groupoid (`Add⁻¹ = Remove`, `Rename⁻¹ =
reverse-Rename`, `Insert⁻¹ = Delete`, …) even though its *emission* is partial (destructive
generators are refused; narrowing realizations warn). The schema and data legs are **the same shape
of change one plane apart**: Add ∥ Insert, Remove ∥ Delete, Reshape ∥ Update, (nothing) ∥ Unchanged.

| # | Move | Plane | Essence (one line) |
|---|---|---|---|
| 1 | **Add** | schema | A kind or attribute appears; a new Identity (`SsKey`) is minted. |
| 2 | **Remove** | schema | A kind or attribute disappears; an Identity is annihilated (destructive — refused unless declared). |
| 3 | **Rename** | schema | For a stable Identity, the Designation changes; pages are untouched (`sp_rename`, never DROP+ADD). |
| 4 | **Reshape** | schema | A facet (type / length / nullability / precision / default / collation) changes while Identity and Designation hold. |
| 5 | **Insert** | data | A row exists in the candidate but not the substrate; a new row is minted. |
| 6 | **Update** | data | A row exists in both with at least one *semantically* different column; Identity conserved, facets change. |
| 7 | **Unchanged** | data | A row is identical on every tracked, non-tolerated column; **no clause fires** — the zero displacement (`‖δ‖=0`). |
| 8 | **Delete** | data | A row exists in the substrate but not the candidate; the row is annihilated (destructive — scope-gated). |
| 9 | **Reidentify** | identity (cross-plane) | The same logical entity carries different surrogates per substrate; the correspondence is reconstructed (match by business key, re-point FKs). |
| 10 | **Move** | cross-substrate | Rows flow from a source substrate to a sink, identity-reconciled, minimality CDC-measured; the data-plane projection of the whole migration. |
| 11 | **Accumulate** | provenance / time | History grows: append a snapshot + append (deduped) to the refactorlog; the chain replays to reconstruct any state. |

> **Note on dedup.** *Reidentify* is genuinely cross-plane (it lives wherever surrogates differ
> across substrates — schema-context for the contract, data-context for the rows). *Move* is the
> composite that carries data across a substrate boundary and may contain Insert/Update/Unchanged/
> Delete/Reidentify within it. *Unchanged* is the algebraic identity element (the change-detection
> predicate must produce it; it is not "no move" but the *recognized* zero move). These three are
> why the alphabet is 11, not a naive 7+5.

### 2.2 Cross-cutting operational verbs — the plane-independent atoms

These are atomic too: each is an irreducible operation the engine performs *about* a change, on any
plane. A protein chain interleaves them with the structural moves.

| Verb | Essence (one line) |
|---|---|
| **Snapshot** | Capture a State at one coordinate — read the model (`Catalog`+`Profile`) or read the deployed substrate (`PhysicalSchema`) into a comparable value. |
| **Diff** | Compute the observational displacement `between A B` — identity-first, modulus-bounded; asserts *what* differs, not *how* to get there. |
| **Declare(-loss)** | The operator explicitly accepts an irreversible loss (a drop, a scoped delete, a narrowing) before it may be emitted; the default is refusal. |
| **Gate** | A pre-flight that refuses *first* — checks a precondition (connection, permission, data-compat, CDC, scope) before any mutation, so failure never happens mid-execution. |
| **Measure** | Read the norm `‖δ‖` — the CDC capture-row count (data) or the per-channel move count (schema) — to prove the touch was minimal. |
| **Publish** | Emit the deploy/consumer artifact (declarative `CREATE` + `refactorlog` + pre/post scripts → DacFx; or the consumer changelog) and hand it to its terminus. |
| **Record** | Write the run as a durable `Episode` into the `LifecycleStore` — the keystone that gives the next sprint's Diff a valid prior. |
| **Verify** | Read the result back and assert `Ingest ∘ Project = id` modulo named tolerances (the canary: `PhysicalSchema` diff, `B'` reproduces `B`). |
| **Reconcile** | Discover the identity correspondence across substrates by a named rule (match users by email; map source→sink surrogates) — produces the mapping Reidentify realizes. |
| **Tolerate** | Classify an observed difference as substrate-noise (DacFx auto-names, empty-string↔NULL, ANSI-padding, decimal scale, collation) and *name* it, so it is neither emitted nor silently dropped. |

### 2.3 The two near-misses the alphabet exists to foreclose

A name is a promise about a function; testing only the behavior a name *suggests* lets a wrong
implementation that *shares the name's surface* pass. The alphabet is defined right-by-function so
these cannot hide:

- **`applyDiff` ("apply the diff to get the target").** A name-driven body `applyDiff base d =
  target d` ignores `base` and the happy-path round-trip `apply(between(A,B), A) = B` passes
  *trivially*. Right by name; wrong by function. Caught only by the no-cheat predicate (§5, P-RT₂).
- **"physical column rename" ("the column's physical name changed").** Detected on the physical
  name-space, it misses every logical rename in the steady-state regime, where the substrate only
  ever sees the *Designation* (§5, P-RN). Right by name; wrong by function.

---

## 3 — The protein catalog

Every real operator workflow, as a folded chain of §2 amino acids. For each: trigger · cadence ·
consumer/terminus · environment-lattice cells · deploy mode · the ordered chain · faithfulness
stakes. The estate is a lattice of **(environment × release-time)** cells — DEV, TEST/QA, UAT, PROD
— each the *same* `SsKey`-stable estate at a different rhythm-point; two states the engine compares
are two cells of this lattice. Cadence is **once a sprint, demand-driven by the consuming team** —
lumpy and batched, not high-frequency. PROD carries no application/data yet, so PROD-blast-radius
reasoning (atomicity, permission gates) is *premature, not central* — represented in the alphabet,
deferred in priority.

### Protein index

| # | Protein | Trigger | Cadence | Terminus | Deploy mode |
|---|---|---|---|---|---|
| P-1 | **Dev-cloud → Dev on-prem load** | Dev model advanced / new env | once/sprint | on-prem Dev SQL Server | declarative + data load |
| P-2 | **QA-cloud → QA on-prem load** | QA refresh after release | once/sprint | on-prem QA SQL Server | declarative + data load |
| P-3 | **Dev → UAT with user re-key** (forward-only, no masking) | UAT sprint refresh | once/sprint | on-prem UAT SQL Server | fresh load (pre-transformed) |
| P-4 | **SSIS-consumer schema-evolution publication** | sprint completion | once/sprint | external SSIS team | publication only |
| P-5 | **Idempotent redeploy** (zero-touch, CDC-silent) | CI re-trigger / retry | any time | any on-prem target | declarative; no net change |
| P-6 | **In-place schema evolution** of a deployed DB | sprint advance (`migrate`) | once/sprint/env | any on-prem target | imperative in-place |
| P-7 | **Terminal eject / freeze** | business decision | once, ever | operator + legacy team | publication only |
| P-8 | **Drift detection / remediation** | scheduled / on-demand | continuous | operator / DBA | read-only (optional remediate) |
| P-9 | **Self-check canary** | pre-commit / CI | per-commit | the engine / CI | ephemeral DacFx |

### P-1 · Dev-cloud → Dev on-prem load

- **Essence.** Full extract of the Dev cloud DB (schema + seed data) published to the on-prem Dev
  target; the canonical source-of-truth load for the development tier.
- **Trigger / Cadence.** Operator-initiated when the sprint's Dev model warrants a fresh on-prem
  baseline (or a new env needs seeding); once per sprint.
- **Terminus / demand.** On-prem Dev SQL Server: schema structurally correct; seed data
  topologically loaded without FK violation; CDC-tracked tables accumulate **zero** spurious
  captures on redeploy.
- **Lattice cells.** OutSystems Dev cloud (source) × on-prem Dev (sink).
- **Deploy mode.** Declarative SSDT+DacFx+Octopus for schema; CDC-aware MERGE (or fresh load) for
  data.
- **Chain (ordered).**
  `Snapshot` (read cloud → Catalog+Profile) → `Diff` (vs prior episode) → `Gate` (declared-loss:
  refuse Removed) → `Rename` (refactorlog append-dedup) → `Reshape` (DacFx ALTER) → `Add`
  (CREATE/ADD) → `Publish` (SSDT bundle → DacFx) → `Insert`/`Update`/`Unchanged` (CDC-aware MERGE,
  FK-topological) → `Measure` (CDC count = |delta|; 0 on redeploy) → `Verify` (PhysicalSchema diff
  empty) → `Record` (durable Episode).
- **Faithfulness stakes.** *Must hold:* CDC silence on idempotent redeploy (P-DM floor); schema
  structural identity; FK ordering (P-ORD-DATA); refactorlog append-and-dedup (P-PROV). *Declared
  losses:* FK-trust flag, computed-column expressions, DacFx auto-named constraints — each a named
  `Tolerate`, none silent.

### P-2 · QA-cloud → QA on-prem load

- **Essence.** Structurally identical to P-1 at the QA tier; the QA model may lag Dev by a release —
  the same logical estate at a different lattice cell.
- **Trigger / Cadence.** QA refresh after a release cycle; once per sprint, independent schedule.
- **Terminus / demand.** On-prem QA SQL Server; same demands as P-1; QA data is non-sensitive test
  data (no masking).
- **Lattice cells.** OutSystems QA cloud × on-prem QA.
- **Deploy mode.** Identical to P-1.
- **Chain.** Identical to P-1; the only structural difference is the `EpisodeCoordinate`'s
  environment, so the QA refactorlog accumulates independently. **`Record` is load-bearing for P-3**:
  the QA episode is P-3's data source and must be clean and durable before UAT promotion begins.
- **Faithfulness stakes.** As P-1; no masking gate (data non-sensitive).

### P-3 · Dev → UAT with user re-key (forward-only)

- **Essence.** Promote Dev schema + QA data to UAT, re-keying every User FK
  (CreatedBy/UpdatedBy/any `dbo.User` reference) from QA/Dev user identities to UAT's. Forward-only;
  no masking.
- **Trigger / Cadence.** UAT sprint refresh; operator supplies QA+UAT user inventories and a
  user-map (or the matcher auto-proposes); once per sprint.
- **Terminus / demand.** On-prem UAT SQL Server: every `dbo.User` FK holds a valid UAT identity (no
  QA orphan); the mapping is provably complete **before** SQL is emitted; the load is idempotent.
- **Lattice cells.** Dev on-prem (schema source) × QA on-prem (data source) × UAT on-prem (sink) —
  three cells.
- **Deploy mode.** Declarative schema; fresh load of pre-transformed INSERTs for data.
- **Chain.**
  `Snapshot` (Dev model + QA User profile) → `Diff` (UAT-prior vs Dev-current; forward-only) →
  `Gate` (schema-compat **and** `validate-user-map`: every orphan QA UserId mapped, every target in
  the UAT allow-list — halts before any SQL) → `Rename` → `Reshape`/`Add` → `Reconcile` (discover
  QA→UAT user correspondence by rule: ByEmail / BySsKey / ManualOverride / FallbackToSystemUser) →
  `Reidentify` (re-point User FKs to UAT identities) → `Publish` → `Insert` (pre-transformed,
  FK-topological) → `Measure` → `Verify` (row+null counts; matching-report audit) → `Record`.
- **Faithfulness stakes.** *Must hold:* every User FK resolves to a valid UAT user (P-REKEY); a
  re-keyed row is an **Update** (relationship preserved modulo surrogate), never Delete+Insert; the
  MERGE `ON` clause keys on the **business key (email)**, never the raw surrogate; `validate-user-map`
  gates before emission; schema-compat (no tightening that breaks the data load — the Decision↔Data
  coupling, §5 T-V). *Declared loss:* unmapped orphans fail loud unless `FallbackToSystemUser` is
  explicitly declared; the matching report is the declared-loss surface.

### P-4 · SSIS-consumer schema-evolution publication

- **Essence.** The external SSIS team maps a legacy DB against this schema and must track the model's
  evolution to keep their mappings current. The engine publishes a per-sprint changelog (schema delta
  + refactorlog + optional narrative) they consume to update their mappings.
- **Trigger / Cadence.** End of each sprint (or on-demand when the model advances); the team needs
  the delta *since their last mapping*, not just the current state.
- **Terminus / demand.** The SSIS team: (a) a human-readable per-sprint changelog (what changed, in
  what direction); (b) a machine-readable delta (refactorlog for renames, full state for new/changed
  objects); (c) provenance — which sprint a delta corresponds to, and the ability to reconstruct any
  prior state. The eject (P-7) is this chain's endpoint, so every sprint must accumulate faithfully
  toward it.
- **Lattice cells.** Schema-only; environment-invariant (no DB-target dependency).
- **Deploy mode.** Publication only (no DB deploy). Artifact = SSDT CREATE bundle + `.refactorlog` +
  `ChangeManifest`.
- **Chain.**
  `Snapshot` (current Catalog) → `Diff` (vs prior episode loaded from `LifecycleStore`) →
  `Declare(-loss)` (every Removed surfaced as an explicit drop) → `Rename` (refactorlog entries — the
  SSIS team cannot infer a rename from CREATE files; the refactorlog **is** the rename signal) →
  `Reshape` (ALTER-lens preview = the human-readable shape diff) → `Add` → `Accumulate` (append +
  dedup refactorlog by `OperationKey`; thread real episode time; `reconstructLatest` from genesis
  still reproduces current state) → `Publish` (CREATE bundle + accumulated refactorlog +
  ChangeManifest + derived changelog) → `Record` (durable episode — the SSIS team's next mapping
  starts from this Diff).
- **Faithfulness stakes.** *Must hold:* the refactorlog never loses or duplicates entries (P-PROV);
  `reconstructLatest` from genesis reproduces current state; renames route through the refactorlog
  (never DROP+ADD, which would break SSIS mappings); every removal is declared-loss. *Open design
  fork (named):* the **provenance-rendering decision** — dacpac / human changelog / machine diff /
  all three (§5, Decision-owed #2). *Ordering:* `Record` last — a half-published episode recorded
  breaks the next sprint's Diff chain.

### P-5 · Idempotent redeploy (zero-touch, CDC-silent)

- **Essence.** The same model version re-deployed to an already-correct target produces **zero** CDC
  captures and **zero** schema ALTERs. The engine's highest-stakes single guarantee.
- **Trigger / Cadence.** A pipeline re-run with no model change (CI re-trigger, Octopus retry,
  operator re-run); any time, potentially many times per sprint per environment.
- **Terminus / demand.** Any on-prem target with a prior deploy: zero spurious CDC rows (CDC-dependent
  features depend on this).
- **Deploy mode.** Declarative (DacFx detects no difference → no ALTERs); CDC-aware MERGE (predicate
  fires zero UPDATEs on byte-identical rows).
- **Chain.**
  `Snapshot` → `Diff` (empty over *every* declared facet — P-CMP: `isEmpty(between A A)`) → `Gate`
  (intent filter: classify each observed difference) → `Tolerate` (every substrate-noise difference
  is a *named* tolerance — the canary asserts equality *modulo named tolerances*, not raw equality) →
  `Publish` (DacFx computes zero ALTERs) → `Unchanged` (MERGE predicate false for every row → zero
  UPDATEs) → `Measure` (CDC count = 0; P-DM floor) → `Verify` (PhysicalSchema diff empty).
- **Faithfulness stakes.** *Must hold:* P-DM floor (zero CDC); zero schema ALTERs; the intent filter
  is **complete** (P-IF) — every substrate-noise difference is a named tolerance, or a single leak
  fires a spurious ALTER. The **change-detection predicate** is the discriminating axis: a naive
  unconditional `WHEN MATCHED` captures every matched row even on identical content.

### P-6 · In-place schema evolution of a deployed DB

- **Essence.** The model advanced; the target holds live data. Apply the minimum-viable schema
  changes (ALTER not DROP+CREATE; `sp_rename` not DROP+ADD) and minimum-viable data movements to
  carry the target from state A to state B, preserving all data, CDC-silent on unchanged rows. The
  one-command **`migrate A B`** operation — the L3 composition that exercises every plane at once.
- **Trigger / Cadence.** A new sprint's model differs from the deployed state; `projection migrate
  --from <A-episode> --to <B>`; once per sprint per environment (Dev → QA → UAT; PROD empty).
- **Terminus / demand.** The same target, advanced to B: all data preserved through schema changes;
  CDC count = |true row delta| (not 2×|table|); idempotent (re-running A→B when already at B = P-5
  behavior); a durable episode recorded.
- **Deploy mode.** Imperative in-place executor (`MigrationRun.execute`): `sp_rename` + logical
  re-bind, `ALTER`, `ADD` — never DROP+CREATE; data via CDC-aware MERGE (minimum diff, not
  TRUNCATE+reload).
- **Chain.**
  `Snapshot` (source: read deployed A via ReadSide → PhysicalSchema) → `Snapshot` (target model B) →
  `Gate` (connection + permission pre-flights) → `Diff` (`plan A B = emit(B ⊖ A)`; partition into
  {Rename, Reshape, Add, Remove}; P-CH disjoint) → `Gate` (declared-loss: refuse Remove fail-loud
  before touching the DB; data-compat: refuse tightening the existing data violates) → `Rename`
  (`sp_rename` + `V2.LogicalName` re-bind; precedes Reshape and data) → `Reshape` (ALTER COLUMN) →
  `Add` (CREATE/ADD) → `Move` (CDC-aware MERGE: Insert/Update/Unchanged/scoped-Delete; two-phase for
  FK cycles; FK-topological) → `Measure` (CDC = |true row delta|) → `Verify`
  (`PhysicalSchema.isSchemaEqual`: B' reproduces B; seeded row survives) → `Record` (FTC:
  `reconstructLatest` from genesis reproduces B).
- **Faithfulness stakes.** *Must hold:* data preservation through schema change (rename preserves
  pages — L0 fact); CDC data-minimality (P-DM general case); refactorlog ⊥ ALTER channel (P-CH); FK
  two-phase ordering; idempotency. *Declared loss:* any narrowing/NOT-NULL tightening the existing
  data would violate is **gated, never attempted silently**.

### P-7 · Terminal eject / freeze

- **Essence.** The model stops being OutSystems-derived; OutSystems will no longer regenerate it. The
  frozen schema and its complete provenance are handed to the operator + legacy team as a
  self-contained, permanently frozen artifact. After the eject there is **no upstream** — the
  provenance must be exactly right *forever*.
- **Trigger / Cadence.** A business decision to exit OutSystems for this schema; once, ever;
  irreversible.
- **Terminus / demand.** The operator + SSIS/legacy team: (a) the frozen SSDT CREATE files; (b) the
  complete accumulated refactorlog (every rename since genesis); (c) the episode chain from genesis
  to freeze (`LifecycleStore`) so any future team can reconstruct any prior state; (d) explicit
  markers that the schema is operator-owned and OutSystems-independent.
- **Deploy mode.** Publication only (the last in-place evolution already landed); the eject is a
  *regime change*, signed.
- **Chain.**
  `Snapshot` (final/terminal Catalog) → `Diff` (genesis vs frozen: the total accumulated delta, an
  optional "what changed across all history" view) → `Accumulate` (confirm the refactorlog chain is
  complete, deduped, episode-timestamped; `reconstructLatest` from genesis reproduces the frozen
  state) → `Declare(-loss)` (any eject-time drops declared with rationale in the terminal
  ChangeManifest) → `Gate` (provenance-completeness: verify the full chain reconstructs before
  publication — the last chance to catch a broken chain) → `Publish` (terminal package: frozen
  bundle + complete refactorlog + LifecycleStore export + terminal ChangeManifest + the
  operator-owned provenance declaration) → `Record` (final episode; chain closed) → `Verify`
  (`reconstructLatest` from genesis = terminal state).
- **Faithfulness stakes.** **P-PROV is the load-bearing law** — append-only, never-deleted,
  episode-timestamped, replay-correct from genesis; this cannot be partially right. *Open design
  fork (named):* the **eject-deliverable decision** — *append-forever* (the legacy team owns the
  whole provenance chain) vs *collapsible at freeze* (the frozen state alone). The discriminating
  condition: **does any downstream system ever DacFx-publish against an *intermediate* (pre-freeze)
  state?** If yes, append-forever is required (the intermediate refactorlog entries are the only
  guard against DROP+ADD on a previously-renamed column); if every consumer takes the frozen schema
  as a blank-sheet deploy, collapsible is safe (§5, Decision-owed #3).

### P-8 · Drift detection / remediation

- **Essence.** Read the deployed DB back and compare to the model; surface any divergence (a column
  altered directly, a table added out-of-band, an index drifted) as a structured diagnostic. This is
  the adjunction's *failure* surfaced: `Ingest(deployed) ≠ Model` is a diff.
- **Trigger / Cadence.** Scheduled (CI cron) or operator query "is what's deployed what the model
  says?"; continuous, not sprint-cadenced.
- **Terminus / demand.** The operator/DBA: a structured diagnostic of every divergence, classified as
  operator-intended or unintended drift; optionally a `CatalogDiff`-based remediation script.
- **Deploy mode.** Read-only (remediation is a separate downstream act, reusing P-6's chain).
- **Chain.**
  `Snapshot` (deployed → PhysicalSchema) → `Snapshot` (expected model from latest episode) → `Diff`
  → `Tolerate` (filter substrate-noise; remaining = true drift) → `Declare(-loss)` (each true drift
  a structured diagnostic, classified model-ahead-of-deployed vs deployed-ahead-of-model — the
  dangerous class) → `Gate` (operator decision: accept / remediate / escalate — human in the loop) →
  `Publish` (optional remediation script over the `CatalogDiff`) → `Measure` (drift size = `‖deployed
  ⊖ expected‖`, per channel).
- **Faithfulness stakes.** The intent filter (P-IF) must be complete — every divergence classified,
  none silent (a silent divergence is a coverage hole); the tolerance set must be curated, or
  false-positive alarms suppress operator attention.

### P-9 · Self-check canary

- **Essence.** Deploy the emitted model to an ephemeral SQL Server, read it back, assert structural
  equality — `Ingest ∘ Project = id` against a live substrate. The engine's continuous proof about
  itself (the keystone of *trust*: a guarantee about the guarantees).
- **Trigger / Cadence.** Pre-commit hook / CI gate; per-commit; the fastest meaningful feedback loop.
- **Terminus / demand.** The CI system / the engine: green = the emitted artifact deploys cleanly and
  read-back reproduces the model modulo named tolerances; red = an emission or readback bug.
- **Deploy mode.** Ephemeral DacFx deploy (fast-lane); not a production deploy.
- **Chain.**
  `Snapshot` (source Catalog) → `Publish` (emit → deploy to ephemeral) → `Snapshot` (read-back
  PhysicalSchema) → `Diff` (round-trip residual) → `Tolerate` (named erasures absorbed; remainder =
  genuine faithfulness gap) → `Gate` (`isEmpty(residual)` else red) → `Measure` (CDC-silence check:
  idempotent redeploy → 0 captures) → `Verify` (the A→B migration canary: B' reproduces B, seeded
  row survives, re-run idempotent).
- **Faithfulness stakes.** This protein *is* the engine's proof about itself; its failure **is** a
  faithfulness gap. Each tolerance is a declared erasure; each non-tolerance residual is an unfaithful
  cell that must close to advance the ladder L1→L2. The discriminating predicates for every axis live
  here: P-CMP (is the diff truly empty?), P-CH (rename ⊥ ALTER?), P-DM (CDC counts correct?).

### Cross-protein ordering constraints (hold for all proteins)

1. **Rename before Reshape** — the ALTER uses the renamed object's new name; `sp_rename` fires first.
2. **Schema before data** — DDL flushes before data scripts; no INSERT into a not-yet-existing column.
3. **Create before Insert** — a table/column must exist before rows load into it.
4. **FK two-phase** — phase-1 insert with NULLed deferred FKs; phase-2 re-point — prevents mid-load
   FK violation on cyclic graphs.
5. **Gate before mutation** — declared-loss + data-compat + connection + permission gates fire before
   any live-DB write; an upfront refusal always beats a mid-execution failure.
6. **Record last** — the episode is recorded only after the artifact is verified complete; a
   recorded-but-broken episode corrupts the next sprint's Diff chain.
7. **Accumulate (refactorlog) before Publish** — the appended-and-deduped refactorlog is the
   publication input; the chain must be internally consistent before the bundle emits.

---

## 4 — The master matrix

The centerpiece: **amino acids × proteins, deepened by the cross-cutting axes.** A cell is not a
checkmark; it is a structured record. §4.1 defines the cell schema and the ten axes; §4.2–§4.4 give
the deep per-move cells (schema, data, cross-cutting); §4.5 crosses moves against the deploy-mode
axis; §4.6 is the gate × failure-mode matrix; §4.7 is the protein × amino-acid incidence.

### 4.1 The cell schema and the ten axes

**Every meaningful (move × context) cell answers nine fields:**

| Field | What it records |
|---|---|
| **Physical realization** | The actual SQL-Server/DacFx mechanic — pages, locks, `sp_rename`, `ALTER`, MERGE clauses, capture instances. |
| **Substrate constraint** | What the engine forbids / what the engine *cannot* do online (the hard limit). |
| **Faithfulness class** | `faithful` / `lossy-with-warning` / `refuse-unless-declared`. |
| **The gate** | The pre-flight that refuses first. |
| **Artifact emitted** | `CREATE` / refactorlog entry / pre- or post-deploy script / dacpac / publish-profile decision / MERGE / Episode. |
| **Measurement / proof** | How minimality and fidelity are observed — CDC capture count, PhysicalSchema diff, refactorlog replay, canary equality-modulo-tolerance. |
| **Failure mode prevented** | The specific way the plan would fail or lose, that this cell forecloses. |
| **Discriminating predicate** | The input on which a plausible-but-wrong implementation diverges — right-by-function, never by name. |
| **Planes touched** | schema / data / identity / provenance — so a move is placed on every plane it crosses. |

**The ten axes the matrix crosses simultaneously** (the full dimensionality — the reason every prior
thread only got partial):

1. **Move** — the amino acid (§2).
2. **Plane** — schema / data / identity / provenance. (Schema and data are the same shape one plane
   apart; identity and provenance are the other two.)
3. **Deploy mode** — declarative (SSDT CREATE + refactorlog + pre/post → DacFx computes the delta →
   Octopus publishes) · imperative in-place executor (the lens/dev path) · fresh wipe-and-load
   (always-correct fallback).
4. **Temporal phase** — genesis → steady-state (snapshot N → N+1) → eject/freeze. The
   snapshot-per-emission discipline and the diff-between-two-snapshots are load-bearing.
5. **Comparison regime** — the diff basis: prior-snapshot · current target-reality · two authored
   models (operator's discretion; §5 names when each applies).
6. **Environment-lattice cell** — (DEV/TEST/UAT/PROD) × (OutSystems cloud source / on-prem target),
   each the same `SsKey`-stable estate at a different rhythm-point.
7. **Consumer / terminus** — the SSIS schema-evolution consumer · the own on-prem DBs · the eject.
   What each demands of the artifact.
8. **Ordering / sequencing** — the dependency order the plan must respect (rename before reshape;
   create before insert; schema-publish before data-load; FK two-phase).
9. **Gate / safety set** — declared-loss · delete-scope · CDC-tracking · permission/connection ·
   CDC-silence-on-idempotence · possible-data-loss · data-compat · transactional/resumable. The
   completeness law: every way a plan can fail or lose has a pre-flight that refuses first.
10. **Measurement & proof** — how minimality and fidelity are observed and enforced (CDC as the
    ruler; the canary; the refactorlog as append-only provenance that replays).

**The DacFx-delegation seam (axis 3, made precise).** Emission splits at *who computes the plan*:

- **Schema plan → delegated.** The engine emits the *declarative* artifact (adjusted `CREATE TABLE` +
  the refactorlog); **DacFx computes the schema ALTER at publish.** The imperative schema ALTER
  (`SchemaMigrationEmitter`) is **not** the deploy artifact — it is a *lens*: a preview (what this
  sprint touches, for the PR and the SSIS team), a verification check (does DacFx's plan match the
  engine's prediction?), and an input to data-script generation and CDC measurement.
- **Data plan → engine-owned.** DacFx does not move data; the engine emits the minimal data movement
  as pre/post-deployment scripts, measured by CDC. *This is the engine's hardest-owned correctness*:
  schema leans on DacFx; the data plane has no crutch.

**The two emission modes, both first-class:**
- **Incremental** (the target mode) — adjusted `CREATE TABLE` + refactorlog (appended-and-deduped
  against the prior) + pre/post data scripts (CDC-aware MERGE). Minimal, CDC-measured, **isometric**
  (`‖emit(δ)‖ = ‖δ‖`).
- **Fresh wipe-and-load** (the safety baseline) — drop all + load schema + bulk-load data. Always
  correct, never minimal: captures `2×|table|` CDC rows — *norm-inflating* — which is the precise
  algebraic reason it is the fallback, not the default.

### 4.2 Schema-plane moves — the deep cells

#### Add
- **Physical realization.** Table: `CREATE TABLE` (new empty pages). Column: `ALTER TABLE … ADD`
  (metadata-only if nullable, or NOT NULL + runtime-constant DEFAULT on modern SQL Server). The
  engine emits declarative `CREATE`; DacFx issues the `ADD`.
- **Substrate constraint.** Additive, safe (brief SCH-M only). NOT NULL with no DEFAULT on a
  populated table fails — IDENTITY cannot be *added* to an existing column.
- **Faithfulness class.** Faithful (existing rows get NULL or the declared default).
- **Gate.** Data-compat (NOT NULL + no DEFAULT + rows) → `BlockOnPossibleDataLoss`.
- **Artifact.** Declarative `CREATE`/adjusted DDL → DacFx; no refactorlog entry; post-deploy seed
  script if static seeds accompany.
- **Measurement / proof.** `CatalogDiff.Added` entry by `SsKey`; post-deploy PhysicalSchema shows the
  object; CDC = 0 for the add itself.
- **Failure prevented.** Spurious DROP+CREATE of an existing object when no diff is needed.
- **Discriminating predicate (P-CMP).** `isEmpty(between A B)` is false iff the element is in B not A
  **by SsKey** — not by Name. Adversarial input: add a kind whose SsKey is absent from A but whose
  Name collides with a removed kind → the diff must report both a Remove and an Add, never a rename or
  silence.
- **Planes.** schema · identity (SsKey created) · data (new table → P-DM `‖δ‖=0`; seeds follow under
  P-ORD).

#### Remove
- **Physical realization.** Table: `DROP TABLE` (deallocates all pages — bytes gone). Column: `ALTER
  TABLE … DROP COLUMN`. DacFx emits it only under `DropObjectsNotInSource`; the engine's policy is to
  **refuse by default**.
- **Substrate constraint.** DROP takes SCH-M (blocks all concurrent access). Computed columns /
  index-referenced columns must drop first (DacFx orders this; the engine must not circumvent).
- **Faithfulness class.** **Refuse-unless-declared** (always destructive).
- **Gate.** Declared-loss gate (`--allow-drops`); the engine refuses the destructive DDL at
  plan/emit time — *before* execution — with a named error; `BlockOnPossibleDataLoss=True` is the
  DacFx-level backstop.
- **Artifact.** On refusal: a structured diagnostic / non-zero exit (never a silent no-op). On
  declared loss: a pre-deploy archive/verify-zero-rows script, then the declarative removal → DacFx
  `DROP`. No refactorlog entry (nothing to track forward).
- **Measurement / proof.** `CatalogDiff.Removed` entry; post-deploy the object is absent; the
  refusal itself is measured (the canary exits non-zero when a declared-Remove fires without the
  override).
- **Failure prevented.** The silent data wipe — most dangerously, a *rename misdetected as Remove+Add*
  (the "naked rename") causing DROP+CREATE.
- **Discriminating predicate.** `SsKey ∈ A ∧ SsKey ∉ B` (by SsKey, not Name) is the Remove condition;
  P-CH requires no element appear in both the Remove and Rename channels.
- **Planes.** schema · identity (SsKey annihilated) · data (a dropped CDC-tracked table captures
  `2×|table|` — the pre-flight must disable CDC before DROP).

#### Rename
- **Physical realization.** `sp_rename 'schema.table','New','OBJECT'` (table) / `sp_rename
  'schema.table.Old','New','COLUMN'` (column) — **metadata-only; data pages untouched**. The engine
  emits a refactorlog entry; DacFx reads it and maps old→new, issuing `sp_rename` not DROP+CREATE.
  The refactorlog *is* the deploy artifact for renames.
- **Substrate constraint.** Brief SCH-M; no page rewrite. The refactorlog entry **must** be present
  (absent → DacFx interprets as Remove+Add → data loss). Entries are append-only, never deleted
  (fresh-environment deploys replay the full history).
- **Faithfulness class.** Faithful — `‖rename‖_data = 0` (A43 cross-plane corollary): `sp_rename`
  induces zero CDC captures; this is the *algebraic proof* the refactorlog is forced, not chosen.
- **Gate (P-RN).** Rename iff, for the same `SsKey`, `deployed.Realization ≠ emitted.Realization` —
  under `Realization := Designation`, `deployed.Name ≠ emitted.Name`. Compares against deployed state
  so an idempotent re-emit does not re-`sp_rename`.
- **Artifact.** Refactorlog entry (XML; `SqlSimpleColumn`/`SqlTable`; GUID; old/new names; parent) +
  adjusted declarative DDL with the new name; **no `ALTER COLUMN`** for the rename itself; deduped
  against the prior committed refactorlog at emit time.
- **Measurement / proof.** The refactorlog entry; "a rename alone emits no ALTER" (the P-CH
  disjointness instance); the `‖rename‖_data=0` canary (deploy a rename → assert zero CDC captures).
- **Failure prevented.** The "naked rename" — editing the `.sql` without a refactorlog entry →
  DacFx DROP+ADD → column data destroyed.
- **Discriminating predicate (P-RN + P-CH).** (1) Detect in the **Designation** name-space the
  substrate uses, not the physical-name space. (2) A rename must **not** double-emit as a shape
  change. Adversarial input: rename a column *while widening it* — the rename appears solely in the
  refactorlog, the width change solely in `AttributeDiffs.Changed`; neither channel sees the other.
- **Planes.** schema · identity (SsKey invariant, Designation changes — the *unique* such move) ·
  provenance (the entry accumulates).

#### Reshape
- **Physical realization.** `ALTER TABLE … ALTER COLUMN`. **Widening** (`VARCHAR(50)→(100)`,
  `INT→BIGINT` metadata path, nullable widening) — **metadata-only** (page-header descriptor update,
  brief SCH-M). **Narrowing / NOT-NULL tightening / on-disk-representation type change** —
  **full-table rewrite**, checked against bytes on disk, **aborts mid-statement on violation**.
  Non-ALTER-able facets — `DEFAULT` (DROP+ADD constraint), `IDENTITY` (table rebuild), PK columns (PK
  drop first), computed columns (DROP+ADD) — DacFx routes these; the engine must not suppress them.
  The engine emits the *declarative* adjusted DDL; DacFx computes the ALTER; `SchemaMigrationEmitter`
  is the **lens** only.
- **Substrate constraint.** Narrowing checks on-disk bytes and aborts mid-statement; NULL→NOT NULL
  requires zero NULLs; `BlockOnPossibleDataLoss=True` blocks truncating narrowings.
- **Faithfulness class.** Faithful (widening) / lossy-with-warning (narrowing) /
  refuse-unless-pre-flighted (NULL→NOT NULL on populated table).
- **Gate (data-compat).** Before any lossy/violating Reshape: `MAX(LEN(col)) ≤ new_length` /
  `COUNT(*) WHERE col IS NULL = 0` / precision-fits — fires **before** DacFx publish, not after.
- **Artifact.** Adjusted declarative `CREATE` (authoritative) → DacFx ALTER; `SchemaMigrationEmitter`
  preview (lens); pre-deploy backfill script if nullability tightening; no refactorlog entry.
- **Measurement / proof.** `CatalogDiff.AttributeDiffs.Changed` entry; "a widening ALTER COLUMN
  executes and preserves data" (T16 sub-square); PhysicalSchema facet matches emitted; CDC = 0 for
  widening, = rows-rewritten for narrowing.
- **Failure prevented.** Silent type mismatch undetected in the diff (P-CMP blind spot); mid-statement
  abort on a live DB; naive ALTER of a non-alterable facet that SQL Server rejects.
- **Discriminating predicate (P-CMP over facets).** `isEmpty(between A B)` false iff at least one
  *declared* facet differs. Adversarial input: change `DECIMAL(10,2)→DECIMAL(18,4)` — a body
  comparing only `DataType` (not Precision/Scale) yields `isEmpty=true` for a real precision change
  (the blind-spot failure). A Reshape must not appear in the refactorlog channel (P-CH).
- **Planes.** schema · data (narrowing/NOT-NULL touch rows — CDC captures them) · decision (a
  NOT-NULL tightening couples to the Data load — the T-V pre-flight).

### 4.3 Data-plane moves — the deep cells

The deploy artifact is a **CDC-aware minimum-diff MERGE** per table (incremental mode). Its
correctness is *not* "it's a MERGE." Four functional parts, each with a wrong-by-name failure:
(a) the **null-safe distinctness** change-detection predicate; (b) the `ON` clause keys on
**reconciled identity, not raw surrogate**; (c) the **comparable-column-set excludes** computed /
surrogate / tolerance columns; (d) the **gated DELETE** clause.

#### Insert
- **Physical realization.** `WHEN NOT MATCHED BY TARGET THEN INSERT (…) VALUES (Source.…)`.
- **Substrate constraint.** FK ordering — a row cannot insert before its parent exists; inserts run
  after parent-table inserts in `DataLoadPlan` topological order.
- **Faithfulness class.** Additive, safe (commutes with Update/Delete — T14).
- **Gate.** FK-order pre-flight (`DataLoadPlan`); CDC-tracking gate if the sink is tracked.
- **Artifact.** Post-deployment MERGE script (StaticSeeds/Migration) or `Transfer`-verb execution.
- **Measurement / proof.** One CDC row per insert (`__$operation=2`); P-DM: a genuine new row
  captures exactly once.
- **Failure prevented.** An `IF NOT EXISTS … INSERT` is idempotent but not minimum-diff (won't update
  present rows); the MERGE handles both surfaces.
- **Discriminating predicate (P-DM).** A genuine new row captures once; idempotent redeploy of an
  identical row-set captures zero. Discriminates from DELETE-all+INSERT-all (captures every row).
- **Planes.** data · (identity, when the row crosses substrates → Reidentify).

#### Update
- **Physical realization.** `WHEN MATCHED AND (<null-safe-distinctness predicate>) THEN UPDATE SET …`.
  Per column: `(T.c <> S.c) OR (T.c IS NULL AND S.c IS NOT NULL) OR (T.c IS NOT NULL AND S.c IS NULL)`,
  OR-folded — the `IS DISTINCT FROM` expansion (`perColumnChangeDetection`).
- **Substrate constraint.** Only updatable columns participate: `{tracked} − {computed} − {reconciled
  surrogate} − {tolerance-covered}`.
- **Faithfulness class.** Faithful; **never fires on a no-op**.
- **Gate.** Comparable-column-set discipline (part c).
- **Artifact.** Post-deployment MERGE (or Transfer); the predicate is ScriptDom-built, not
  string-composed.
- **Measurement / proof.** Two CDC rows per update (`__$operation=3` before, `=4` after).
- **Failure prevented.** An unconditional `WHEN MATCHED THEN UPDATE` captures every matched row
  whether or not anything changed — `|capture| = |matched|`, not `|changed|`.
- **Discriminating predicate (P-NOOP).** `WHEN MATCHED` without the change-detection guard fires on
  every matched row. Adversarial input: a column transition `NULL → 'foo'` — a naive `<>` is `UNKNOWN`
  on a NULL side and **misses the change** (a real change goes uncaptured); the null-safe expansion
  captures it. The witness pair: idempotent redeploy → 0 captures; changed-content redeploy → fires.
- **Planes.** data · (decision, when the change is operator-tightened).

#### Unchanged
- **Physical realization.** *No clause fires* — the `WHEN MATCHED AND <predicate>` guard is false; the
  row passes through untouched. The algebraic identity element (`A ⊕ 0 = A`).
- **Substrate constraint.** The predicate must be false for every tracked column; the
  comparable-column-set must exclude tolerance columns or representation noise false-fires it.
- **Faithfulness class.** **Silence** — the strongest guarantee.
- **Gate.** The `AND <predicate>` guard (and the tolerance exclusion).
- **Artifact.** No DML emitted.
- **Measurement / proof.** Zero CDC rows; P-DM floor `‖δ‖=0 ⟹ |capture|=0` — the log's *absence* is
  the proof.
- **Failure prevented.** An unconditional MERGE captures every matched row even when unchanged.
- **Discriminating predicate (P-DM floor).** Re-running the same MERGE against an unchanged substrate
  produces zero captures; non-zero ⇒ a false-positive in the comparable-column-set or an
  unconditional clause.
- **Planes.** data.

#### Delete
- **Physical realization.** `WHEN NOT MATCHED BY SOURCE [AND <scope-predicate>] THEN DELETE`.
- **Substrate constraint.** Destructive; deletes follow referrer removal (P-ORD-DATA reverse order).
  Without the scope predicate, a MERGE against a *partial* candidate deletes every unmatched row —
  including out-of-scope rows the engine has no authority over.
- **Faithfulness class.** **Refuse-unless-declared** (the data analog of the schema drop-refusal).
- **Gate (P-DEL-SCOPE).** The DELETE arm is emitted only when a declared scope `S` is present and
  fires only on `r ∈ S`; absent a scope, the clause is suppressed (safe default = no unscoped
  deletes).
- **Artifact.** Post-deployment MERGE (the DELETE arm), gated.
- **Measurement / proof.** One CDC row per delete (`__$operation=1`).
- **Failure prevented.** A table-wide `WHEN NOT MATCHED BY SOURCE THEN DELETE` wipes rows outside the
  intended set, exiting clean.
- **Discriminating predicate (P-DEL-SCOPE).** Given a MERGE over a partial set `S ⊂ T`, rows in `T−S`
  not in the semantic delta must **survive**. Adversarial input: scope `S = {key ≤ 100}` with rows
  `> 100` present → ungated wipes them; scoped preserves them.
- **Planes.** data.

#### Reidentify (data-context)
- **Physical realization.** The MERGE `ON` clause keys on reconciled identity (business key or
  post-remap surrogate); the row's surrogate is re-pointed to the sink's minted value via two-phase
  FK re-point.
- **Substrate constraint.** IDENTITY mints per-DB (same logical row is `Id=280` in Dev, `Id=18` in
  UAT); a raw-surrogate match classifies the row as Delete+Insert.
- **Faithfulness class.** Faithful by named rule (P-REKEY), else fail-loud (P-DROP).
- **Gate.** Two-phase realization (phase-1 NULLed FKs; phase-2 re-point); cyclic/composite-IDENTITY
  `AssignedBySink` refused before any write.
- **Artifact.** `Transfer`-verb execution; `SurrogateRemapContext` (source→sink map).
- **Measurement / proof.** The re-key canary `(Order → User-by-email)` join, identical across source
  and sink while source surrogates are provably absent from the sink.
- **Failure prevented.** Matching on raw surrogate → Delete+Insert (`2×|rows|` captures) + broken FK
  relationship.
- **Discriminating predicate (P-REKEY).** Rows match by reconciled identity and compare by *semantic*
  value; a re-keyed row is an **Update**, not Delete+Insert. Adversarial input: `source_id=42`,
  `sink_id=7` for the same logical row → the `ON` must yield `WHEN MATCHED` (1 Update capture), not a
  Delete+Insert pair (2 captures).
- **Planes.** identity · data · (schema-dependency: sink schema at target state before transfer).

### 4.4 Cross-substrate and provenance moves — the deep cells

#### Move (cross-substrate flow)
- **Physical realization.** Bulk insert (`SqlBulkCopy` / batched `INSERT … SELECT`) for the primary
  transfer; CDC-aware MERGE for the minimum-diff variant; phase-2 FK update for deferred/cyclic FKs;
  post-deployment-script form for the publication flow.
- **Substrate constraint.** FK topological order (`DataLoadPlan` / `TopologicalOrderPass`); cycles &
  self-references go two-phase; TRUNCATE impossible if the sink is FK-referenced (use FK-ordered
  DELETE, or disable+`WITH CHECK` re-enable); CDC must be tracked for P-DM measurement.
- **Faithfulness class.** Minimal = CDC-measured; drops fail-loud.
- **Gate.** P-DROP (orphan/unmatched → non-zero exit); P-DEL-SCOPE; CDC-tracking pre-flight
  (`transfer.cdcTrackedSink`); FK-order pre-flight; connection + permission pre-flights.
- **Artifact.** Post-deployment MERGE (publication flow) or `Transfer` `executeWithData` (Dev→UAT).
- **Measurement / proof.** CDC capture series = |net delta|; idempotent → 0; complete replace →
  `2×|table|` (norm-inflating — the fallback's signature).
- **Failure prevented.** Silent row loss (ungated DELETE); CDC pollution (no change-detection
  predicate); FK violation mid-load (no topological order).
- **Discriminating predicate (P-DM + P-NOOP).** The MERGE uses the null-safe distinctness predicate;
  idempotent → 0 captures; one genuine change → exactly one capture. A predicate-less MERGE yields
  `|matched|` captures in both cases — indistinguishable. The witness is silence-then-firing.
- **Planes.** data · identity (Reidentify embedded) · schema (dependency: schema before data) ·
  provenance (CDC log is the data-leg provenance record).

#### Accumulate (provenance / time)
- **Physical realization.** Two parallel append operations per cycle: append the `CatalogSnapshot` to
  the timeline (`LifecycleStore`); append new refactorlog entries (deduped by `OperationKey`). On a
  fresh deploy, DacFx replays the entire refactorlog before computing the schema diff. State snapshots
  serialize via `CatalogCodec` (total / deterministic / re-validating). `CatalogDiff.compose` is the
  `+`; `reconstructLatest` is the FTC fold.
- **Substrate constraint.** The refactorlog is **never deleted** (fresh-deploy correctness needs the
  full history); new entries deduped against the prior; the codec round-trip must be **total over the
  IR** (a missed DU variant is a silent erasure at the persistence boundary); CDC capture rows are
  append-only by SQL Server design.
- **Faithfulness class.** Faithful = replay reconstructs.
- **Gate.** Dedup-against-prior (by `OperationKey`); codec totality (the `{ create … with … }`
  default-substitution audit); CDC pre-flight for the data leg.
- **Artifact.** Updated `.refactorlog`; new `Episode` JSON in the `LifecycleStore` (Catalog +
  Profile reference + refactorlog xref + CDC handle); `ChangeManifest` per edge (move counts, `‖δ‖`,
  refactorlog xref, CDC series; `pathLength` vs net-displacement surfaces churn); the per-sprint
  changelog for the SSIS consumer.
- **Measurement / proof.** `reconstructLatest` over the **disk-loaded** chain reproduces the latest
  state (the durable FTC); codec round-trip property `∀c. deserialize(serialize c) = Ok c`.
- **Failure prevented.** Mis-reconstruction on fresh deploy (a dropped entry → DROP+ADD → data loss;
  a duplicated entry → double `sp_rename` → SQL error); silent field default-substitution at the
  deserialization boundary; history loss at eject.
- **Discriminating predicate (P-PROV + P-RT₂ no-cheat).** Replay must thread the *actual* accumulated
  diffs, not return a stored target. Adversarial input: chain `A→B→C`; corrupt `δ₁` and re-run — a
  body that returns a cached `C` (ignoring `δ₁`) violates the no-cheat law. Second test: apply the
  full refactorlog to a fresh environment — any deleted/duplicated entry diverges the result.
- **Planes.** provenance · schema (entries drive `sp_rename` on fresh deploys) · time (the T13 FTC;
  the `LifecycleStore` substrate) · data (the CDC log is the data-leg Accumulate record).

#### The cross-cutting verbs as cells (compact)

| Verb | Physical realization | Faithfulness / gate | Measurement / proof | Discriminating predicate |
|---|---|---|---|---|
| **Snapshot** | OSSYS read → Catalog+Profile, or `ReadSide.read` → PhysicalSchema | — | the captured value is comparable by SsKey | a snapshot carries *true* SsKeys (engine-emitted), not synthesized-from-name keys, so a rename diffs as `Renamed` not `Removed+Added` |
| **Diff** | `CatalogDiff.between A B`, identity-first, modulus-bounded | observational; asserts nothing about *how* | `isEmpty` over every declared facet | P-CMP: a forgotten facet is silent blindness — `isEmpty` must be false iff A≉B over *every* declared facet |
| **Declare(-loss)** | operator passes `--allow-drops` / declares a scope | turns refuse → permit | the declaration is recorded in the ChangeManifest | the default is refusal; nothing destructive proceeds without an explicit declaration |
| **Gate** | a pre-flight probe (connection / permission / data-compat / CDC / scope) | refuse-first; named `Error` | the refusal precedes any mutation | P-GATE: every execution-time failure has a pre-flight that refuses *first* |
| **Measure** | read `cdc.<table>_CT` count / per-channel move count | the ruler of minimality | `‖emit(δ)‖ = ‖δ‖` (isometry) | the CDC count *is* the norm — minimal ⟺ isometric emission |
| **Publish** | emit CREATE+refactorlog+pre/post → DacFx → Octopus; or the consumer changelog | the artifact reaches its terminus | the generated script reviewed pre-prod (sp_rename, never DROP+ADD) | the deploy artifact is *declarative + refactorlog + data scripts*, never the imperative ALTER |
| **Record** | write the run as a durable `Episode` into the `LifecycleStore` | the keystone of provenance | the next sprint's Diff loads this prior; FTC reproduces the state | recorded only after Verify — a recorded-but-broken episode corrupts the chain |
| **Verify** | read-back, assert `Ingest∘Project = id` modulo named tolerances | the canary | PhysicalSchema diff empty; B' reproduces B | equality is *modulo named tolerances*, never raw — a leak is a spurious ALTER |
| **Reconcile** | discover the source→sink correspondence by a named rule (email; surrogate map) | faithful-by-rule else fail-loud | the matching report; `validate-user-map` | the correspondence is by **business key**, never the raw surrogate |
| **Tolerate** | classify a difference as substrate-noise and *name* it | the residual of T16 | the named entry appears in the manifest as tolerance residual | P-IF: every observed difference is operator-intended (emit) or substrate-noise (tolerate) — **silence on neither** |

### 4.5 Move × deploy-mode (axis 3)

How each move realizes under the three modes. The granular-vs-coarse intricacy (declared-loss vs
DacFx's global levers) is held in §5.

| Move | Declarative (SSDT+DacFx+Octopus) | Imperative in-place (lens / `migrate --execute`) | Fresh wipe-and-load |
|---|---|---|---|
| **Add** | adjusted `CREATE` → DacFx `ADD` | `ALTER … ADD` (lens preview; live executor) | full `CREATE` (empty target → all adds) |
| **Remove** | file removed → DacFx `DROP` (gated by `DropObjectsNotInSource`=False in prod + `BlockOnPossibleDataLoss`) | `DROP` only under `--allow-drops`, refused first by default | N/A (target is empty) |
| **Rename** | refactorlog entry → DacFx `sp_rename` | `sp_rename` + `V2.LogicalName` re-bind (live) | replayed from the full refactorlog history on first deploy |
| **Reshape** | adjusted `CREATE` → DacFx `ALTER COLUMN`; `SchemaMigrationEmitter` is the *lens* | `ALTER COLUMN` (live; widening metadata-only) | full `CREATE` at the target facet |
| **Insert/Update/Delete/Unchanged** | post-deploy CDC-aware MERGE (engine-owned; DacFx moves no data) | the same MERGE executed in-line | `TRUNCATE`+bulk reload (`2×|table|` CDC — norm-inflating) |
| **Reidentify / Move** | post-deploy two-phase transfer scripts | `Transfer.executeWithData` (live, schema-then-data) | bulk reload with reconciliation |
| **Accumulate** | append+dedup refactorlog into the dacpac; Episode recorded | each `migrate` run recorded as an Episode | first episode = genesis; full refactorlog seeded |

**Publish-profile levers (declarative mode), and the impedance.** `BlockOnPossibleDataLoss=True`
(always, every env) · `DropObjectsNotInSource=False` (prod; explicit PR-reviewed removal only) ·
`IgnoreColumnOrder=True` (column-order changes are cosmetic but force full rebuilds) ·
`GenerateSmartDefaults=False` (prod; smart defaults silently fill wrong values) ·
`AllowIncompatiblePlatform=False`. The **granular-vs-coarse impedance**: the declared-loss gate wants
*per-object* granularity ("drop column A, refuse column B") but `BlockOnPossibleDataLoss` is a
**global binary** (fires if *any* statement could lose data). The target reconciles this in three
layers — (1) the engine's per-object pre-flights (`--allow-drops`, `dataViolatesTightening`) refuse
per-object *before* DacFx runs; (2) the engine controls *which objects enter the dacpac model*,
achieving per-object control by inclusion/exclusion, not by DacFx flags; (3) `BlockOnPossibleDataLoss=
True` is the non-negotiable backstop catching anything the pre-flights missed. The residual intricacy:
the engine cannot inspect DacFx's generated script, only the model inputs — so the per-object gates
must be tight, and the global lever blocks a *too-broad* set (the whole deploy) as the last line of
defense.

### 4.6 The gate × failure-mode matrix (axis 9)

The complete gate set. The **completeness law (P-GATE):** for every execution-time failure mode F,
some gate refuses first when `precondition(F)` is violated. This is what **T-VI spanning** quantifies
over.

| Gate | Faithfulness enforced | Moves/proteins guarded | Failure mode prevented | Discriminating predicate |
|---|---|---|---|---|
| **Connection pre-flight** | refuse-unless-live | all mutation moves (P-1…P-6) | mid-transfer `SqlException` from a dead endpoint → half-populated target | `State=Open ∧ identity resolves` on both endpoints before the first write |
| **Permission pre-flight** | refuse-unless-granted | Move/Add/Reshape/Remove on a sink | **write-denied sink silently transfers zero rows, exits clean** (the illusion of success) | `sys.fn_my_permissions ≠ ∅` for every planned (object, verb); first uncovered → `insufficientGrant` |
| **Declared-loss (DROP)** | refuse-unless-declared | Remove; destructive Reshape (P-4/P-6/P-7) | mid-ALTER abort on destructive DDL; partially-altered schema | `allowDrops=false ∧ destructive → Error` at *plan/emit* time, not execution time |
| **Delete-scope (P-DEL-SCOPE)** | refuse-unless-declared | Delete (P-3/P-6) | silent wipe of out-of-scope rows on a partial refresh | `WHEN NOT MATCHED BY SOURCE` absent unless a declared scope S is present |
| **CDC-tracking** | refuse-unless-declared | Move/bulk-load on a tracked sink (P-1…P-3) | `2×|table|` CDC churn → downstream ETL corruption | `cdcTrackedTables(sink) ≠ ∅ ∧ ¬allowCdc → Error` before phase 1 |
| **CDC-silence-on-idempotence** | faithful (idempotent) | all schema moves when unchanged (P-5) | spurious schema-level CDC events on an unchanged redeploy | `isEmpty(between A A)=true ⟹ zero DDL emitted` |
| **Data-compat (NOT-NULL, cross-substrate)** | refuse-unless-declared | Reidentify/Move/Reshape under `EnforceNotNull` (P-3/P-6) | **mid-load NOT-NULL violation → half-populated sink** (the Decision↔Data coupling) | reads *source* null-counts: `∃ r: r.col=NULL ∧ EnforceNotNull(col) → notNullViolation` before phase 1 |
| **Possible-data-loss (DacFx)** | refuse-unless-declared | Reshape (narrowing), Remove (P-6, declarative) | silent column truncation mid-ALTER | `BlockOnPossibleDataLoss=True` — **global/coarse**, blocks the whole deploy if any object risks loss |
| **Data-compat (on-disk bytes, in-place)** | refuse-unless-declared | Reshape (NOT-NULL on a populated table) | mid-statement `ALTER COLUMN NOT NULL` abort; inconsistent column state | `COUNT(*) WHERE col IS NULL > 0 ∧ enforce → Error` before the ALTER is submitted |
| **Transactional / resumable envelope (P-EXE)** | refuse-unless-resumable | Move (all data transfer, P-6) | half-populated target on mid-transfer crash; duplicate rows on retry | `∀ mid-load failure: state(sink) ∈ {clean, deterministic-resume-point}` |

> **Premise re-prioritization (held, not flattened).** Because PROD carries no data yet, the
> transactional/resumable envelope and the permission/connection gates are **represented but
> deferred** in priority — they become the critical path only when PROD gains data or a write-denied
> environment enters a real flow. The completeness law still *requires* them in the target; the
> premise reorders *when* they are built, not *whether*.

### 4.7 Protein × amino-acid incidence (the fold map)

Which moves each protein folds. `●` = central to the protein; `○` = present when the sprint's delta
contains it; `—` = not exercised. This is the matrix's "every protein decomposes into listed amino
acids" face (§7).

| Move \ Protein | P-1 Dev | P-2 QA | P-3 UAT-rekey | P-4 SSIS pub | P-5 redeploy | P-6 migrate | P-7 eject | P-8 drift | P-9 canary |
|---|---|---|---|---|---|---|---|---|---|
| **Snapshot** | ● | ● | ● | ● | ● | ● | ● | ● | ● |
| **Diff** | ● | ● | ● | ● | ● | ● | ● | ● | ● |
| **Add** | ○ | ○ | ○ | ○ | — | ○ | — | ○ | ○ |
| **Remove** | ○(declared) | ○ | — (fwd-only) | ○(declared) | — | ○(declared) | ○(declared) | ○ | ○ |
| **Rename** | ○ | ○ | ○ | ● | — | ○ | ● | ○ | ○ |
| **Reshape** | ○ | ○ | ○ | ● | — | ○ | ○ | ○ | ○ |
| **Insert** | ● | ● | ● | — | — | ○ | — | — | — |
| **Update** | ○ | ○ | ○ | — | — | ○ | — | — | — |
| **Unchanged** | ● | ● | ○ | — | ● | ● | — | — | ● |
| **Delete** | ○(scoped) | ○ | — | — | — | ○(scoped) | — | — | — |
| **Reidentify** | — | — | ● | — | — | ○ | — | — | ○ |
| **Move** | ○ | ○ | ● | — | — | ● | — | (remediate) | — |
| **Accumulate** | ● | ● | ● | ● | ○ | ● | ● | ○ | — |
| **Declare(-loss)** | ○ | ○ | — | ● | — | ○ | ● | ● | — |
| **Gate** | ● | ● | ● | ● | ● | ● | ● | ● | ● |
| **Measure** | ● | ● | ● | ○ | ● | ● | — | ● | ● |
| **Publish** | ● | ● | ● | ● | ● | ● | ● | ○ | ● |
| **Record** | ● | ● | ● | ● | ○ | ● | ● | ○ | — |
| **Verify** | ● | ● | ● | — | ● | ● | ● | — | ● |
| **Reconcile** | — | — | ● | — | — | ○ | — | — | — |
| **Tolerate** | ● | ● | ● | ○ | ● | ● | ● | ● | ● |

---

## 5 — The laws and invariants

The cross-cutting predicates that must hold across *all* cells. Each is the *discriminating* law (the
input where a plausibly-named-but-wrong version breaks the equation), not a restatement of a name.

### 5.1 The torsor — State is a point, Delta is a displacement (T12)

States form an **affine space (torsor)** over the group of Deltas, with two primitives:
`⊖ (between) : State × State → Delta` (`δ = B ⊖ A`) and `⊕ (apply) : State × Delta → State`
(`B = A ⊕ δ`). The round-trip law, the identity diff, and composition-over-time are **not three
facts — they are the three Weyl axioms** of an affine space:

- **W1 (identity).** `A ⊕ 0 = A`, where `0 := A ⊖ A`. *(The empty diff is the zero displacement;
  idempotent redeploy is a no-op.)*
- **W2 (composition / Chasles).** `(B ⊖ A) + (C ⊖ B) = C ⊖ A`. *(Evolution over a timeline is vector
  addition; `+` is `CatalogDiff.compose`.)*
- **W3 (round-trip / uniqueness).** `A ⊕ (B ⊖ A) = B` and `(A ⊕ δ) ⊖ A = δ`.

### 5.2 The no-cheat law (P-RT₂) — forced by W3

W3 entails that `apply` is a *genuine action*: `A ⊕ δ` depends on `A`. An implementation
`apply(δ) = const` (e.g. `applyDiff base d = target d`) collapses the torsor. The **discriminating
witness** — `∃ A' ≠ source. apply(d, A') ≠ target(d)` — is not an extra test; it is W3 made
falsifiable. This is the structural armor against the `applyDiff` near-miss (§2.3).

### 5.3 Faithfulness (T-I) and the isomorphism ladder

`Ingest ∘ Project = identity` modulo a **named, closed** erasure set, on every plane. The ladder:

- **L1 — witness present.** A named round-trip test exists and is live. *Insufficient alone:* a
  round-trip can be green while an axis erases a feature **silently**.
- **L2 — faithful.** Every erasure is a `Tolerance` entry, a structured diagnostic, or a fail-loud
  refusal — **never a silent drop**. The round-trip is an *equivalence*, not a one-sided retraction.
- **L3 — composed.** The axis is orthogonal (no hidden coupling) and participates in the green
  one-command `migrate A B`.

**The cardinal sin: a silent erasure is strictly worse than no claim** — it manufactures the illusion
of fidelity. The three faithfulness classes (`faithful` / `lossy-with-warning` /
`refuse-unless-declared`) and the four erasure-surfacing channels (Tolerance entry · structured
diagnostic · fail-loud refusal · `AxiomTests.fs` Skip stub) are how "named and closed" is enforced.

### 5.4 The intent filter (P-IF) — the residual of the master equation

`observe(A, B) = (B ⊖ A) ⊕ tolerate(A, B)` — intended displacement ⊕ tolerated noise (orthogonal).
A *raw observed difference is not a true delta*: read-back surfaces DacFx auto-named constraints,
default collations, empty-string↔NULL, ANSI-padding, decimal scale, collation-equal-but-byte-different
strings — substrate noise that must be **tolerated, not emitted**. Acting on the raw diff re-emits
spurious changes, relocking tables and churning CDC for non-changes — the exact disease the engine
exists to cure. **Every observed difference is operator-intended (emit) or substrate-noise (tolerate);
silence on neither** — pillar 9 at the difference level, on both planes.

### 5.5 Channel partition (P-CH) and ordering (P-ORD) — orthogonality (T-V, T14)

A displacement is the **direct sum of its channel projections**: `δ = ⊕_c π_c(δ)`, with `π_c ∘ π_c' =
0` and `Σ π_c = id`. Schema channels = Rename ⊕ Reshape ⊕ Add ⊕ Remove ⊕ Reidentify; data channels =
Insert ⊕ Update ⊕ Delete ⊕ Reidentify. The norm is additive: `‖δ‖ = Σ_c ‖π_c(δ)‖`.

- **Partition (P-CH).** Every `CatalogDiff` element is realized by exactly one move — none twice,
  none dropped. Rename ⊥ Reshape by construction (a renamed element carries no shape facet); the
  refactorlog and ALTER channels never touch the same attribute. *Discriminating input:* a rename
  that also widens — the rename emits no ALTER; the width change emits no refactorlog entry.
- **Ordering (P-ORD).** Rename precedes reshape-on-the-new-name; create precedes insert; schema
  precedes data; FK two-phase. The plan is a *partial order*, not a set.
- **The cross-axis couplings are surfaced, not implicit (T-V).** Decision→Data: a NOT-NULL tightening
  on a column whose source rows carry NULLs is a *named pre-flight*, not a mid-load failure.
  Identity→Schema: a rename that diverges the physical coordinates a Transfer matches on is reconciled
  by the Transfer *consuming the rename map* (match by SsKey, never by ordinal).

### 5.6 Minimality (T15) — CDC is the norm made physical

`‖δ‖_data = |capture(run(emit(δ_data), Â))|` — **the CDC capture-row count is the norm**. Emission is
an **isometry**: `‖emit(δ)‖ = ‖δ‖`.

- **CDC-silence** is the `‖δ‖=0 ⟹ |capture|=0` instance (W1 under emission) — the highest-stakes
  single guarantee.
- **Minimum data diff** is isometric emission (the change-detecting MERGE: capture = |changed rows|).
- **Complete replace** is non-isometric (`‖replace‖ = 2·|table| ≫ ‖δ‖`) — correct but norm-inflating;
  the precise reason it is the *fallback*, not the default.
- *General case (P-DM):* after the incremental plan, `capture = |true delta|` for a known `k`.

### 5.7 The master equation (T16) — the Project square commutes

```
run( emit(B ⊖ A), realize(A) )  =  realize(B)        modulo residual = (erasure ⊎ tolerance)
```

A change computed in the Model and realized on the Substrate lands at the same point. **The schema
leg and the data leg are its two projections.** `emit` is *partial* (destructive moves refused) and
*lossy* (narrowing warns); the **iso-ladder L1/L2/L3 measures `emit`'s faithfulness**, and the
residual is the intent-filter's tolerated bucket (§5.4). This is the H-050 adjunction lifted from
points to displacements: `between`/`apply` are Ingest/Project on arrows. The composed `migrate A B`
(P-6) is the forcing instance that exercises T12–T15 at once.

### 5.8 Identity is the conserved charge (A43) — and the refactorlog is *derived*

Under every move, `SsKey` is conserved, with one creation/annihilation pair:

| Move | Identity | Designation | Facets / cells |
|---|---|---|---|
| Rename | conserved | **changed** | — |
| Reshape / Update | conserved | conserved | **changed** |
| Reidentify | correspondence reconstructed (surrogate differs; matched by business key) | — | — |
| Add / Insert | **created** | — | — |
| Remove / Delete | **annihilated** | — | — |

The three name-spaces are distinct: **Identity** (`SsKey`, invariant — *what it is*) · **Designation**
(`Name`, where renames act — *what we call it*) · **Realization** (the physical object name, derived —
*what the substrate bears*), with the production policy `Realization := Designation`. Conflating them
is the "physical column rename" near-miss (§2.3). **The cross-plane corollary:** a faithful schema
Rename must induce **zero data moves** — `‖emit(π_Rename(δ))‖_data = 0`. Because `sp_rename` conserves
rows whereas DROP+ADD induces `2·|table|`, "use the refactorlog for renames" is **not a convention we
adopt — it is forced** by Identity-conservation across the schema→data coupling. P-ID: comparison
matches by `SsKey` before comparing anything; physical names are never reused; provenance compares
engine-emitted snapshots carrying true SsKeys.

### 5.9 Provenance replay (P-PROV) and the two parallel logs

The accumulated record (snapshots + append-only refactorlog) **replays** to reconstruct any state
from its earlier provenance; entries are never deleted; new entries are deduped against the prior. The
schema and data legs each maintain an append-only log that is **simultaneously the history AND the
proof of minimality** — the same construct one plane apart:

| | Schema-plane log | Data-plane log |
|---|---|---|
| Artifact | append-only `.refactorlog` (XML, `OperationKey` GUID) | append-only `cdc.<table>_CT` (one row per DML row-version) |
| Records | each rename as a named move | each data move (`__$op`: 1=delete, 2=insert, 3/4=update before/after) |
| Faithful = | replay reconstructs (dedup, never deleted) | `capture = |true row delta|` (P-DM) |
| History AND proof because | a fresh DacFx publish that reads it cannot derive DROP+ADD for any listed rename — the entry *is* the proof the rename was faithful | reading `count = k` *is* the proof exactly k rows moved; `count = 0` *is* the proof of idempotence |

Both are the time-integral of their plane's derivative (the concern-movement field); the FTC
`reconstructLatest = genesis ⊕ Σδ` recovers state from the schema integral.

### 5.10 The three comparison regimes (axis 5) — name when each applies

The Diff is observational; *which two states* it compares is the operator's discretion:

- **(a) prior-snapshot basis** — `A` = the last-emitted snapshot from the `LifecycleStore`; `B` = the
  new model. The standard sprint-by-sprint incremental path; the prior snapshot is what DacFx and the
  deployed refactorlog were built against. Yields the minimum-viable differential.
- **(b) current target-reality** — `A` = `ReadSide.read` of the live DB; `B` = the model. Drift
  detection and live-state reconciliation; the intent filter (P-IF) is *mandatory* here (read-back is
  noisy).
- **(c) two authored models** — both `A` and `B` are authored from source. Design-time "what would
  this migration cost" preview; pure displacement, no substrate noise.

### 5.11 Determinism (T1) and classification totality (pillar 9)

Two properties hold across *every* cell by construction: **Determinism** — same inputs → byte-
identical text/JSON, model-equivalent binary; no clock, randomness, or I/O in the core. **Classification
totality** — every transformation is `DataIntent` (the factual skeleton, reachable from
`Project(catalog, Policy.empty, profile)`) or `OperatorIntent` (a named, recorded overlay on a named
axis). Nothing is unclassified; nothing is silent. The skeleton is the deterministic factual baseline;
every overlay is named and recorded in the manifest.

### 5.12 The three named design forks (decisions the target owns but has not closed)

These are genuine degrees of freedom in the target — named here so a future agent recognizes each and
resolves it before the dependent slice opens:

1. **Refactorlog-against-prior input boundary** (gates P-4/P-6/P-7). Is the committed prior
   `.refactorlog` an *engine input at emit time* (engine appends, deduping) or a *repo merge-time
   concern* (engine emits full, repo reconciles)? The target leans engine-input (sourcing the prior
   from the `LifecycleStore` via the FTC), but the seam is named, not closed.
2. **Provenance rendering for the SSIS consumer** (gates P-4). Do they consume the dacpac (CREATE
   files), a human-readable per-sprint changelog, a machine-readable diff, or all three? Decides
   whether `Comparison` needs a consumer-facing rendering distinct from the deploy rendering.
3. **Eject deliverable** (gates P-7). At freeze, is the deliverable the frozen state **plus the full
   accumulated refactorlog history** (append-forever) or the **frozen state alone** (collapsible)?
   Discriminating condition: whether any downstream system ever DacFx-publishes against an
   *intermediate* (pre-freeze) state — if yes, append-forever is required.

---

## 6 — Glossary and corpus map

### 6.1 Glossary — the load-bearing terms

**Adjunction `Project ⊣ Ingest`** — the relationship between emission (Model→Substrate) and ingestion
(Substrate→Model); the north star is this adjunction made total and self-verifying.
**`between` (`⊖`) / `applyDiff` (`⊕`)** — the torsor's two primitives: subtraction of states yielding
a Delta, and the affine action of a Delta on a state.
**Amino acid / protein** — *expository* framing (not type names): the atomic moves (§2) and the
folded workflows (§3) they compose into.
**Canary** — the round-trip verification harness (`emit → deploy → readSide → compare`) that checks
the adjunction at runtime, modulo named tolerances; blocks merges on unaccepted divergence.
**`CatalogDiff` / Comparison** — the schema-plane Delta value; the morphism set in the
catalog-evolution category; partitioned exhaustively into Add/Remove/Rename/Reshape; composed via
`compose`.
**CDC-silence** — deploying V2's output generates zero spurious CDC rows; the `‖δ‖=0 ⟹ |capture|=0`
instance; the highest-leverage single guarantee.
**`ChangeManifest`** — the per-edge manifest of a Delta (move counts, `‖δ‖`, refactorlog xref, CDC
series); `pathLength` vs net-displacement surfaces churn.
**DataIntent / OperatorIntent** — the dichotomy at every transformation site: factual skeleton vs
named overlay. Policy IS OperatorIntent reified.
**Eject** — the terminal state: schema freeze; the model stops being OutSystems-derived and becomes
operator-owned forever; the hardest correctness requirement (no regeneration after).
**`Episode` / `EpisodicLifecycle` / `LifecycleStore`** — the durable multi-plane episode substrate;
co-records schema+Profile+metadata at one `EpisodeCoordinate`; persists the chain; runs the FTC over a
disk-loaded chain.
**Faithful (L2)** — `Ingest ∘ Project = id` modulo a *named, closed* erasure set; contrasted with a
mere witness (L1).
**Isomorphism ladder L1/L2/L3** — witness present → faithful (no silent drop) → composed (orthogonal,
in the green `migrate`).
**`migrate A B`** — the L3 composed operation: diff → rename → CDC-safe deploy → identity-reconciled
transfer → verify, in one command; atomic-or-resumable; refuses loudly rather than corrupting.
**Norm `‖δ‖`** — the count of moves; physically the CDC capture-row count (data plane); the instrument
that makes "minimum viable touches" measurable.
**Publication-and-provenance engine** — the correct framing: the engine publishes an evolving model
and records its provenance for the SSIS consumer and the eject; NOT primarily a live-PROD deploy
engine.
**`RefactorLog`** — the SSDT-native XML recording rename history; consumed by DacFx so incremental
deploys `sp_rename` not DROP+CREATE; append-only, never deleted.
**Sibling chorus** — the family of emitters (SSDT DDL, DACPAC, RefactorLog, JSON, Distributions, data
triumvirate), all sibling Π's of one Catalog; T11 requires their keysets agree.
**Skeleton / Overlay** — the deterministic factual baseline `Project(catalog, Policy.empty, profile)`
vs a named `OperatorIntent` transform; together they partition every output.
**`SsKey`** — the stable identity carrier; four-variant DU
(`OssysOriginal | Synthesized | DerivedFrom | V1Mapped`); identity-survives-rename (A1).
**Tolerance** — a named, declared erasure (entry / diagnostic / refusal) — never a silent drop; the L2
precondition.
**Torsor over Delta** — the affine-space structure of States; `⊖`/`⊕` are the section/retraction
pair; W1/W2/W3 are the round-trip / identity / composition laws unified.
**Total Projection** — the endpoint: the adjunction total on every plane, faithful (L2), orthogonal
(T-V), spanning (T-VI), composed into `migrate A B` (L3), self-describing.
**Transfer / Ingestion / Projection / Source / Sink** — the bidirectional vocabulary: `Projection` is
emit, `Ingestion` is read-back (the other adjunction leg), `Transfer` is the cross-substrate
composition (`Project_sink ∘ Ingest_source`).
**T-I…T-VI** — the six totalities (round-trip · executable-axiom · input · documentation · orthogonality
· spanning) that define "done."
**Three planes' near-misses** — `applyDiff`-ignores-base (P-RT₂) and physical-vs-logical rename (P-RN):
the two recorded right-by-name-wrong-by-function traps.

### 6.2 Corpus map — what each prior document contributed (now provenance, this is the index)

The corpus carried **at least five competing "apex" claims** (NORTH_STAR, PRODUCT_AXIOMS,
WAVE_6_ONTOLOGY, the five-axis audit, SPINE). This masterwork ends the sprawl by being the single
index; each prior document is reduced to its precise provenance role below.

| Document | Fragment of the target it holds | Disposition |
|---|---|---|
| **`NORTH_STAR.md`** | The bullseye as an isomorphism-ladder matrix across five axes; the six totalities T-I…T-VI; the operator's eight-promise covenant; "fidelity is a theorem." | **Superseded as apex** (this is the new index); provenance for where the bullseye and totalities were named. |
| **`NORTH_STAR.matrix.generated.md`** | The machine-derived current coverage snapshot (L1/L2 counts; gate PASS). | Live generated artifact; the pass-two surface for current state. |
| **`VISION.md`** | The cutover-era operational frame: the forcing function, the sibling chorus, `Project = Π ∘ E`, the fallback ladder (V1-only/augmented/driver), T-30/T-15 gates, R6 governance. | Provenance for the cutover strategy; the first ring of the bullseye (a strict subset). |
| **`VISION_REVIEW.md`** | The adversarial substantiation of VISION rev 2; killed unfalsifiable rhetoric; separated CDC-safety from determinism; proposed the type-theorem refactor. | Provenance for the scope-restraint discipline and reasoning resolutions R1–R8. |
| **`WAVE_6_ONTOLOGY.md`** | The ontological grounding: the premise (publication+eject), the five-layer microscope, the seven entities, the seven schema + five data moves, the discriminating-predicate discipline. | Provenance for the change-ontology and the premise re-prioritization; the direct parent of §2–§5 here. |
| **`WAVE_6_ALGEBRA.md`** | The reification into the change calculus: State-as-torsor, W1/W2/W3, the norm, T12–T16 + A43, the commuting square. | Provenance for the formal equations (§5). |
| **`WAVE_6_MORPHOLOGY.md`** | The as-is structural research: the amino-acid families and protein list mapped to the codebase; the latent-vs-activated finding. | Provenance for the amino-acid/protein framing (expository); the bridge to pass two. |
| **`PRODUCT_AXIOMS.md`** | The L3 operator-facing contract: every promise as a falsifiable axiom (L3-S/D/I/X/C/CC). | **Live prerequisite** — the canonical L3 registry; this indexes it for operator-facing claims. |
| **`AXIOMS.md`** | The L2 formal system (A1–A43, T1–T16): identity, purity, policy-orthogonality, lineage, the torsor theorems. | **Live prerequisite** — the axiom ledger; cited, not duplicated. |
| **`AUDIT_2026_05_31_FIVE_AXIS_REDTEAM.md`** | Established the isomorphism ladder; per-axis silent erasures; the two couplings; the three missing spanning dimensions (Permissions/Transactionality/Connection). | Provenance; the empirical forcing function for the gate set (§4.6) and T-V/T-VI. |
| **`AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md`** | The L1/L2/L3 coverage model; buckets A/B/C/D; the campaign structure. | Live prerequisite for the T-II executable-axiom claim. |
| **`DEBRIEF_2026_06_02_ISOMORPHISM_CLIMB_AND_BACKLOG.md`** | The reconciled current-state matrix; the 20-row fidelity ledger; the 10-cluster backlog. | **The pass-two surface** — the live "where we stand" ledger this masterwork's matrix is the target for. |
| **`V2_DRIVER.md`** | The destination KPI; the per-axis correctness-stakes table; the four structural-evidence concerns. | Live reference for per-axis stakes. |
| **`SPINE.md`** | The categorical rendering: seven primitives, seven patterns, six structural inferences (sheaf/adjunction/Hom-set/quotient/continuation/tessellation). | Provenance for the categorical framing. |
| **`ADMIRE.md`** | The V1-reference register; editorial-inheritance ledger; V1-as-editorial-donor. | Live operational ledger for V1 inheritance. |
| **`EXECUTION_PLAN.md`** | The wave structure (0–6); per-slice acceptance criteria; the self-verification endgame. | Live prerequisite for the Wave-6 slice backlog. |
| **The handbook (`/handbook/`)** | The operator-facing SSDT/DacFx doctrine: declarative-vs-imperative, the refactorlog/rename discipline, CDC and schema evolution, multi-phase patterns, the anti-pattern gallery, deployment-safety levers. | **Live prerequisite** — the physical deployment doctrine; cited (§4.5, §5), not duplicated. |
| **`PRESCOPE_TRANSFER.md` / `TRANSFER_ISOMORPHISM_SUBSTANTIATION.md`** | The bidirectional Transfer architecture (`SubstrateRole`, `SchemaContract`, `IdentityDisposition`, `SurrogateRemapContext`); the UAT capability survey instrument. | Live reference for the data-axis L3 target and the survey gate. |
| **`CUTOVER_READINESS_BRIEF.md` / `V1_PARITY_MATRIX.md`** | The per-axis T-30 confidence map; the 185-row V1↔V2 parity ground truth. | Live operational readiness surfaces. |

---

## 7 — The completeness checklist

The explicit test for whether the matrix has no holes. Each item is a checkable predicate over the
document itself.

1. **Every protein decomposes into listed amino acids.** Each of P-1…P-9 (§3) is a folded chain whose
   every step is a verb in the §2 alphabet — no step appeals to an operation outside the alphabet. The
   incidence matrix (§4.7) is the witness: every protein column is covered by §2 rows.
2. **Every amino acid has a cell on every plane it touches.** Each structural move (§4.2–§4.4) lists
   its *planes touched*; each plane it touches has a deep cell with all nine fields. Schema and data
   legs mirror (Add∥Insert, Remove∥Delete, Reshape∥Update); Reidentify and Accumulate are placed on
   their multiple planes explicitly.
3. **Every gate has a failure mode.** Each of the ten gates (§4.6) names the specific execution-time
   failure it refuses-first; the completeness law (P-GATE) is the meta-predicate: no execution-time
   failure mode lacks a pre-flight. *Check:* scan §4.6 — no gate row reads "—" in the failure column.
4. **Every artifact has a measurement.** Each emitted artifact (CREATE / refactorlog / pre/post
   script / dacpac / publish-profile / MERGE / Episode) has a proof in its cell: PhysicalSchema diff,
   refactorlog replay, CDC capture count, or canary equality-modulo-tolerance. *Check:* no deep cell
   has an "Artifact" without a "Measurement / proof."
5. **Every cell has a discriminating predicate.** No cell rests on a name; each names the input on
   which a plausible-but-wrong implementation diverges (right-by-function). *Check:* every deep cell
   and every law (§5) carries a P-* predicate.
6. **The faithfulness class is total.** Every move is classified `faithful` / `lossy-with-warning` /
   `refuse-unless-declared`; no move is unclassified, and no erasure is silent (every loss routes to a
   Tolerance entry, a structured diagnostic, or a fail-loud refusal).
7. **The partition holds (P-CH).** Every `CatalogDiff` element is realized by exactly one move — none
   twice, none dropped — and the channels are orthogonal (`π_c ∘ π_c' = 0`). The norm is additive
   (`‖δ‖ = Σ ‖π_c δ‖`).
8. **The ordering is a respected partial order (P-ORD).** Rename before reshape; create before insert;
   schema before data; FK two-phase; gate before mutation; record last; accumulate before publish (the
   seven cross-protein constraints, §3).
9. **Provenance replays (P-PROV).** The accumulated record reconstructs any state from its earlier
   provenance; entries never deleted; deduped against prior — on both the schema (refactorlog) and
   data (CDC) legs.
10. **The master equation balances on both legs (T16).** `run(emit(B ⊖ A), realize(A)) = realize(B)`
    modulo (erasure ⊎ tolerance), with the schema and data legs its two projections, exercised
    end-to-end by the composed `migrate A B` (P-6).
11. **The ten axes are all present.** Move · plane · deploy-mode · temporal-phase · comparison-regime ·
    environment-lattice · consumer/terminus · ordering · gate-set · measurement — each is named in
    §4.1 and exercised in the cells.
12. **The named design forks are explicit, not silent (§5.12).** The three open decisions (refactorlog
    input boundary · SSIS provenance rendering · eject deliverable) are named with their discriminating
    conditions, so the target's genuine degrees of freedom are visible rather than papered over.

When all twelve hold, the matrix has no holes: every protein folds from the alphabet, every amino acid
sits on every plane it touches, every gate guards a named failure, every artifact carries a proof, and
every cell is pinned by the predicate that makes the engine *structurally isomorphic to the shape of
change* — not merely named after it.

---

— Recorded for the receiving agent. This is the masterwork: the amino-acid alphabet (§2), the protein
catalog (§3), the master matrix (§4), the laws (§5), the glossary and corpus map (§6), and the
completeness checklist (§7). Read only this and you are oriented. The other documents are provenance;
this is the index. Hold the spine. Complete the matrix.
