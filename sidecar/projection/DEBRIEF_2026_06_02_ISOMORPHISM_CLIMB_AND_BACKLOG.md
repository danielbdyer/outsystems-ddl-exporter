# DEBRIEF 2026-06-02 — The Isomorphism Climb: Technical Debrief & Total-Projection Backlog

> **UPDATE — round D→E→F (survey-independent L2 climb closed).** Since this debrief
> was authored, the keystone and the schema/diff + decision L2 holes have closed:
> **D1+D2** (the self-verification meta-cell — the matrix reports the L1/L2/L3 ladder
> from `Tolerance.fs` `@ladder` tags + witnesses, wired into CI; G13), **E2** (the
> cross-schema FK now a named diagnostic, not silent/opaque; G4), **E1** (non-PK
> index structure reconstructed *and* compared in `PhysicalSchema.Indexes`, round-trip
> witnessed; G3 — residual narrowed to `IndexOptionsUnreflected`), and **F1+F2** (the
> unified 3-axis decision adjunction witness — nullability + uniqueness + FK-trust in
> one overlay round-trip; G2/G12). North-Star §5 criteria 2, 3, 5 are met. **What
> remains** (each named, none on the silent-corruption path): **H1**/**G18** (the
> speculative-optics cluster — parked at defer-with-trigger until cutover+1);
> **J1**/**G20** (51-label perf canary — low-priority enabler); **A3**/**G6**
> (atomic-resumable transfer — survey-*dependent* / PROD-deferred); **G19** (the
> physical-comparison VO lift — deferred-with-trigger).
>
> **G-series sweep (G1–G20, reconciled to HEAD).** The fidelity ledger is now
> almost entirely closed. **Closed this session:** G3 (E1), G4 (E2), G12 (F2), G13
> (D1/D2), G14 (bounded guard). **Closed earlier but stale in the ledger (now
> reconciled):** G1 (C1), G2/G5/G7/G8 (F1/F2 + the wired spanning pre-flights + B1
> `--execute`), G9/G10/G11 (P6 real clock + 6.H.4 displacement-manifest + 6.H.3
> compose-fold). **Already-closed refusals:** G15/G16/G17. **Deferred-by-design:**
> G6 (A3, PROD-empty), G18 (optics, cutover+1), G19 (VO lift, trigger), G20 (J1).
> Net: **17 of 20 closed; 3 deferred-with-trigger; 0 open on the survey-independent
> faithfulness path.**
>
> **Status:** canonical for this moment (HEAD `307ef65`). A standalone debrief +
> backlog for the L1→L2→L3 climb toward the Total Projection (`NORTH_STAR.md`).
> Supersedes nothing; **integrates** the per-axis findings of
> `AUDIT_2026_05_31_FIVE_AXIS_REDTEAM.md` (which described a state the codebase
> has since climbed past), the F#-practices slice plan of
> `AUDIT_2026_06_02_FSHARP_EIGHT_AXIS_REDTEAM.md` (slices 0–12, landed), the
> latent-calculus finding of `WAVE_6_MORPHOLOGY.md`, the survey instrument of
> `TRANSFER_ISOMORPHISM_SUBSTANTIATION.md`, and a fresh file:line read of the
> transfer/diff machinery at HEAD.
>
> **Discipline of this document (per the `NORTH_STAR.md` rev-3 discipline).**
> Every gap below carries (a) a file:line citation, (b) its ladder level today
> (L1 witness / L2 faithful / L3 composed), (c) the named refusal or silent
> erasure it is, and (d) a buildable closing slice with an acceptance witness.
> If a claim here cannot be reduced to a file:line, a green test name, or a
> named trigger, it does not belong, and a future agent should cut it.
>
> **Who should read this:** the agent opening the next Wave-6 slice. Read §0
> (how to use), §1 (where we actually are), §2 (the fidelity ledger), then the
> §3 cluster you are about to build. §4 is the critical path; §5 tells you what
> is gated on the UAT capability survey vs buildable now.

---

## 0. How to use this document

This is a **debrief + backlog**, not a vision. The vision is `NORTH_STAR.md`;
the buildable path is `EXECUTION_PLAN.md` Wave 6; the why-the-climb-exists is the
two red-team audits. This document's single job is to put **everything you need
to start and finish the L2/L3 climb in one place**, reconciled against the code
as it exists at HEAD, so you don't have to re-derive the current state from a
dozen stale planning surfaces.

- **§1** reconciles the North-Star matrix against HEAD. Read it first — most of
  the planning docs still describe the 2026-05-31 state, which the codebase has
  climbed past. This section is the corrected ground truth.
- **§2** is the **fidelity ledger**: every known silent erasure, named refusal,
  and round-trip boundary, with file:line and ladder level. This is the
  evidence base.
- **§3** is the **slice backlog**: ten work clusters (A–J), each a set of
  buildable slices with scope, files, signatures, acceptance witnesses, size,
  risk, dependencies, and survey-gating.
- **§4** is the dependency graph and the critical path.
- **§5** is the survey-dependent vs survey-independent split (what you can build
  today vs what waits on the UAT capability survey, OPEN-2).
- **§6** is the discipline subset that will bite during this work.
- **§7** is the consolidated defer-with-trigger registry.
- **§8** is the explicit not-doing list.
- **§9** maps the slices back to the North-Star §5 falsifiable acceptance
  criteria — the definition of "the climb is done."
- **§10** is the source map (every file:line this debrief cites).

**The one-sentence orientation.** The engine has the adjunction, the live
`migrate` square (T16), and the durable episode substrate. The data plane's
refusal discipline is strong (fail-loud execute-gate, exit-9 on drops). The
remaining L2/L3 work is concentrated on the **schema/diff plane** (the
`CatalogDiff` captured-surface boundary, unenforced FK-trust, unreconstructed
indexes, a silent cross-schema FK filter) and the **T-VI spanning axes**
(permissions, transactionality, connection pre-flight) — plus the
**self-verification meta-cell** (a generated matrix that reports its own ladder
level, which would have caught every drift in this ledger automatically).

---

## 1. Where we actually are — the matrix reconciled against HEAD

### 1.1 The bullseye, restated

The engine is one adjunction between a logical Model and a physical Substrate:

```
Project  : Model     ──►  Substrate          (Π — emit)
Ingest   : Substrate ──►  Model              (ReadSide — the reader leg)
Law      : Ingest ∘ Project = identity       (modulo named, declared erasures)
```

The bullseye is this adjunction made **total** — an isomorphism ladder, not a
checkbox, on every axis:

- **L1 — witness present.** A named round-trip test exists and is live.
- **L2 — faithful.** The round-trip loses nothing *silently*: every erasure is a
  `Tolerance` entry, a structured diagnostic, or a fail-loud refusal — never a
  silent drop.
- **L3 — composed.** The axis is orthogonal (no hidden coupling) and participates
  in the one-command `migrate A B`.

Two properties hold across every cell by construction: **determinism (T1)** —
same inputs → byte-identical output, no clock/randomness/IO in Core; and
**classification totality (pillar 9)** — every transformation is `DataIntent` or a
named `OperatorIntent` overlay.

### 1.2 What landed since the 2026-05-31 red-team (the climb so far)

The five-axis red-team (`AUDIT_2026_05_31_FIVE_AXIS_REDTEAM.md`) measured a state
where every axis was an L1 witness with at least one **silent** erasure, two axes
coupled, the basis did not span the migration, and the composed operation did not
exist. **Most of that has been closed.** Verified against the git log and the code
at HEAD:

| Landed | Commit(s) | What it closed |
|---|---|---|
| **6.A.1** transfer drop fail-loud | exit-9 `DroppedReferencesExit` (`TransferRun.fs:511`) | Data: exit-0 row drops → named refusal |
| **6.A.2 / 6.A.3** surrogate-capture refusals | execute-gate (`TransferRun.fs:242-265`) | Data: cyclic + composite `AssignedBySink` refused, not mis-keyed |
| **6.A.4** empty-string↔NULL | `Tolerance.EmptyTextNormalizedToNull` (`Tolerance.fs:58-70`) | Data: conflation named (not silent) |
| **6.A.5 / 6.A.6** un-hollow ReadSide + NOCHECK FK emit | `d35bcf2`, `2f5ae80` | Schema/Decision: FK-trust **recovered**; NOCHECK FK reproduced |
| **6.A.7** Synthesized-key rename | `CatalogDiff.synthesizedRenameWarnings` (`CatalogDiff.fs:306-331`) | Identity: silent re-key → surfaced warning |
| **6.A.10** attribute-level diff | `AttributeFacet/AttributeChange/AttributeDiff` (`CatalogDiff.fs:27-61`) | Schema/Time: column-shape changes now visible to the diff |
| **6.A.13** schema CDC-silence gate | `MigrationRun` CDC pre-flight (`MigrationRun.fs:290-299`) | Idempotent redeploy emits zero DDL or refuses on CDC-tracked |
| **6.B.1** Decision↔Data pre-flight | `Preflight.dataViolatesTightening` (`Preflight.fs`) | T-V: NOT-NULL-on-NULL tightening refused pre-write |
| **6.B.2** RefactorLog-aware Transfer | `adb2b98`, `14ab675` | T-V: renamed column re-pointed by rename map, not ordinal |
| **6.D.1** `migrate A B` (L3) | `a45b7c6`, `5bb6ccc`, `c19b304` | The composed operation **exists and runs live** (T16) |
| **6.H.1/2/4** durable provenance | `Episode` + `LifecycleStore` + `ChangeManifest` (`57176c2`) | Time: durable episode chain; FTC over durable provenance |
| **F#-practices slices 0–12** | `8090833`…`7492459` | IR illegal-state collapses, lens/CE adoption, analyzer gap, test pruning |

**Net:** the matrix has advanced from "five L1 witnesses, all with silent
erasures" to "L1 5/5 with most data-plane erasures closed, the L3 migrate square
live, and the durable substrate in." The remaining work is the **schema/diff
plane L2 holes**, the **T-VI spanning axes**, and the **self-verification
meta-cell**.

### 1.3 The current matrix, cell by cell (reconciled)

`scripts/matrix-status.sh` → `NORTH_STAR.matrix.generated.md` reports **L1 = 5/5,
gate PASS, L2 live/C/D = 83/6/1** as of 2026-06-01T11:54Z. That generator reports
**L1 witness-presence only** — it does **not** report per-axis L2/L3 level. The
table below is the *hand-reconciled* ladder state, which is exactly the artifact
§3 cluster D proposes to make machine-generated.

| Axis | L1 witness | L2 faithful — status at HEAD | L3 composed into `migrate A B` |
|---|---|---|---|
| **Schema** | ✅ `PhysicalSchema diff` | ◑ FK-trust recovered but **not enforced** (`ReadSide.fs:110`); **indexes read but not reconstructed** (`ReadSide.fs:877`, `Tolerance.IndexesUnreflected`); **cross-schema FK rows silently filtered** (`ReadSide.fs:580`) | ◑ `migrate` diff **excludes references/indexes/sequences** (`CatalogDiff.fs:380-388`) — only column-shape evolves |
| **Data** | ✅ `data canary` | ✅ drops fail-loud (exit-9); cyclic/composite `AssignedBySink` refused; empty-string↔NULL named tolerance | ◑ cross-substrate load runs; not yet atomic/resumable (T-VI) |
| **Identity** | ✅ `reload preserves SsKey` | ◑ `OssysOriginal` faithful; `Synthesized`-rename **surfaced as warning**, not threaded | ◑ rename map consumed by Transfer (6.B.2); reconciliation rule still operator-supplied |
| **Time** | ✅ `replayTo genesis` | ◑ `applyDiff (between A B) A = B` holds **on the captured surface only** (`CatalogDiff.fs:370-388`); refactorlog time pinned to a constant (`ChangeDateTime`) | ◑ `migrate` plan emits ALTER for column-shape; no FK/index/sequence ALTER |
| **Decision** | ✅ `reproduces the DecisionOverlay` (nullability) | ◑ uniqueness + FK-trust **recovered on readback** (6.A.5) but the **full 3-axis adjunction round-trip is unwitnessed**; FK-trust **not gated** | ◑ tightening pre-flight (6.B.1) closes the Decision→Data coupling |
| **— T-VI spanning —** | | **Permissions / Transactionality / Connection pre-flight: only the Decision↔Data tightening pre-flight (6.B.1) exists.** The other three gates are unbuilt. | A write-denied sink, a mid-load crash, or a dead endpoint can still corrupt or no-op silently |
| **— meta —** | | The matrix generator reports **L1 presence, not L2/L3 level** — the drift between "ReadSide un-hollowed" and "indexes still dropped" is **invisible to the machine surface today** | The self-verification cell (§3.D) is the keystone |

---

## 2. The fidelity ledger — every known gap

This is the evidence base. Each row is a place the round-trip is not yet L2, or
the composed operation is not yet L3. The "ladder" column is the current state;
the "closing slice" points into §3.

### 2.1 The master gap table

| # | Gap | Location | Axis | Ladder today | Refusal / erasure | Closing slice |
|---|---|---|---|---|---|---|
| G1 | `CatalogDiff` captured surface **excludes references, FKs, indexes, modality, module structure, sequences** — they ride through `applyDiff` unchanged | `CatalogDiff.fs:380-388` | Schema, Time | L2 partial (column-shape only) | structural — round-trip law witnessed only on captured axes | **C1** (the widening) |
| G2 | FK-trust (`IsNotTrusted`) is **carried faithfully** through the round-trip (recovered + reproduced as NOCHECK + witnessed by F2), per F1's option (b) — not gated, since no fixture demands the refusal | `ReadSide.fs:1075` | Schema, Decision | ✅ **CLOSED** (F1/F2 Decision plane; G14 illegal-state guard landed) | none (carry, not gate) | *(closed)* |
| G3 | Non-PK index **structure** read, reconstructed (`attachIndexes`), and now **compared** (`PhysicalSchema.Indexes`: owner + name + uniqueness + ordered key columns) | `PhysicalSchema.fs` `PhysicalIndex`; `ReadSide.readIndexes` | Schema | ✅ **CLOSED** (E1) — round-trips emit/deploy/ReadSide (witness `IndexRoundtripTests`); the prior `IndexesUnreflected` retired | narrower residual: index *options* (filter / included / storage) → `IndexOptionsUnreflected` (OpenGap) | *(structure closed; options residual named)* |
| G4 | Cross-schema FK rows whose `SCHEMA_NAME()` is NULL (dropped schema / missing `VIEW DEFINITION` grant) — was an opaque `GetString` cast failure (live `readSchemaCombined`) / blank-and-drop (`readForeignKeys`) | `ReadSide.fs` `ForeignKeyReadback` | Schema | ✅ **CLOSED** (E2) — both FK readers route through the pure `ForeignKeyReadback.classify`, which emits a NAMED diagnostic + skip on an unreadable coordinate | named diagnostic, not silent / opaque | *(closed)* |
| G5 | T-VI **Permissions** — a write-denied sink is refused before mutation | `Preflight.permissionPreflight`; `TransferRun.spanningPreflight`; `Program.fs:1222` | spanning | ✅ **CLOSED** (A2, wired into transfer + migrate `--execute`; refuses with `transfer.insufficientGrant`) — only the live grant-matrix probe vs a real least-privilege account is survey-gated | named refusal | *(closed; grant probe survey-gated)* |
| G6 | T-VI **Transactionality** — mid-transfer failure leaves a **half-populated target**; no atomic boundary, idempotent retry, or rollback | (no axis) | spanning | ◑ **DEFERRED** (A3) — survey-dependent / PROD-empty per the premise re-prioritization (`WAVE_6_ONTOLOGY.md` §10); not on the survey-independent path | none yet | **A3** (trigger: PROD gains data) |
| G7 | T-VI **Connection pre-flight** — "both endpoints live + credentialed" before mutation | `Preflight.connectionPreflight`; `TransferRun.spanningPreflight`; `Program.fs:1209/1440` | spanning | ✅ **CLOSED** (A1, wired into transfer + migrate `--execute`) | named refusal | *(closed)* |
| G8 | `migrate` CLI verb live `--execute` leg (against a deployed DB) | `Program.fs:1051` (the `--execute` arm) | L3 face | ✅ **CLOSED** (B1) — the live square is operator-reachable, R6-gated | n/a | *(closed)* |
| G9 | Refactorlog `ChangeDateTime` carries the **episode's real `At`** | `RefactorLogRender.fs:17-25` | Time | ✅ **CLOSED** (P6/C3) — the real-clock path threads the episode clock; the pinned `2000-01-01` is retired to a no-production-caller legacy overload | n/a | *(closed)* |
| G10 | Change-manifest records **displacement** — per-channel move counts (`Channels`), schema norm (`SchemaNorm = CatalogDiff.norm`), refactorlog xref, CDC capture count | `ChangeManifest.fs:13-30` | Time, meta | ✅ **CLOSED** (6.H.4/C4) | n/a | *(closed)* |
| G11 | `CatalogDiff.compose` consumer — cross-episode `δ₁ + δ₂` fold | `Episode.fs:239`; `Lifecycle.fs:194` | Time | ✅ **CLOSED** (6.H.3/C2) — both `Episode.netDiff` and `Lifecycle.netDiff` fold `CatalogDiff.compose` over the evolution chain | n/a | *(closed)* |
| G12 | Decision adjunction — `Ingest(deploy(Project(C, overlay)))` reproduces the overlay on **all three** axes (nullability + uniqueness + FK-trust) | `CanaryRoundTripTests.fs` `E3:` | Decision | ✅ **CLOSED** (F2) — the unified compositional witness; per-axis proofs (A42 / 6.A.8 / readside-fktrust) now joined by one overlay round-trip carrying all three intents | n/a | *(closed)* |
| G13 | Matrix generator reports **L1 presence, not L2/L3 level** — witness-present ≠ faithful drift is invisible to the machine surface | `scripts/matrix-status.sh` | meta | ✅ **CLOSED** (D1+D2, this round) — generator now reports the per-axis L1/L2/L3 ladder, derives L2 from `Tolerance.fs` `@ladder` tags (Schema=L2-partial names `IndexOptionsUnreflected`), and is wired into CI (`verifiability-projection.yml`) with a byte-deterministic currency gate | n/a | *(closed)* |
| G14 | `Reference` boolean pair expressed the illegal quadrant `(HasDbConstraint=false ∧ IsConstraintTrusted=false)` (untrusted without a constraint) | `Catalog.fs` `Reference.withConstraintState` | Schema, Decision | ✅ **CLOSED** (bounded guard) — `isConstraintStateConsistent` + the normalizing `withConstraintState` (every producer routes through it); witness `ReferenceConstraintStateTests` | normalized to vacuous-trust | *(closed; full DU collapse deferred)* |
| G15 | Phase-2 deferred-FK UPDATE keys on **source PK** — for `AssignedBySink` the sink minted a fresh surrogate, so the WHERE matches zero rows | `TransferRun.fs:77-92` | Data | L2 (refused at gate 6.A.2) | **named refusal** (`TransferRun.fs:250-256`) | *(closed — documented for completeness)* |
| G16 | `AssignedKey` is single-string — composite surrogate truncated to first leg | `SurrogateRemap.fs:34`; `TransferRun.fs:227` | Data | L2 (refused at gate 6.A.3) | **named refusal** (`TransferRun.fs:258-264`) | *(closed — documented for completeness)* |
| G17 | FK-orphan rows silently dropped at `remapRowFks`, **after the write executes** — skip-and-diagnose, no pre-write gate | `SurrogateRemap.fs:227-228`; `TransferRun.fs:166, 196` | Data | L2 (exit-9 surfaces the drop) | skip-and-diagnose + exit-9 | *(partially closed — see A3 for the pre-write atomic boundary)* |
| G18 | Speculative-optics cluster (`Prism`, `PassContext`, `LineageTree`, `Certificate`, `DiagnosticLattice`) defined + law-tested, **zero production consumers** | `Diagnostics.fs:802-986`, `Lineage.fs:660-735` | meta | defer-with-trigger (Slice 12) | deletion contract at cutover+1 | **H1** |
| G19 | `PhysicalSchema` physical-comparison VOs (`PhysicalColumn`/`LogicalNameBinding`/`PhysicalForeignKey`, `Sequence`) stay `string`-typed | `PhysicalSchema.fs`, `Catalog.fs:240-242` | Schema | deferred-with-trigger (audit Slice 5) | documented asymmetry | **G2-phys** |
| G20 | Only 6 of 51 bench labels fire in the gating canary — perf regressions can hide | `bench/baseline-canary.json`; `PERF_OPPORTUNITIES.md` | perf | structural | n/a | **J1** |

### 2.2 The shape of the ledger

Read the table by **plane**, and the picture is clear:

- **The data plane is the strongest.** Every data-plane erasure is either closed
  (G15, G16) or surfaced (G17 via exit-9, G4-data via tolerance). The
  execute-gate (`TransferRun.fs:242-265`) is a genuine fail-loud refusal surface:
  unsatisfiable cycles, cyclic `AssignedBySink`, and composite `AssignedBySink`
  all refuse *before* any write. This is the codebase's discipline working.

- **The schema/diff plane was the deep L2 hole — now largely closed.** G1 (the
  captured-surface boundary) landed with C1 (`migrate A B` now sees references /
  indexes / sequences). The read-leg blind spots are closed: **G3** (index
  structure reconstructed *and* compared — E1), **G4** (cross-schema FK now a named
  diagnostic, not silent/opaque — E2), **G2/G12** (FK-trust carried + the 3-axis
  decision adjunction witnessed — F1/F2). The remaining schema-plane items are
  *hygiene*: **G14** (`Reference` illegal-state modeling — no illegal state is
  actually produced, just expressible) and the narrowed **`IndexOptionsUnreflected`**
  residual (index filter/included/storage options).

- **The meta-cell (G13) — the keystone — landed (D1/D2).** The generated matrix now
  reports each axis's L1/L2/L3 level (not just witness presence), derives L2 from
  `Tolerance.fs`'s `@ladder` tags, and is wired into CI. The exact drift this
  paragraph warned about ("6.A un-hollowed ReadSide" vs "indexes still dropped at
  reconstruction") is now a *build-visible* artifact: Schema reports L2-partial
  naming its open tolerance, and flips to L3 automatically when it retires.

- **T-VI spanning remains the highest-stakes surface — but is survey-gated.** G5/G6/G7
  (permissions / transactionality / connection) are where a write-denied sink, a
  mid-load crash, or a dead endpoint could still corrupt a target. Per the premise
  re-prioritization (`WAVE_6_ONTOLOGY.md` §10; PROD-empty), A3 (atomic/resumable)
  is **deferred-with-trigger** until PROD gains data or a write-denied environment
  enters a real flow — it is not on the survey-independent path.

---

## 3. The backlog — slices ordered by leverage

Ten clusters, A–J. Within each, slices carry: **principle/totality** cited;
**scope**; **files + line ranges**; **signatures** where the type shape is
load-bearing; **acceptance witness** (the backtick-cited test name to write);
**size** (S/M/L); **risk**; **dependencies**; **gating** (survey-independent vs
survey-dependent per §5).

The leverage order across clusters: **A (T-VI pre-flights) and C1 (the
captured-surface widening) are co-equal #1** — A is the only place data is
silently corrupted; C1 is the only place the L3 `migrate` claim overstates what
it round-trips. **D (the generated matrix)** is the keystone that makes the whole
climb self-verifying. **B (migrate wiring)** is the operator-facing payoff but
**must sit behind A and C1**.

---

### Cluster A — Close T-VI spanning (the pre-flight gate suite)

> **STATUS — A1 + A2 gates LANDED 2026-06-02 (as composable functions).** The
> `Preflight` module now carries the connection gate (A1 —
> `connectionViolations` / `connectionPreflight`, refusing
> `migrate.connectionUnavailable`) and the permission gate (A2 —
> `permissionViolations` / `permissionPreflight` / `captureGrantEvidence`,
> refusing `migrate.insufficientGrant`), plus a `Preflight.all` short-circuiting
> composition. 8 pure DB-free witnesses in `PreflightTests.fs` (`` ``A1: …`` `` /
> `` ``A2: …`` ``). **Survey-gated residuals:** A2's grant capture probes
> database-scope today; object-scope refinement waits on survey P1. **A3
> (transactional)** is a documented scaffold only — the resumable-upsert
> wrapper in `TransferRun.writePlan` needs the survey's granularity decision
> (P6/P11) + a Docker witness. **Wiring the gates into `migrate --execute`** is
> cluster **B1** (the gates exist and are tested; B1 threads `Preflight.all`
> before any mutation).

**Totality:** T-VI (spanning) + T-V (orthogonality). **Promise:** operator covenant
#8 (atomic-or-resumable; refuses rather than corrupts). **Home:** extend
`Preflight.fs` (today it holds only the 6.B.1 tightening check) into a
**pre-flight gate suite** that every `--execute` path must pass.

The architectural shape: a `Preflight` module family of pure-where-possible gates,
each returning `Task<Result<unit>>`, composed into one `preflightAll` that
`migrate`/`transfer --execute` runs *before* any mutation. Each gate is a **named
fail-loud refusal** with an operator-facing message, mirroring
`Preflight.tighteningPreflight`'s existing shape.

#### A1 — Connection pre-flight (G7)

- **Scope:** before any write, prove both substrates are open + the identity is
  the expected login. A dead or misconfigured endpoint refuses here, not
  mid-load.
- **Files:** `src/Projection.Pipeline/Preflight.fs` (add
  `connectionPreflight : SqlConnection -> SqlConnection -> Task<Result<unit>>`);
  wire into `MigrationRun.execute` (`MigrationRun.fs:271-334`) and the transfer
  `--execute` path (`Program.fs:480-597`).
- **Signature:**
  ```fsharp
  /// 6.C — both endpoints live + credentialed before mutation begins.
  /// Refuses `migrate.connectionUnavailable` rather than failing mid-write.
  let connectionPreflight
      (source: SqlConnection) (sink: SqlConnection) : Task<Result<unit>>
  ```
- **Acceptance:** `` ``6.C connection pre-flight: a closed sink refuses before any write`` `` (Docker-gated; open the sink connection, close it, assert the named refusal + zero writes).
- **Size:** S. **Risk:** low. **Deps:** none. **Gating:** survey-independent (the gate shape is local; the specific probe — `SELECT SUSER_SNAME()` / `@@SPID` liveness — is generic).

#### A2 — Permission pre-flight (G5)

- **Scope:** prove the sink grant actually covers the planned DML/DDL. The engine
  already plans the operations (the `Statement list` for schema; the
  `DataLoadPlan` for data); the gate proves the grant spans them via
  `sys.fn_my_permissions` (probe P1 of the survey).
- **Files:** `Preflight.fs` (add `permissionPreflight`); needs the planned
  operation set from `MigrationArtifacts.SchemaStatements`
  (`MigrationRun.fs:13-19`) and the transfer's target kinds.
- **Signature:**
  ```fsharp
  /// 6.C — the sink grant covers the planned writes. Reads sys.fn_my_permissions
  /// at the object scope of every planned INSERT/ALTER; refuses
  /// `migrate.insufficientGrant` with the first uncovered object.
  let permissionPreflight
      (sink: SqlConnection) (planned: PlannedWrite list) : Task<Result<unit>>
  ```
- **Acceptance:** `` ``6.C permission pre-flight: a write-denied sink refuses before transferring zero rows`` `` (deploy with a DML-only login lacking INSERT on a target kind; assert the named refusal, not a clean exit).
- **Size:** M. **Risk:** medium (the grant-shape assertion depends on what the
  UAT login actually exposes). **Deps:** none structurally. **Gating:**
  **survey-dependent for the exact grant matrix** (survey probes P1/P2/P3 resolve
  which DML the managed login permits) — but the *gate scaffold + the
  sys.fn_my_permissions probe* are buildable now against the local Docker canary.

#### A3 — Transactional / resumable envelope (G6, G17)

- **Scope:** wrap the transfer in an atomic-or-resumable boundary. A mid-load
  crash either rolls back to a clean target or resumes idempotently — never
  leaves a half-populated target. This also upgrades G17 (FK-orphan drops) from
  "surfaced after the write" to "surfaced inside an atomic boundary."
- **Files:** `src/Projection.Pipeline/TransferRun.fs` (`writePlan`,
  `TransferRun.fs:141-197`) — wrap Phase 1 + Phase 2 in a transaction or a
  chunk-commit loop with an idempotent retry key; `MigrationRun.execute`
  (`MigrationRun.fs:271-334`) for the schema+data composition.
- **Design note:** the survey (probe P11: `BEGIN TRAN … COMMIT`, `@@LOCK_TIMEOUT`;
  probe P6: `DELETE` vs `TRUNCATE`) determines the granularity — full-transaction
  vs chunk-commit-with-resume. Build the **resumable** shape (idempotent upsert
  keyed by the surrogate remap) as the default, since it survives both a managed
  login that forbids long transactions and a CDC-tracked target where a single
  giant transaction is hostile.
- **Acceptance:** `` ``6.C transactional envelope: a mid-load failure leaves the target unchanged or resumable`` `` (inject a failure after Phase 1; assert the target is either empty or the resume completes it without duplication).
- **Size:** L. **Risk:** high (interacts with CDC-silence, the two-phase deferred-FK model, and the managed-login transaction policy). **Deps:** A1 (connection), A2 (permission). **Gating:** **survey-dependent** (granularity decision waits on P11/P6); the resumable-upsert scaffold is buildable now.

**Cluster A close condition:** `preflightAll` composes A1+A2+A3 and is the
mandatory gate on every `--execute`. The North-Star T-VI counterexamples
(write-denied sink, mid-transfer half-populate, dead endpoint) all refuse loudly.

---

### Cluster C — Activate the change-calculus (the schema/diff plane L2 core)

This cluster is the structural heart of the L2/L3 climb. **C1 is the centerpiece.**

#### C1 — Widen the `CatalogDiff` captured surface (G1) ★ centerpiece

> **STATUS — diff algebra LANDED 2026-06-02.** The Reference / Index / Sequence
> channels are wired through `between` / `applyDiff` / `isEmpty` / `norm` /
> `channelCounts` / `compose` (`CatalogDiff.fs`); the round-trip law
> `applyDiff (between A B) A = B` now holds on the widened surface, witnessed by
> 11 tests in `CatalogDiffTests.fs` (`` ``C1: …`` `` — added/changed/dropped FK,
> added/changed index incl. the `Options` default-substitution guard, added/
> reshaped sequence, the integrative all-three round-trip, the isEmpty honesty
> regression guard, and the norm channel counts).
>
> **STATUS — emitter-side LANDED 2026-06-02 (safe-additive).** `SchemaMigrationEmitter`
> now emits the new channels: an **added FK** → `ALTER TABLE ADD CONSTRAINT …
> FOREIGN KEY` (new `Statement.AlterTableAddForeignKey` variant + ScriptDom
> builder reusing `foreignKeyConstraint`; renders to real T-SQL); an **added
> index** → `CREATE INDEX`; an **added sequence** → `CREATE SEQUENCE`; a
> **Trust-only FK change** → the WITH NOCHECK two-step (6.A.6). Every other
> change/removal **refuses fail-loud** with a named Error (`migration.destructive*`
> / `migration.unsupported*`) — same add-vs-refuse discipline as the attribute
> channel. 7 emitter witnesses in `SchemaMigrationEmitterTests.fs` (incl. a
> render-to-T-SQL assertion on the FK-add path). `migrate A B` now produces a
> real minimum-viable ALTER for an added FK/index/sequence instead of failing
> at verify.
>
> **STATUS — destructive emission LANDED 2026-06-02 (under `--allow-drops`).**
> `SchemaMigrationEmitter.emitWith allowDrops` now emits the destructive DDL the
> operator accepted: **DROP COLUMN** (new `AlterTableDropColumn`), **DROP
> CONSTRAINT** (new `AlterTableDropConstraint`, for a removed FK + the DROP leg
> of an FK reshape), **DROP INDEX** (new `DropIndex`, for a removed index + the
> DROP leg of an index reshape), **DROP SEQUENCE** (new `DropSequence`, removed +
> reshape DROP leg). Reshapes are DROP-then-recreate (FK: drop+ADD; index:
> drop+CREATE; sequence: drop+CREATE — value-preserving `ALTER SEQUENCE` is a
> noted refinement). With `allowDrops=false` (the default) every drop still
> refuses fail-loud (the gate holds). `MigrationRun.preview` threads `allowDrops`
> into `emitWith` so `migrate --allow-drops` actually emits them. 7 destructive
> witnesses in `SchemaMigrationEmitterTests.fs` (each render-asserted for DROP
> COLUMN/CONSTRAINT/INDEX/SEQUENCE). The new-variant blast radius is the same 4
> sites as `AlterTableAddColumn` (Statement / ScriptDomBuild / Deploy /
> DacpacEmitter).
>
> **Positioning (2026-06-02 clarification).** This whole emitter — additive and
> destructive — is the IMPERATIVE `migrate --execute` executor's, NOT the
> declarative SSDT deploy artifact. Per `WAVE_6_ONTOLOGY.md` §4, the declarative
> path emits the target CREATE-TABLE model + `.refactorlog` and **DacFx computes
> the ALTER/DROP at publish** (a drop is absence + DacFx's data-loss flags); the
> emissions here are unnecessary for that path. They serve only the live-square
> T16 in-place evolver + the verification lens, and they never feed the `.dacpac`
> (`DacpacEmitter.isSchemaStatement` now excludes the imperative-migration
> family by construction). See `DECISIONS 2026-06-02 — Clarification: …lens, not
> the declarative deploy artifact`.

- **Totality:** T-I (round-trip faithfulness) on Schema + Time; the forcing
  instance for L3 `migrate`.
- **The problem, precisely.** `CatalogDiff.between` (`CatalogDiff.fs:224-276`)
  partitions kinds into Renamed/Added/Removed/Unchanged and descends to
  attribute-level facets (`AttributeFacet`, 9 facets:
  `DataType | Nullability | PrimaryKey | Length | Precision | Scale | Identity |
  DefaultValue | Computed`). But the captured surface **explicitly excludes
  references, FKs, indexes, modality, module structure, and sequences**
  (`CatalogDiff.fs:380-388`). `applyDiff` (`CatalogDiff.fs:440-490`) transforms
  only the captured axes; everything else rides through from the base unchanged.
  **Consequence:** the round-trip law `applyDiff (between A B) A = B` is
  witnessed only on the captured surface, and `migrate A B` (which composes
  `between → emit → execute`) **silently no-ops** any FK / index / sequence
  change between A and B.
- **Scope.** Add the missing change channels to the diff algebra. Three new
  facet families, each with its `between`-side detection and its `applyDiff`-side
  reconstruction:
  1. **Reference channel** — added / removed / re-targeted / trust-changed FKs
     per kind. (Couples to G2/G14 — the FK-trust facet lives here.)
  2. **Index channel** — added / removed / changed indexes per kind. (Couples to
     G3 — requires `Kind.Indexes` to be reconstructed first; see E1.)
  3. **Sequence channel** — added / removed / reshaped sequences (start /
     increment / min / max / cycle / cache).
- **Signatures (extend the existing shapes):**
  ```fsharp
  // New per-kind reference diff, sibling to AttributeDiff
  type ReferenceFacet = Target | Trust | OnUpdate | OnDelete | Presence
  type ReferenceChange = { ReferenceKey: SsKey; Facets: Set<ReferenceFacet> }
  type ReferenceDiff =
      { Added: Set<SsKey>; Removed: Set<SsKey>
        Renamed: Map<SsKey, RenameRecord>; Changed: ReferenceChange list }

  // New per-kind index diff
  type IndexFacet = Columns | Uniqueness | Filter | IncludeColumns | Options
  type IndexChange = { IndexKey: SsKey; Facets: Set<IndexFacet> }
  type IndexDiff =
      { Added: Set<SsKey>; Removed: Set<SsKey>; Changed: IndexChange list }

  // CatalogDiffData gains:  ReferenceDiffs : Map<SsKey, ReferenceDiff>
  //                         IndexDiffs     : Map<SsKey, IndexDiff>
  //                         SequenceDiff   : SequenceDiff
  ```
- **`applyDiff` arms.** Each new channel needs its reconstruction arm in
  `transformKind` (`CatalogDiff.fs:443-451`) and the catalog-level apply
  (`CatalogDiff.fs:453-490`). **Watch the smart-constructor default-substitution
  bomb** (see §6): every `{ Reference.create … with … }` / `{ Index.create … with … }`
  reconstruction MUST set every field the diff touches explicitly, or it inherits
  the `true` default for `IsConstraintTrusted` / `AllowRowLocks` / `AllowPageLocks`.
- **`isEmpty`, `norm`, `channelCounts`, `compose` follow.** Extend `isEmpty`
  (`CatalogDiff.fs:356-361`) to require the new channels empty (so CDC-silence
  6.A.13 stays honest); extend `channelCounts` (`CatalogDiff.fs:513-525`) and
  `norm` (`CatalogDiff.fs:531-534`) with the new move counts; extend `compose`
  (`CatalogDiff.fs:547`) to fold the new channels.
- **The emitter side.** `migrate`'s `MigrationRun.preview` already refuses
  schema changes the emitter can't express (`MigrationRun.fs:106-109`) — but it
  can only refuse what `between` *computes*. Once the channels are computed, the
  `SchemaMigrationEmitter` must emit the corresponding `ALTER TABLE ADD/DROP
  CONSTRAINT`, `CREATE/DROP INDEX`, `ALTER SEQUENCE` (or fail-loud refuse the ones
  DacFx owns). Per `WAVE_6_ONTOLOGY.md`: **DacFx owns the schema ALTER; the engine
  owns the data movement measured by CDC** — so the emitter may delegate the
  structural ALTER to DacFx and keep the engine's job the displacement
  accounting.
- **Acceptance (three witnesses):**
  - `` ``C1: between/applyDiff round-trips an added FK (reference channel)`` ``
  - `` ``C1: between/applyDiff round-trips an added/changed index (index channel)`` ``
  - `` ``C1: between/applyDiff round-trips a reshaped sequence`` ``
  - and the integrative `` ``C1: applyDiff (between A B) A = B on the widened surface`` `` over a generated A/B pair that differs on all channels.
- **Size:** L (the largest single L2 lever). **Risk:** medium-high (the
  default-substitution bomb; CDC-silence interaction; the emitter/DacFx
  ownership split). **Deps:** E1 (index reconstruction) for the index channel;
  G14 `Reference` modeling (G1-ref) makes the reference channel cleaner.
  **Gating:** survey-independent (proven against the local ephemeral canary).

#### C2 — Wire `CatalogDiff.compose` (G11)

- **Scope.** `compose : CatalogDiff → CatalogDiff → CatalogDiff`
  (`CatalogDiff.fs:547`) exists but has no consumer. Wire it into multi-episode
  recombination: `δ₁ + δ₂` over the durable `LifecycleStore` so "a move in episode
  i recombining with episode j" is a *fold*, not a single diff. Flips
  A-Lifecycle-4 from Bucket-C to operational; earns T13's `compose`.
- **Files:** `LifecycleStore` consumer; `Lifecycle.netDiff` (the integral `∫δ`).
- **Acceptance:** `` ``C2: compose folds a two-episode evolution chain (δ₁ + δ₂ = between A C)`` ``.
- **Size:** M. **Risk:** low (the algebra is built and law-tested). **Deps:** C1 (so compose folds the full surface). **Gating:** survey-independent.

#### C3 — Refactorlog against-prior + real episode time (G9)

- **Scope.** Accumulate the refactorlog (read prior `.refactorlog`, dedup by
  `OperationKey`, append) and thread the boundary clock into `ChangeDateTime`
  (today pinned to a constant). Core stays clock-free; the Pipeline stamps it as
  `ApprovalRecord.At` already does. This is `EXECUTION_PLAN.md` 6.F.1 = morphology F6.
- **Files:** `RefactorLogEmitter`; `MigrationRun` (thread the episode clock).
- **Acceptance:** `` ``C3: refactorlog accumulates against prior, dedup by OperationKey, episode-clock-stamped`` ``.
- **Size:** M. **Risk:** low. **Deps:** 6.H episode substrate (landed). **Gating:** survey-independent.

#### C4 — Change-manifest integrates the displacement (G10)

- **Scope.** Make `ChangeManifest` integrate **δ, not just state**: per-move
  `‖δ‖` by channel (added/removed/renamed/reshaped), the refactorlog xref, the
  **per-run tolerance residual** (not the static `Tolerance.allKnown`
  enumeration), the `AppliedTransforms` **outcome** (not just axis), and the CDC
  capture series `k`. This is morphology F5 — the `∂²κ/∂emission∂episode`
  observability.
- **Design note (F4).** Do **not** build a model-plane `RowDiff`. The data δ stays
  substrate-fused (the MERGE predicate *is* the comparison); what reifies on the
  data plane is the **norm** `‖·‖` as the realized CDC capture count, read back
  post-hoc and recorded into the manifest.
- **Files:** `src/Projection.Core/ChangeManifest.fs`; the SSDT manifest emission
  (6.H.4).
- **Acceptance:** `` ``C4: change-manifest records per-channel ‖δ‖ + tolerance residual + AppliedTransforms outcome + CDC series`` ``.
- **Size:** M. **Risk:** low. **Deps:** C1 (the channels), C3 (refactorlog xref). **Gating:** survey-independent for the schema side; the CDC-series read is survey-independent (local canary).

**Cluster C close condition:** `applyDiff (between A B) A = B` holds on the
*full* schema surface (references, indexes, sequences), the manifest integrates
the displacement, and the time-integral (compose + accumulated refactorlog) is
durable. This is the Schema + Time axes reaching L2-faithful and L3-composed.

---

### Cluster B — Make `migrate A B` operator-reachable (the L3 face)

#### B1 — Wire `migrate --source-conn --sink-conn --execute` (G8)

> **STATUS — LANDED 2026-06-02.** The CLI now carries
> `projection migrate --to <modelB.json> --conn <env|file:ref> --execute
> [--allow-drops] [--allow-cdc]` (`runMigrateExecute`, `Program.fs`):
> flag-order-independent parse; **R6-gated** (`PROJECTION_ALLOW_EXECUTE=1` or
> exit 7); the **A1 connection pre-flight runs before any mutation**; then
> `MigrationRun.executeFromLive` reads the deployed state A live, evolves it in
> place to B, reads B' back, and reports VERIFIED / refused (the live square,
> T16). Smoke-verified without a DB: R6 refusal → exit 7, missing args → exit 2,
> read-fail → exit 6, flag-order independence. Live execution is covered by the
> existing Docker `MigrationCanaryTests` (`execute` / `executeWithData`).
>
> **STATUS — A2 permission gate WIRED + cross-substrate form LANDED 2026-06-02.**
> The in-place executor now reads state A live, **previews to derive the planned
> writes** (`plannedWritesOf` maps each schema statement to its sink write), and
> runs the **A1 connection + A2 permission pre-flights** (`migratePreflights`:
> `captureGrantEvidence` + `permissionPreflight`) before any mutation, then
> `MigrationRun.execute`. A2's grant capture is database-scope (object-scope is
> the survey-gated P1 refinement). **The cross-substrate data-load form**
> (`runMigrateWithData`) now exists: `migrate --to <modelB> --sink-conn <ref>
> --source-conn <ref> --execute [--allow-drops] [--allow-cdc]` evolves the sink
> A→B then transfers rows from the source over contract B via
> `MigrationRun.executeWithData` (the data leg runs only if the schema verified).
> Straight load today; the `--reconcile` / `--user-map` re-key forms are the
> follow-on (the `transfer` verb already carries that machinery).

- **Scope.** Today `migrate --from --to [--allow-drops]` is a **dry-run plan
  emitter** from two JSON models (`Program.fs:803, 902-904`). The live square
  (`MigrationRun.execute`, `MigrationRun.fs:271-334`) runs only under test. Wire
  the live verb by reusing Transfer's connection apparatus
  (`ConnectionResolver` / `TransferConnections` in `Transfer.fs:84-111`), gated
  by `PROJECTION_ALLOW_EXECUTE=1` (R6) **and** `Preflight.preflightAll` (cluster A).
- **Files:** `src/Projection.Cli/Program.fs` (the `migrate` match arm, ~`:902`);
  `src/Projection.Cli/` arg parsing (mirror `TransferArgs.fs`).
- **Acceptance:** `` ``B1: migrate --execute evolves a deployed A to B through the pre-flight suite, fail-loud on refusal`` `` (Docker-gated A→B canary).
- **Size:** M. **Risk:** medium (it exposes the live mutation path to the operator
  — every §A gate must be in place first). **Deps:** **A1, A2, A3 (hard
  dependency — do not ship B1 without the pre-flight suite), C1 (so the diff
  doesn't silently no-op FK/index/sequence changes the operator expects).**
  **Gating:** the CLI wiring is survey-independent; the *live execution against
  real UAT* (EXECUTION_PLAN 5.1) is survey-dependent (OPEN-2).

**Why B1 sits behind A and C1.** The North-Star covenant promise #8 is "moves the
whole estate touching only what changed … atomic-or-resumable … refuses rather
than corrupting." Shipping a live `migrate --execute` before A (it could corrupt
a write-denied / mid-crashed target) or before C1 (it could silently skip an FK
the operator added) would make the engine **claim** promise 8 while violating it —
the exact silent-fidelity-illusion the North Star forbids.

---

### Cluster D — The self-verification meta-cell (the keystone)

> **STATUS — D1 + D2 LANDED (round D→E).** `scripts/matrix-status.sh` now emits
> the per-axis **L1/L2/L3 ladder** into `NORTH_STAR.matrix.generated.md`: **L1**
> from round-trip witness presence, **L2** from `Tolerance.fs`'s machine-readable
> `@ladder <Variant> <Axis> <Disposition>` tags (an `OpenGap` variant caps its
> axis at L2-partial and is named — Schema reports `IndexesUnreflected`), **L3**
> from `migrate A B` witness presence. The generator cross-checks that every live
> `ToleratedDivergence` (per the `name` single-source-of-truth) carries exactly
> one tag (exit 3 on drift) and is **byte-deterministic** (no wall-clock stamp).
> D2 wired `verifiability-gate.sh` + a matrix-currency diff gate into CI
> (`.github/workflows/verifiability-projection.yml`). Witnesses:
> `tests/Projection.Tests/MatrixLadderTests.fs` (3, green). The honesty
> mechanism: retiring an `OpenGap` variant auto-flips its axis — L2 cannot be
> hand-marked. **E1 retiring `IndexesUnreflected` will flip Schema to L3
> automatically** — the matrix now *measures* the rest of this cluster's progress.

#### D1 — Generated per-axis L2/L3 matrix (G13)

- **Totality:** T-IV (documentation totality) + the North-Star §5 criterion 5.
- **Scope.** `matrix-status.sh` → `NORTH_STAR.matrix.generated.md` reports L1
  witness-*presence* (5/5). Extend it to report each cell's **ladder level**
  (L1 / L2 / L3), derived from the proof: the `AxiomTests.fs` buckets, the
  `Tolerance` set (a named erasure = L2, an un-named gap = L1), the
  `TransformRegistry` coverage, and the per-feature canary witnesses. A cell goes
  L2 only when every erasure on its axis is a `Tolerance`/diagnostic/refusal
  (not a silent drop); L3 only when it participates in a green `migrate` witness.
- **Files:** `scripts/matrix-status.sh`; a new generator step reading the
  tolerance set + the axiom buckets; `tests/Projection.Tests/AxiomTests.fs` (the
  per-feature totality entries).
- **Acceptance:** `` ``D1: the generated matrix reports Schema=L2-partial because IndexesUnreflected is an open tolerance`` `` — i.e. the generator *names* G3 automatically.
- **Size:** M. **Risk:** low. **Deps:** none (but it *measures* the others, so it
  pays compounding interest). **Gating:** survey-independent.

#### D2 — Verifiability gate in CI (E1 from the endgame backlog)

- **Scope.** Wire `scripts/verifiability-gate.sh` into CI so no surface can claim
  a coverage bucket its `AxiomTests.fs` evidence does not support; a phantom
  Bucket-A claim fails the build. (Already runs locally; this lands it in CI.)
- **Files:** `.github/workflows/`; `scripts/verifiability-gate.sh`.
- **Acceptance:** CI red on a hand-edited phantom claim.
- **Size:** S. **Risk:** low. **Deps:** D1 (they share the proof-reading
  machinery). **Gating:** survey-independent.

**Why D is the keystone.** G3 (indexes still dropped) hid behind "6.A un-hollowed
ReadSide" until a manual read found it. A generated L2/L3 matrix makes that drift
a **build-visible artifact**: the moment an axis has an open tolerance or an
un-witnessed sub-axis, the matrix says so, in CI. This is what makes "trust
unnecessary" literally operational — the engine reports its own distance to the
bullseye.

---

### Cluster E — Close the silent-erasure residuals

> **STATUS — reconnaissance + E2 landed (round D→E).** Three corrections to this
> cluster's recorded state, found by a file:line re-read at HEAD:
> - **E2 — LANDED.** Both FK readers (`readSchemaCombined` live + `readForeignKeys`
>   out-of-band) route through the pure, public `ReadSide.ForeignKeyReadback.classify`,
>   which classifies a NULL/blank `SCHEMA_NAME()` as `Unreadable` and surfaces a
>   NAMED diagnostic + skip. (Correction: today's behaviour was an *opaque
>   `GetString` cast failure* on the live path, not the recorded "silent filter" —
>   E2 still closes G4 honestly: opaque/silent → named.) Witnesses:
>   `tests/Projection.Tests/ForeignKeyReadbackTests.fs` (5, green).
> - **E3 — durable substrate already guarded.** The `CatalogCodec` reconstruction
>   arms (`readIndex`/`readReference`/`readKind`) set every persisted field
>   explicitly, and `CatalogCodecTests.fs` exercises the bomb-prone defaults
>   (`AllowRowLocks=false`, `AllowPageLocks=false`, `NoRecomputeStatistics=true`,
>   `IgnoreDuplicateKey=true`, `IsConstraintTrusted=false`) under the round-trip
>   law. The LifecycleStore folds over the codec, so its substrate is covered. The
>   remaining E3 surface (ReadSide's index field-fidelity) is **interlocked with E1**
>   and best done there.
> - **E1 — LANDED (structure), residual narrowed.** `PhysicalSchema` now carries an
>   `Indexes` axis (`PhysicalIndex`: owner + name + uniqueness + ordered key
>   columns) — exactly the surface `ReadSide.readIndexes` recovers, so both canary
>   halves stay symmetric. The non-PK index structure round-trips emit/deploy/
>   ReadSide (witness `tests/Projection.Tests/IndexRoundtripTests.fs` — a UNIQUE
>   single-col + a non-unique two-col index, Docker). Rather than over-claim L3, the
>   honest move: `IndexesUnreflected` is retired and replaced by the *narrower*
>   `IndexOptionsUnreflected` (still `OpenGap` Schema) for the un-compared index
>   options (filter / included columns / storage flags — recovered by neither side).
>   So Schema stays L2-partial, now naming the narrower residual, and D1's witness is
>   preserved (renamed in `MatrixLadderTests.fs`). Closing `IndexOptionsUnreflected`
>   (extend `readIndexes` + widen `PhysicalIndex`) is the next step that flips Schema
>   to L3 — the matrix measures it.

#### E1 — Reconstruct `Kind.Indexes` (G3) — 3-part

- **Scope.** Indexes are *read* (`readIndexes`, `ReadSide.fs:429-465`) but **not
  reconstructed** into `Kind.Indexes` (hardcoded `[]` at `ReadSide.fs:877, 1004`),
  and `PhysicalSchema` ignores them (`Tolerance.IndexesUnreflected`,
  `Tolerance.fs:43-49`). Three parts:
  1. **ReadSide populates `Kind.Indexes`** from the index read (watch the
     `Index.create` default-substitution bomb: `AllowRowLocks` / `AllowPageLocks`
     default `true`).
  2. **`PhysicalSchema` extends its comparison surface** to include indexes (a new
     `Indexes` axis, mirroring the `LogicalNameBindings` precedent from chapter D).
  3. **Retire `Tolerance.IndexesUnreflected`** once the round-trip preserves them.
- **Files:** `src/Projection.Adapters.Sql/ReadSide.fs:877, 1004` (the two
  `Indexes = []` sites — note there are **two** `buildKind` paths; both must be
  fixed); `src/Projection.Core/PhysicalSchema.fs`; `src/Projection.Core/Tolerance.fs`.
- **Acceptance:** `` ``E1: a UNIQUE/filtered index survives emit/deploy/ReadSide and is reflected in PhysicalSchema`` ``.
- **Size:** M. **Risk:** medium (two reconstruction paths; the PhysicalSchema
  comparison widening). **Deps:** none (but it unblocks the C1 index channel).
  **Gating:** survey-independent.

#### E2 — Surface the cross-schema FK filter (G4)

- **Scope.** FK rows whose `SCHEMA_NAME()` is NULL (dropped schema / missing
  `VIEW DEFINITION` grant) are **silently filtered** at `ReadSide.fs:580`. Per the
  no-silent-drop boundary axiom, replace the silent filter with a **structured
  diagnostic** naming the reference that could not be reconstructed.
- **Files:** `src/Projection.Adapters.Sql/ReadSide.fs:570-595`.
- **Acceptance:** `` ``E2: an unreadable cross-schema FK surfaces a diagnostic, not a silent drop`` ``.
- **Size:** S. **Risk:** low. **Deps:** none. **Gating:** survey-independent.

#### E3 — Audit 6.H IR-reconstruction sites for the default-substitution bomb

- **Scope.** Per the totality-contract discipline (`DECISIONS 2026-06-01`), audit
  every `{ X.create … with … }` reconstruction in `LifecycleStore`,
  `ChangeManifest`, `MigrationRun.renameStatements`, and the new C1 `applyDiff`
  arms. Verify (#fields set by `create`) + (#fields in `with`) = total field
  count, so no field silently inherits a constructor default.
- **Files:** `LifecycleStore`, `ChangeManifest.fs`, `MigrationRun.fs:189-257`,
  `CatalogDiff.fs:440-490`.
- **Acceptance:** a totality-contract comment at each reconstruction site + a
  test asserting a round-tripped `Reference`/`Index` preserves its non-default
  fields.
- **Size:** S. **Risk:** low (but the bomb is *invisible* — the compiler does not
  flag the omission). **Deps:** C1 (audit its new arms). **Gating:** survey-independent.

---

### Cluster F — The Decision adjunction (the "stronger than V1" theorem)

> **STATUS — CLOSED (round D→E).** F1 took the recommended option (b): FK-trust is
> *carried faithfully* through the round-trip (`IsConstraintTrusted` recovered at
> `ReadSide`; a NOCHECK FK reads back untrusted, witnessed) rather than gated — no
> fixture demands the refusal. F2 (G12) landed the **unified 3-axis adjunction
> witness** `E3: Ingest(deploy(Project(C, overlay))) reproduces the decision
> overlay on all three axes (nullability + uniqueness + FK-trust)` in
> `CanaryRoundTripTests.fs` (Docker, green) — one overlay carrying all three
> intents survives emit→deploy→ReadSide at once, proving the axes compose. This
> was unblocked by E1 (uniqueness became readable via the index axis). The Decision
> cell is 3/3 sub-axes. North-Star §5 criterion 2 is met.

#### F1 — Gate FK-trust on readback (G2)

- **Scope.** `IsNotTrusted` is recovered (`ReadSide.fs:1075`) but **not acted
  on**. Decide the contract: either (a) `migrate`/`transfer` refuses to replicate
  an untrusted FK without an explicit operator acknowledgement, or (b) the trust
  state is faithfully carried through the round-trip and the Decision adjunction
  (F2) witnesses it. Recommended: (b) — carry it; (a) only if a real fixture
  demands the refusal.
- **Files:** `src/Projection.Adapters.Sql/ReadSide.fs:110, 1075`; the
  `Reference` consumers.
- **Acceptance:** folded into F2's witness. **Size:** S. **Deps:** G1-ref
  (modeling). **Gating:** survey-independent.

#### F2 — Witness the 3-axis decision adjunction (G12)

- **Scope.** Write the property test: `Ingest(deploy(Project(C, overlay)))`
  reproduces `overlay` on **all three** tightening sub-axes — nullability +
  uniqueness + FK-trust. 6.A.5 un-hollowed the uniqueness + FK-trust readback;
  this closes the loop as a single adjunction witness so the engine's *opinions*
  provably survive the round-trip. This is the theorem that makes "replace V1"
  stronger than "trust V1."
- **Files:** `tests/Projection.Tests/` (new `DecisionAdjunctionTests.fs` or extend
  the existing overlay round-trip); `AxiomTests.fs` (the E3 entry).
- **Acceptance:** `` ``E3: Ingest(deploy(Project(C, overlay))) reproduces overlay on nullability + uniqueness + FK-trust`` ``.
- **Size:** M. **Risk:** medium (needs E1 indexes + F1 trust-gate to be readable).
  **Deps:** E1, F1. **Gating:** survey-independent (local canary).

---

### Cluster G — IR illegal-state modeling (faithfulness debt)

#### G1-ref — Model `Reference`'s trust-state (G14)

- **Scope.** `Reference` (`Catalog.fs:549-595`) carries
  `(IsUserFk, HasDbConstraint, IsConstraintTrusted, OnUpdate)` with expressible
  illegal states (`IsConstraintTrusted = true ∧ HasDbConstraint = false`). The
  audit deliberately deferred this (Slice 2 did `Index` + `ApprovalState` only)
  as "needs a deliberate domain modeling pass." Collapse the impossible quadrants
  — e.g. trust state lives *inside* the `HasDbConstraint`-present variant. This
  sits directly on the FK-trust axis (G2/F1) and the C1 reference channel, so
  doing it first makes both cleaner.
- **Files:** `src/Projection.Core/Catalog.fs:549-595`; the ~N reference consumers
  (let the compiler walk the blast radius).
- **Acceptance:** the illegal quadrant is unconstructable; existing reference
  tests green by the boolean-projection accessors.
- **Size:** M. **Risk:** medium (touches FK emit + readback + diff). **Deps:**
  none; **enables** C1-ref, F1, F2. **Gating:** survey-independent.

#### G2-phys — `PhysicalSchema` physical-comparison VO lift (G19)

- **Scope.** `PhysicalColumn` / `LogicalNameBinding` / `PhysicalForeignKey` and
  `Sequence` stay `string`-typed (audit Slice 5 deferred this as a documented
  asymmetry — the physical-comparison domain is "what SQL Server reports back,"
  where string-as-comparison-key is defensible).
- **Recommendation:** **keep deferred** per the audit's late-stage cost-benefit
  re-calibration, *unless* C1/E1 touch these types anyway — in which case
  co-locate the lift. Trigger: a real cross-domain identifier-confusion bug, or a
  major IR-shape pass touching these types. **Gating:** deferred-with-trigger.

---

### Cluster H — Resolve the speculative-optics cluster (G18)

#### H1 — Land the named consumer or honor the deletion clock

- **Scope.** `Prism`, `PassContext`, `LineageTree`, `Certificate`,
  `DiagnosticLattice` (`Diagnostics.fs:802-986`, `Lineage.fs:660-735`) are defined
  + law-tested with **zero production consumers**. Slice 12 gave each a
  defer-with-trigger + a deletion contract (cutover + 1 chapter). The North-Star
  discipline — "a feature that is not a corollary of the adjunction is probably
  the wrong feature" — means the in-between state must not rot.
- **Note.** `Prism` is literally the bidirectional Catalog↔DDL partial-accessor
  seam — the most adjunction-shaped of the set. If C1's `applyDiff`/`between`
  reconstruction wants a total-vs-partial lens for the FK/index channels, `Prism`
  is its natural home. Adopt it there *or* delete on schedule.
- **Acceptance:** each surface is either consumed (with the consumer named) or
  deleted per its §7 trigger. **Size:** S–M (per surface). **Risk:** low.
  **Deps:** C1 may consume `Prism`. **Gating:** survey-independent.

---

### Cluster I — The generality frontier (named, cut)

#### I1 — Second source adapter (DACPAC reader) — **CUT until trigger**

- **Scope.** The Model is source-typed; an adjunction does not care it came from
  OutSystems. A DACPAC reader (`Ingest` from a non-OSSYS substrate) would make
  source-agnosticism true *by construction* and is the natural L2 stress-test of
  the `Ingest` leg.
- **Status.** Per North-Star §7: **stays cut.** Source-agnosticism is claimed only
  as far as a second adapter proves it. **Trigger:** a real second source. Named
  here so a future agent recognizes the frontier and refuses it until the trigger
  fires.

---

### Cluster J — Perf enabler

#### J1 — The all-51-label operator-reality canary (G20)

- **Scope.** Only 6 of 51 bench labels fire in the gating canary. Before the
  remaining perf sweep (`PERF_OPPORTUNITIES.md` ranks 6–34 open), land a canary
  that exercises all 51 labels so regressions can't hide. Pre-condition for the
  perf sweep, not the L2/L3 climb — lower priority, but a structural enabler.
- **Files:** the operator-reality canary fixture; `bench/baseline-canary.json`
  (re-record with `PERF_GATE_RECORD=1` + a DECISIONS amendment naming the new
  floor).
- **Acceptance:** the perf gate reads all 51 labels. **Size:** M. **Risk:** low.
  **Deps:** none. **Gating:** survey-independent.

---

## 4. Dependency graph & critical path

```
                    G1-ref (Reference modeling)
                       │
            ┌──────────┼───────────────┐
            ▼          ▼                ▼
   E1 (indexes)   F1 (FK-trust gate)   C1-ref channel
       │   │            │                   │
       │   └────────────┼───────────────────┤
       ▼                ▼                   ▼
  C1 index channel   F2 (decision      C1 (captured-surface
       │              adjunction)        widening) ★
       └──────┬───────────────────────────┘
              ▼
        C2 (compose)   C3 (refactorlog)   C4 (manifest δ)
              └──────────────┬─────────────┘
                             ▼
   A1 ─┐                 (schema/time L2 complete)
   A2 ─┼─► preflightAll ──────────────┐
   A3 ─┘                              ▼
                                 B1 (migrate --execute)  ← THE L3 PAYOFF
                                      │
                              (gated on UAT survey for live UAT exec)

   D1 (generated matrix) ──► D2 (CI gate)     [measures all of the above;
                                               build early — it pays compounding
                                               interest and catches drift]

   E2, E3, H1, J1   — independent; schedule opportunistically
   G2-phys, I1      — deferred-with-trigger
```

**The critical path to a fully-green L2/L3 schema axis + a trustworthy live
`migrate`:**

```
G1-ref → E1 → C1 → {A1,A2,A3} → B1
                 ↘ C2,C3,C4 (time-integral)
                 ↘ F1,F2 (decision adjunction)
```

**Build D1 in parallel from day one** — it is the cheapest way to make every
subsequent slice's progress (and any regression) machine-visible.

**Estimated depth:** G1-ref (M) + E1 (M) + C1 (L) + C2/C3/C4 (3×M) + A1/A2/A3
(S+M+L) + B1 (M) + F1/F2 (S+M) + D1/D2 (M+S). Roughly **8–10 engineered weeks**
for the full schema/time/decision L2 + T-VI spanning + the live migrate verb +
the self-verifying matrix — with the survey-dependent portions (A2 grant matrix,
A3 granularity, B1 live-UAT) gated on the ops spike.

---

## 5. Gating — survey-independent vs survey-dependent

The operator is running a UAT **capability survey** (OPEN-2; the instrument is
§2 of `TRANSFER_ISOMORPHISM_SUBSTANTIATION.md`, 11 probes P1–P11). It resolves
which `IdentityDisposition` the managed UAT login permits and which DML/DDL/
transaction shapes are available. **Do not block the climb on it** — most of the
backlog is survey-independent.

| Survey-INDEPENDENT (build now) | Survey-DEPENDENT (wait / scaffold-only) |
|---|---|
| C1 (captured-surface widening) | A2 grant-matrix specifics (P1/P2/P3) — scaffold the gate now |
| C2, C3, C4 (calculus activation) | A3 transaction granularity (P11/P6) — build the resumable-upsert default now |
| E1, E2, E3 (residuals) | B1 *live UAT execution* (EXECUTION_PLAN 5.1) — wire the CLI now, gate live exec |
| F1, F2 (decision adjunction) | The set-based `MERGE…OUTPUT` capture (§3 of the substantiation doc) — trigger-gated on P4 + a measured per-row bottleneck (OPEN-5) |
| D1, D2 (the generated matrix + CI gate) | |
| A1 (connection pre-flight) | |
| G1-ref (Reference modeling) | |
| H1 (optics resolution), J1 (perf canary) | |

**Rule:** build the survey-independent gate *scaffold* for A2/A3 now (the
`Preflight` module shape, the `sys.fn_my_permissions` probe, the resumable-upsert
loop); fold the survey results into the specific assertions when they land. When
the survey returns, fold P3/P10 into the disposition decision
(`PreservedFromSource` vs `ReconciledByRule` vs `AssignedBySink` mix) per §2 of
the substantiation doc.

---

## 6. Disciplines that will bite (internalize before writing code)

- **Build the whole solution + `bash scripts/test.sh fast` before "done."** The
  only ground truth. **Never** run the pure + Docker pools as one `dotnet test`
  (OOM on this 4-core/15GiB host) — use `scripts/test.sh` (`fast` / `docker` /
  `canary` / `all`). New SQL/async tests route through `TaskSync.run`, never bare
  `(task).GetAwaiter().GetResult()`.
- **The smart-constructor default-substitution bomb** (the codec discipline's
  named hazard). In any `{ Reference.create … with … }` / `{ Index.create … with … }`
  / `{ Kind.create … with … }` reconstruction, **every field the diff touches must
  appear in the `with` block**, or it silently inherits the constructor default
  (`IsConstraintTrusted` / `AllowRowLocks` / `AllowPageLocks` / `IsActive` all
  default `true`). The compiler does **not** flag the omission. Detection:
  (#fields set by `create`) + (#fields in `with`) = total field count. This bites
  C1, E1, E3 hardest.
- **No silent drops (boundary axiom).** Any concept the engine cannot carry
  surfaces as a `Tolerance` entry, a structured diagnostic, or a fail-loud
  refusal — never a silent filter (the G4/E2 lesson).
- **Closed-DU / record-field change → let the compiler drive the blast radius.**
  Change the source sites, build, read the exhaustiveness/field-missing errors;
  don't hand-hunt. C1's new facet DUs and G1-ref's `Reference` reshape will light
  up every consumer.
- **`PhysicalSchema.diff` is skeleton-only by design.** It deliberately does NOT
  compare indexes or FK-trust today (the `IndexesUnreflected` tolerance). E1 part
  2 *changes* that — but until it lands, schema witnesses for indexes/trust must
  assert on the **reconstructed `Catalog`** directly (find the kind, assert
  `Kind.Indexes` / `Reference.IsConstraintTrusted`), not on `PhysicalSchema.isEqual`.
- **ReadSide marks every row-carrying reconstructed table `Modality.Static`** (the
  Wave-4 4.4 trap) — irrelevant to most of this work but it bites profiling code;
  `compare` clears it first.
- **Adjunction is structural; widen `ofCatalog` AND `ofStatementStream` in
  lockstep.** When E1/C1 widen a `PhysicalSchema` axis, the H-050 adjunction
  (`PhysicalSchema.ofCatalog c = ofStatementStream (SsdtDdlEmitter.statements c)`)
  requires extending both projections *and* the statement-stream emitter together,
  or the AdjunctionLawTests break.
- **DacFx owns the schema ALTER; the engine owns the data movement** (per
  `WAVE_6_ONTOLOGY.md`). C1's emitter side should delegate the structural ALTER to
  DacFx where appropriate and keep the engine's job the displacement accounting +
  the CDC-measured data movement.
- **HANDOFF.md is append-only within a chapter; never overwrite.** When you close
  a slice here, prepend the letter; don't Write-overwrite.
- **DECISIONS is for resolved questions.** When a slice here resolves a question
  (e.g. the FK-trust contract in F1, or the transaction granularity in A3), append
  a DECISIONS entry; keep session narrative out.

---

## 7. Consolidated defer-with-trigger registry

| Surface / slice | Trigger to build | If trigger doesn't fire by… | Source |
|---|---|---|---|
| **A2 grant-matrix specifics** | UAT survey P1/P2/P3 return | n/a (scaffold builds now) | §5 |
| **A3 transaction granularity** | UAT survey P11/P6 return | n/a (resumable-upsert default builds now) | §5 |
| **B1 live UAT execution** | UAT survey resolves OPEN-2 (writable SQL connection) | n/a (CLI wiring builds now) | §3.B |
| **Set-based `MERGE…OUTPUT` capture** | survey P4 confirms + a real `AssignedBySink` load makes per-row capture the measured bottleneck (OPEN-5) | — | substantiation §3 |
| **G2-phys** (PhysicalSchema VO lift) | a cross-domain identifier-confusion bug, or a major IR pass touching these types | — | audit Slice 5 |
| **I1** (second source / DACPAC reader) | a real second source | — | North-Star §7 |
| **`Prism`** | C1 wants a total-vs-partial lens for FK/index channels (Catalog↔DDL) | cutover + 1 chapter → delete | F#-audit Slice 12 |
| **`PassContext`** | real reader-comonad pressure (≥5 parameter layers) | cutover + 1 chapter → delete | Slice 12 |
| **`LineageTree`** | Cluster C (speculative execution / policy diff) | cutover + 1 chapter → delete | Slice 12 |
| **`Certificate`** | H-009 multi-target fanout | cutover + 1 chapter → delete | Slice 12 |
| **`DiagnosticLattice`** | operator-triage CLI surface | cutover + 1 chapter → delete | Slice 12 |

---

## 8. What we are explicitly NOT doing

- **No model-plane `RowDiff`.** The data δ is correctly substrate-fused (the MERGE
  predicate is the comparison); what reifies on the data plane is the **norm**
  `‖·‖` as the realized CDC capture count (morphology F4). Building a value-level
  `RowDiff` would duplicate the substrate's job.
- **No second-source adapter yet** (I1). Named as the post-cutover generality
  frontier; cut until a real second source fires the trigger.
- **No set-based `MERGE…OUTPUT` capture yet.** Per-row `INSERT…OUTPUT`
  (`TransferRun.fs:101-130`) is correct until P4 confirms the grant permits MERGE
  *and* a measured per-row bottleneck justifies it.
- **No `Reader<'env,'a>` monad / `reader { }` CE.** Parameter-passing remains
  correct in F# at this layer; `PassContext` is the deferred-with-trigger form.
- **No `PhysicalSchema` VO lift** (G2-phys) absent a co-located major pass or a
  real confusion bug — the audit's late-stage cost-benefit re-calibration holds.
- **No perf sweep before J1.** The all-51-label canary is the pre-condition;
  sweeping before it lets regressions hide.
- **No relaxation of any cutover-safety commitment.** R6 split-brain governance,
  the T-30/T-15 gates, V1-stays-warm-through-cutover+30 all hold. `--execute`
  stays gated behind `PROJECTION_ALLOW_EXECUTE=1`.

---

## 9. Acceptance — when is the climb done

Mapped to the North-Star §5 falsifiable criteria. The climb is complete when all
hold simultaneously, each a test or a generated artifact:

| North-Star §5 criterion | Closed by | Witness |
|---|---|---|
| **1. Round-trip matrix fully green at L2** (every axis faithful; every erasure named) | C1, E1, E2, E3, F1, F2, plus the closed 6.A.* | per-axis canary witnesses; zero un-named silent drops |
| **1b. Axes are an orthogonal, spanning basis (T-V + T-VI)** | A1, A2, A3 (spanning); 6.B.1/6.B.2 (orthogonality, landed) | `preflightAll` refuses the T-VI counterexamples |
| **1c. The composed operation exists and round-trips at L3** | C1 (so `migrate` doesn't silently no-op) + B1 (operator-reachable) + A (atomic-or-resumable) | green A→B canary; `migrate --execute` through the pre-flight suite |
| **2. The decision adjunction holds** | F2 | `` ``E3: Ingest(deploy(Project(C, overlay))) reproduces overlay on 3 axes`` `` |
| **3. The verifiability gate is in CI** | D2 | CI red on a phantom Bucket-A claim |
| **4. The four-input algebra is complete** | C2/C3 (Lifecycle operationalized with a real consumer) | a 2-version evolution chain replays and composes refactor logs |
| **5. The coverage surfaces are generated** | D1 | the matrix reports per-axis L2/L3 level; a hand-edit divergence fails the build |
| **6. Determinism + classification totality hold across every cell** | (standing; do not relax) | the existing permutation-invariance + skeleton-purity properties |
| **7. The cutover-era criteria hold** | (standing; R6 / T-30 / T-15 / V1-warm) | the canary catches a real emitter bug with zero false negatives |

When criterion 5's generated matrix shows L2/L3 green on every cell **and** the
meta-cell holds (the generator reports its own level, in CI), the engine does not
make the cutover trustworthy — it makes trust unnecessary. That is the bullseye.

---

## 10. Source map — every file:line this debrief cites

**Code (HEAD `307ef65`):**
- `src/Projection.Core/CatalogDiff.fs` — `AttributeFacet/Change/Diff` (`:27-61`),
  `between` (`:224-276`), `attributeDiff` (`:176-204`), captured-surface exclusion
  (`:380-388`), `applyDiff` (`:440-490`), `isEmpty` (`:356-361`),
  `synthesizedRenameWarnings` (`:306-331`), `channelCounts` (`:513-525`), `norm`
  (`:531-534`), `compose` (`:547`).
- `src/Projection.Adapters.Sql/ReadSide.fs` — FK-trust recovery (`:110, :1075`),
  `Indexes = []` (`:877, :1004`), `readIndexes` (`:429-465`), cross-schema FK
  silent filter (`:570-595`, esp. `:580`), `readForeignKeys` (`:549-595`),
  `buildAttribute` defaults (`:597-699`).
- `src/Projection.Pipeline/TransferRun.fs` — `phase2UpdateSql` (`:77-92`),
  `insertCaptureRow` (`:101-130`), `writePlan` (`:141-197`), execute-gate refusals
  (`:242-265`), `DroppedReferencesExit` (`:511`).
- `src/Projection.Core/SurrogateRemap.fs` — `SourceKey`/`AssignedKey` (`:28, :34`),
  `IdentityDisposition` (`:65-68`), `capture` invariant (`:115-130`), `remapRowFks`
  (`:202-230`, empty-string `:219`, orphan drop `:227-228`).
- `src/Projection.Core/Migration.fs` — `MigrationPlan` (`:40-45`), `plan`
  (`:96-101`), `applyTo` / T16 (`:117-118`), `isIdempotent` (`:109`).
- `src/Projection.Pipeline/MigrationRun.fs` — `MigrationArtifacts` (`:13-19`),
  `MigrationError` (`:22-46`), `preview` (`:99-136`), `execute` (`:271-334`), CDC
  gate (`:290-299`), `renameStatements` (`:189-257`).
- `src/Projection.Pipeline/Preflight.fs` — `dataViolatesTightening`,
  `tighteningPreflight` (the 6.B.1 home for cluster A).
- `src/Projection.Core/Transfer.fs` — `Substrate`/`TransferConnections` (`:72-111`),
  `ConnectionRef` (`:66-68`).
- `src/Projection.Core/Tolerance.fs` — `IndexesUnreflected` (`:43-49`),
  `EmptyTextNormalizedToNull` (`:58-70`), `ToleratedDivergence` (`:25-125`).
- `src/Projection.Core/Catalog.fs` — `Reference` (`:549-595`), `Sequence`
  (`:240-242`).
- `src/Projection.Core/Diagnostics.fs` — optics cluster (`:802-986`).
- `src/Projection.Core/Lineage.fs` — `LineageTree` (`:660-735`).
- `src/Projection.Cli/Program.fs` — `migrate` plan-only (`:803, :902-904`),
  transfer `--execute` (`:480-597`), `PROJECTION_ALLOW_EXECUTE` (`:593-597`).
- `scripts/matrix-status.sh`, `scripts/verifiability-gate.sh`,
  `bench/baseline-canary.json`.

**Planning surfaces:**
- `NORTH_STAR.md` (the bullseye, the ladder, the covenant, §5 criteria),
  `NORTH_STAR.matrix.generated.md` (L1 5/5).
- `AUDIT_2026_05_31_FIVE_AXIS_REDTEAM.md` (the per-axis counterexamples — the
  state since climbed past).
- `AUDIT_2026_06_02_FSHARP_EIGHT_AXIS_REDTEAM.md` (slices 0–12, landed).
- `WAVE_6_MORPHOLOGY.md` (latent calculus; F1–F6; `:223-282`),
  `WAVE_6_ONTOLOGY.md` (the change moves; DacFx ownership), `WAVE_6_ALGEBRA.md`
  (T12–T16; the master equation).
- `TRANSFER_ISOMORPHISM_SUBSTANTIATION.md` (the survey §2; the MERGE capture §3).
- `EXECUTION_PLAN.md` Wave 6, `PERF_OPPORTUNITIES.md`, `HANDOFF_2026_06_02.md`.

---

*— Debrief recorded 2026-06-02 for the agent opening the next Wave-6 slice. The
data plane refuses loudly; the schema/diff plane has the deep L2 holes; T-VI is
the only place a target is still silently corrupted; the generated matrix is the
keystone that makes the climb self-verifying. Hold the spine. Complete the matrix.*
