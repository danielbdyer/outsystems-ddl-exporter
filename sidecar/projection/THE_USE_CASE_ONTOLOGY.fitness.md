# THE USE-CASE ONTOLOGY — Fitness Overlay (Pass Two: code mapped onto the matrix)

> **What this is.** The **current-state** companion to `THE_USE_CASE_ONTOLOGY.md` (the target-first
> masterwork). Where the masterwork describes the ideal end state, this overlay reports **where the
> code actually stands at HEAD against every cell** — each cell's ladder level (L1/L2/L3), file:line
> evidence, a verdict tag, and the gap. It is the result of a six-territory code survey (schema moves ·
> data moves & CDC · identity & transfer · provenance & migrate · gates · proteins end-to-end).
>
> **Relationship to the DEBRIEF.** `DEBRIEF_2026_06_02_ISOMORPHISM_CLIMB_AND_BACKLOG.md` is the prior
> current-state ledger, organized around *known gaps* (G1–G20). This overlay is organized around the
> *masterwork's cells* (so it carries a verdict on every cell, including the green ones) and **it
> corrects the DEBRIEF against HEAD** — several rows have closed since 2026-06-02, and the survey
> surfaced gaps the ledger never named (§1). When the two disagree, this overlay is the more recent
> reading; when this overlay is itself superseded by new work, refresh it first.
>
> **Reading the ladder.** **L1** = a named round-trip witness exists. **L2** = faithful — every
> erasure is named (a `Tolerance` entry, a structured diagnostic, or a fail-loud refusal), no silent
> drop. **L3** = composed — orthogonal and participating in the green one-command `migrate A B`.
> **Verdict tags:** `faithful-composed` (L3) · `faithful` (L2, no silent erasure) ·
> `partial-named-gap` (works but a named axis is incomplete) · `partial-SILENT-gap` (an erasure is
> *not* surfaced — the cardinal sin) · `built-not-wired` (function exists, no production call site) ·
> `scaffold` (documented intent, no implementation) · `absent`.

---

## 1 — DEBRIEF reconciliation against HEAD (what changed since 2026-06-02)

### 1.1 Rows that have CLOSED since the ledger

| DEBRIEF row | Ledger status | HEAD status | Evidence |
|---|---|---|---|
| **G1** — `CatalogDiff` captured surface excludes references/FKs/indexes/sequences | open (L2 partial) | **closed** — the C1 widening landed; `between`/`applyDiff`/`isEmpty`/`norm`/`channelCounts`/`compose` all cover `ReferenceDiff`/`IndexDiff`/`SequenceDiff` | `CatalogDiff.fs:63-168, 520-545, 661-669`; 11 tests in `CatalogDiffTests.fs` incl. `C1: isEmpty is honest — an added FK alone makes the diff non-empty` (`:650`) |
| **G3** — non-PK indexes read but reconstructed as `[]` | open (L1 + named tolerance) | **closed for the read path** — `attachIndexes` is wired; `Indexes = []` at `ReadSide.fs:1004` is the initial default, immediately overwritten by `attachIndexes` at `:1552-1560`. The `Tolerance.IndexesUnreflected` variant is now an **orphan** (E1 part 3 — retire it — not yet run) | `ReadSide.fs:1552-1560`; `Tolerance.fs:43-49` (orphan) |
| **G8** — `migrate` CLI is plan-only; live `execute` test-driven only | open (L3 in tests, not CLI) | **closed for schema-only execute** — `Program.fs:1168` dispatches `migrate --execute` to `runMigrateExecute` (→ `MigrationRun.execute`, `:1028`) and `runMigrateWithData` (→ `executeWithData`, `:1107`), both R6-gated (`PROJECTION_ALLOW_EXECUTE=1`) | `Program.fs:1168-1185`, `MigrationRun.fs:313` |

> **Caveat on G8.** Schema-evolution `migrate --execute` is operator-reachable, but the CLI execute
> path does **not** call `MigrationRun.record` (the episode is not persisted after a live run) and the
> cross-substrate `executeWithData` passes `Map.empty` reconciliation (so the CDC-aware data MERGE
> against *existing* data and the Dev→UAT re-key are not composed into the one command). So G8 is
> "schema-reachable," not "the full P-6 protein reachable" (see §4, P-6).

### 1.2 Gaps the survey surfaced that the ledger never named

| New gap | Plane | Verdict | Evidence |
|---|---|---|---|
| **N1** — Reference-level renames produce **no refactorlog entry** | schema/identity | `partial-named-gap` | `RefactorLogEmitter.emit` reads only `CatalogDiff.renamed` (kinds) + `AttributeDiff.Renamed` (columns); `ReferenceDiff.Renamed` is computed in the diff but never emitted — a renamed FK gets no `sp_rename` entry. `RefactorLogEmitter.fs:254-267` |
| **N2** — Computed columns **not excluded** from the MERGE comparable-column-set | data | `partial-SILENT-gap` (non-operational today) | `StaticSeedsEmitter.renderMerge` filters `UpdColumns` to non-PK, non-deferred only — no `Computed ≠ None` filter (`StaticSeedsEmitter.fs:160-164`). A persisted computed column would land in `UPDATE SET` → runtime SQL error, no pre-filter, no tolerance. Dormant because V1 JSON carries no computed metadata yet. |
| **N3** — ANSI-padding + decimal-scale tolerances **absent** | data | `partial-named-gap` (latent false-positive) | No `RTRIM`/scale-normalization in `ReadSide.formatRawValue` (`:720-769`) or `SqlLiteral.ofRaw`; a `'foo '` vs `'foo'` or `1.0` vs `1.00` difference would fire the change-detection predicate as a genuine change. Only the NULL axis and empty-string↔NULL are handled. |
| **N4** — `validate-user-map` **pre-flight gate absent** | identity | `partial-named-gap` | `UserRemapContext.isFullyMapped`/`unmatchedCount` exist (`UserRemap.fs:122-129`) but **no caller gates on them before the write**; orphans surface *post-write* via exit-9, not a pre-SQL halt. No `Preflight.validateUserMap`. |
| **N5** — `Preflight.all` has **zero production callers** | gates (meta) | `built-not-wired` | The composing gate (`Preflight.fs:310-318`) exists but `migratePreflights` hand-chains gates à la carte and `transfer` calls none — so P-GATE completeness is not structurally enforced; a caller can skip the suite. |
| **N6** — `ChangeDateTime` pinned to a constant is **unadvertised** | provenance/time | `partial-named-gap` | `RefactorLogRender.fs:55` pins `"2000-01-01T00:00:00Z"`; it is the concrete form of G9 but has **no `Tolerance` entry and no `AxiomTests` Skip stub** — an operator-invisible deferral. |
| **N7** — `compose`/`netDiff`/`ChangeManifest.*` are **test-only** | provenance/time | `built-not-wired` | `CatalogDiff.compose` (`:1003`), `Lifecycle.netDiff`, `EpisodicLifecycle.netSchemaDiff`, `ChangeManifest.between`/`series`/`pathLength` have **zero non-test callers** — the cross-episode recombination algebra is defined and law-tested but unwired (extends DEBRIEF G11). |
| **N8** — P-DM **general case** (`capture = k`) witnessed only as an inequality | data | `partial-named-gap` | CDC tests assert the floor (`= 0`) and the sensitivity (`post > baseline`), never the exact `capture = k` for k > 1 (`CdcSilenceTests.fs:239-317`). |

### 1.3 Rows the survey re-confirmed as open at HEAD

G2 (FK-trust read but ungated — `ReadSide.fs:1073-1075`, no planner/executor gate); **G4 (cross-schema
FK silent filter — `ReadSide.fs:580` — the most serious remaining `partial-SILENT-gap`)**; G6
(transactional envelope — scaffold only, `Preflight.fs:290-302`); G9 (refactorlog clock + no
accumulate-against-prior); G10 (change-manifest records state+axis, not the full displacement with
tolerance residual / `AppliedTransforms` outcome); G12 (decision adjunction witnessed on 1 of 3
sub-axes); G14 (`Reference` illegal states expressible); G17 (FK-orphan drop is exit-9 *post*-write —
A3 pre-write boundary absent); G18 (speculative-optics cluster, zero consumers); G19/G20.

---

## 2 — Schema-plane move fitness

| Move | Ladder @ HEAD | Verdict | Discriminating predicate witnessed? | Key gap | Evidence |
|---|---|---|---|---|---|
| **Add** | L2 | `partial-named-gap` | P-CMP: ✅ `C1: isEmpty is honest` — but **no** SsKey-collision adversarial test | G4 silent FK filter on readback; `IndexesUnreflected` orphan | `CatalogDiff.fs:484-487, 661-669`; `SchemaMigrationEmitter.fs:119-123, 217-231` |
| **Remove** | L2 | `partial-named-gap` | P-CH: ✅ structural (set-ops); named refusals for every destructive channel | G4 readback filter; Synthesized-key naked-rename only *warns* (not gated) | `SchemaMigrationEmitter.fs:173-176, 268-272, 329-333, 377-381` |
| **Rename** | L2 | `faithful` (column path) / `partial-named-gap` (overall) | P-RN: ✅ detected in Designation space; P-CH: ✅ `a rename alone emits no ALTER` (`SchemaMigrationEmitterTests.fs:138`) | N1 (reference renames → no refactorlog entry); N6 (`ChangeDateTime` pinned); `‖rename‖_data=0` no live CDC witness | `RefactorLogEmitter.fs:254-267`; `RefactorLogRender.fs:55` |
| **Reshape** | L2 | `partial-named-gap` | P-CMP over 9 facets: ✅ detected; **no** precision+scale-specific test | G2 (FK-trust ungated); G14 (illegal `Reference` states); non-alterable facets named-refused; sequence reshape destroys value (comment-only) | `CatalogDiff.fs:27-36, 264-274`; `SchemaMigrationEmitter.fs:130-142` |

**Deploy-mode axis.**

| Mode | Ladder @ HEAD | Verdict | Note |
|---|---|---|---|
| **Declarative (SSDT+DacFx)** | L2 | `partial-named-gap` | The DacFx-delegation seam is **correct**: `DacpacEmitter.isSchemaStatement` (`:77-86`) excludes the imperative ALTER/DROP from the declarative model. Gaps: G9 clock; no refactorlog accumulate-against-prior |
| **Imperative in-place lens (`migrate --execute`)** | L3 (column-shape) / L2 (full surface) | `faithful-composed` (column-shape) | Live Docker canary `a widening ALTER COLUMN executes and preserves data` (`SchemaMigrationCanaryTests.fs:53`); **no** Docker canary for the C1 FK/Index/Sequence channels through `execute`; G6 transactional unbuilt |
| **Fresh wipe-and-load** | L1 | `partial-named-gap` | Full `CreateTable` stream is correct; the refactorlog-accumulation gap means a fresh-env replay of *multi-sprint* rename history is incomplete (only the latest diff's renames are present) |

---

## 3 — Data-plane move fitness

The four-part MERGE anatomy, surveyed: **(a) null-safe distinctness predicate** — ✅ present, the
`IS DISTINCT FROM` three-arm expansion, ScriptDom-built (`ScriptDomBuild.fs:763-789`). **(b) ON keys
on reconciled identity** — ✅ via `PkColumns`, with surrogate substitution applied upstream in
`DataLoadPlan.build` before MERGE rows are built. **(c) comparable-column-set excludes
computed/surrogate/tolerance** — ⚠️ surrogate (PK) and deferred-FK excluded, **computed NOT excluded**
(N2). **(d) gated DELETE clause** — ❌ **absent everywhere** (zero `NotMatchedBySource` sites).

| Move | Ladder @ HEAD | Verdict | Discriminating predicate witnessed? | Key gap | Evidence |
|---|---|---|---|---|---|
| **Insert** | L2 | `faithful` | P-DM floor: ✅; general `=k`: only inequality (N8) | wipe-and-load fallback absent; N3 tolerances absent | `ScriptDomBuild.fs:845-862`; `CdcSilenceTests.fs:239` |
| **Update** | L2 | `faithful` | P-NOOP: ✅ `Assert.Equal(baseline, post)` on idempotent redeploy (`CdcSilenceTests.fs:239-271`) | N2 (computed not excluded — `partial-SILENT`, dormant); N3; N8 | `ScriptDomBuild.fs:830-843, 763-789` |
| **Unchanged** | L2 | `faithful` | P-DM floor: ✅ + 3 property sweeps + C0–C4 cross-emitter | N3 ANSI-pad could fire a false-positive (latent) | `CdcSilencePropertyTests.fs:278,290,302`; `CdcSilenceCrossEmitterTests.fs:330,361,405` |
| **Delete** | — | `absent` | P-DEL-SCOPE: not implemented | **No `WHEN NOT MATCHED BY SOURCE` arm** in any emitter or in Transfer `writePlan` | `ScriptDomBuild.fs:801-865` (no DELETE arm) |
| **Reidentify (data)** | L2 | `partial-named-gap` | P-REKEY: ✅ re-key canary `(Order → User-by-email)`; cyclic/composite refused at gate | G17 FK-orphan drop is exit-9 **post**-write; A3 pre-write boundary absent | `TransferRun.fs:242-265`; `TransferCanaryTests.fs:330,635` |
| **Move (cross-substrate)** | L2 / L3-in-tests | `partial-named-gap` | P-DM+P-NOOP: ✅ on the MERGE path; BulkCopy path guarded by CDC pre-flight refusal | Move/Measure/Record not in the `migrate --execute` CLI composition; wipe-and-load absent | `TransferRun.fs:141-197, 336-350` |

**CDC as the ruler.** The norm identity `‖δ‖_data = |capture|` is witnessed at the floor and the
sensitivity boundary; the exact `capture = k` general case (N8) and the `‖rename‖_data = 0` cross-plane
corollary have **no live witness**. CDC-as-provenance (the `cdc.<table>_CT` log as a consumable
evolution record) is not surfaced — the change-manifest carries a single `CdcCaptureCount` per episode,
not the series (G10).

---

## 4 — Identity, provenance, and protein fitness

### 4.1 Identity plane (PT-3)

| Cell | Ladder | Verdict | Note |
|---|---|---|---|
| Three name-spaces (Identity/Designation/Realization) | L2 | `faithful` | Four-variant `SsKey` DU; `Realization := Designation` via typed VOs; `Synthesized` bounded (`Identity.fs:45-298`) |
| P-ID (match by SsKey) | L2 | `faithful` | `reconcileKind` builds the sink index by **column value**, not ordinal (`Reconciliation.fs:59-68`) |
| Reidentify — AssignedBySink / PreservedFromSource | L2 | `faithful` | per-row `OUTPUT inserted.<pk>` capture; cyclic + composite refused at `executeGate` |
| Reidentify — ReconciledByRule / Dev→UAT re-key | L2 | `partial-named-gap` | re-key is an Update (FK re-point), match by email — but **N4: no pre-write `validate-user-map` gate**; orphans exit-9 post-write |
| Identity↔Schema rename coupling | L2 | `partial-named-gap` | `repointRow` re-points by Name not ordinal (`RenameProjectionTests.fs:57`); the reconcile+rename *combination* is a named follow-on (`TransferRun.fs:421`) |

### 4.2 Provenance / time / migrate (PT-4)

| Cell | Ladder | Verdict | Note |
|---|---|---|---|
| Accumulate (refactorlog against-prior + dedup + episode clock) | L2 partial | `partial-named-gap` | snapshot accumulation faithful; **refactorlog-against-prior absent**; `ChangeDateTime` pinned (G9/N6) |
| Snapshot/Diff/applyDiff (W1/W3) | L2 | `faithful` (captured surface) | **no-cheat P-RT₂ witnessed** (`CatalogDiffTests.fs:314`); modality + module-structure uncaptured |
| compose (W2) | L2 surface | `built-not-wired` | defined + law-tested, **zero production callers** (N7) |
| FTC reconstructLatest over disk-loaded chain | L3 | `faithful-composed` | the strongest claim — `previewFromStore` wires it; reload-from-disk reproduces B (`LifecycleStoreTests.fs:68`, `MigrationRunTests.fs:117`) |
| Norm ‖δ‖ (schema) | L2 | `faithful` | production caller in `Migration.previewOf` (`Migration.fs:103`); `‖rename‖_data=0` has no live witness |
| Change-manifest as displacement | L2 partial | `partial-named-gap` | carries channel counts + `SchemaNorm` + `CdcCaptureCount`; **missing** tolerance residual + `AppliedTransforms` outcome; **no production consumer** (G10/N7) |
| T16 — migrate composition | L3 | `faithful-composed` | structural + durable round-trip + **live SQL Server canary**; CLI `--execute` dispatched (G8 closed) |
| Comparison regimes (a)/(b)/(c) | (a) L3 / (b) L2 / (c) L2 | `faithful-composed` / `faithful` / `faithful` | (a) `previewFromStore` + `--store`; (b) `executeFromLive` (no P-IF on the noisy plan-A read); (c) `--from` two-model |

### 4.3 Gate fitness (PT-5)

| Gate | Status @ HEAD | Verdict |
|---|---|---|
| Connection pre-flight | wired on `migrate`, **not** on `transfer`; `Preflight.all` unused | `partial-named-gap` |
| Permission pre-flight | wired on `migrate` (**DB-scope only**; object-scope survey-gated), not on `transfer` | `partial-named-gap` |
| Declared-loss DROP | wired in `preview` (mandatory before execute); granular `LossDeclaration` | **`closed`** |
| Delete-scope (P-DEL-SCOPE) | no implementation | `absent` |
| CDC-tracking | wired on both `transfer` and `migrate` | **`closed`** |
| CDC-silence-on-idempotence | by construction (`isEmpty(between A A) ⟹ 0 DDL`) + Docker | **`closed`** |
| Data-compat NOT-NULL (cross-substrate) | function exists + pure tests, **no `--execute` call site** | `built-not-wired` |
| Possible-data-loss (DacFx) | narrowing → **Warning, not refusal** (weaker than spec) | `partial-named-gap` |
| Data-compat on-disk-bytes (in-place) | no pre-flight; caught post-facto as `ExecutionFailed` | `absent` |
| Transactional / resumable envelope | documented scaffold (`Preflight.fs:290-302`), no wrapper | `scaffold` |

**The completeness law (P-GATE) is not structurally enforced** (N5): `Preflight.all` exists but no
mandatory call site composes the suite.

### 4.4 Protein operator-reachability (PT-6)

| Protein | Verdict | Where the chain stops |
|---|---|---|
| P-1 Dev-cloud → Dev on-prem | `partial` | `full-export` emits SSDT to disk; no diff-vs-prior, no data MERGE, no Measure/Record |
| P-2 QA-cloud → QA on-prem | `partial` | as P-1; environment not selectable in the emit path |
| P-3 Dev → UAT re-key | `partial` | re-key reachable via `transfer`; **not composed** with schema migrate (`executeWithData` passes `Map.empty`) |
| P-4 SSIS publication | `partial` | CREATE files only; no refactorlog/changelog/ChangeManifest/Record |
| P-5 idempotent redeploy | `partial` | schema idempotence reachable via `migrate --execute`; data CDC-silence **measure** absent |
| P-6 in-place migrate | `partial` | schema evolution reachable; Move (CDC-MERGE vs existing data), Measure, Record absent from CLI |
| P-7 terminal eject | **`absent`** | no `eject` verb, no terminal package, no freeze marker — zero chain steps |
| P-8 drift / remediation | `partial` | `verify-data` does row/null counts only; no schema-drift-vs-model path |
| P-9 self-check canary | **`operator-reachable`** | full schema round-trip via `projection canary`; CDC-silence measure absent from the canary CLI |

**The three named design forks (§5.12 of the masterwork) are untouched in code:** the
refactorlog-against-prior boundary, the SSIS-consumer rendering, and the eject deliverable all have
substrate (`LifecycleStore`, `ChangeManifest`) but no resolving implementation.

---

## 5 — The fitness scorecard

Counting the surveyed cells by verdict (the matrix's move/gate/regime cells; proteins counted
separately):

| Verdict | Count | Cells |
|---|---|---|
| **`faithful-composed` (L3)** | 5 | FTC-over-disk · T16 migrate composition · comparison-regime (a) · imperative-lens column-shape · (declared-loss + CDC-tracking + CDC-silence gates are `closed`) |
| **`faithful` (L2, no silent erasure)** | ~11 | 3 name-spaces · P-ID · Reidentify(AssignedBySink/PreservedFromSource) · Reconcile · Snapshot/Diff/applyDiff · norm(schema) · Insert · Update · Unchanged · Rename(column path) · comparison-regime (c) |
| **`partial-named-gap`** | ~14 | Add · Remove · Reshape · declarative-deploy · fresh-load · Accumulate · ‖rename‖_data · change-manifest · Reidentify(ReconciledByRule) · Dev→UAT re-key · Identity↔Schema · Reidentify(data) · Move · connection gate · permission gate · possible-data-loss gate |
| **`partial-SILENT-gap` (the cardinal sin)** | 2 | **G4 cross-schema FK filter** (the priority) · N2 computed-column MERGE (dormant) |
| **`built-not-wired`** | 4 | compose/netDiff/ChangeManifest (N7) · data-compat tightening gate · `Preflight.all` (N5) · norm carriers |
| **`scaffold`** | 1 | transactional/resumable envelope |
| **`absent`** | 4 | Delete move · delete-scope gate · on-disk-bytes gate · P-7 eject |

**The shape of the distance.** The **schema/diff plane is the strongest** — the captured surface,
the torsor laws (W1/W3/no-cheat), the FTC, and the T16 composition are `faithful` to
`faithful-composed`, and `migrate --execute` is operator-reachable for schema evolution. The **gaps
cluster in three regions**:

1. **The data leg's destructive + general-case edges** — the Delete move and its scope gate are
   absent; `capture = k` and `‖rename‖_data = 0` lack live witnesses; the representation-noise
   tolerances (N2/N3) are incomplete.
2. **The T-VI spanning gates and their wiring** — connection/permission gates exist but aren't wired
   on `transfer` and aren't composed via a mandatory `Preflight.all` (N5); the transactional envelope
   and on-disk-bytes gate are scaffold/absent; `validate-user-map` is post-write only (N4).
3. **The provenance/publication leg above schema** — refactorlog-against-prior + real episode clock
   (G9/N6), the change-manifest-as-full-displacement and its consumer (G10/N7), the SSIS changelog,
   and **the entire eject protein (P-7)** are unbuilt; `compose`/`netDiff` are defined but unwired.

4. **The one open `partial-SILENT-gap` to prioritize: G4** — the cross-schema FK filter at
   `ReadSide.fs:580` drops a reference with no diagnostic, violating the no-silent-drop boundary axiom.
   It is the single most important cell to move from `partial-SILENT` to at least `partial-named`.

This is the map a pass-three acceptance suite measures against, and the ordered target for the next
build wave. Refresh this overlay first when any cell advances; the masterwork (the target) does not
move when the climb does.

---

— Pass two recorded for the receiving agent. The masterwork is the target; this is the distance to it,
cell by cell, at HEAD. Pass three (lab-grade acceptance criteria + the fitness evaluation against them)
builds on this map.
