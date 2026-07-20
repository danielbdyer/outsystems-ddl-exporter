# THE USE-CASE ONTOLOGY — Acceptance Suite & Fitness Evaluation (Pass Three)

> **What this is.** The **lab-grade acceptance criteria** for the matrix, and the **fitness
> evaluation** of the code against them. Pass one (`THE_USE_CASE_ONTOLOGY.md`) is the target; pass two
> (`THE_USE_CASE_ONTOLOGY.fitness.md`) maps HEAD onto every cell; **this pass turns each cell's
> discriminating predicate into a falsifiable acceptance test and judges the code against it.**
>
> **What "lab-grade" means.** Each acceptance criterion (AC) is a *runnable test spec*, not a
> restatement of a name. It is built on the **adversarial input** — the input on which a
> plausible-but-wrong implementation diverges from the correct one — and states an **observable** with
> a binary **pass condition**. A criterion that a wrong-but-plausibly-named body would pass is not
> lab-grade and does not belong here. Each AC has the shape:
>
> > **Setup** (the fixture) → **Adversarial input** (the discriminating case) → **Observable** (what is
> > measured) → **Pass iff** (the binary condition).
>
> **Fitness scale.** **PASS** — a live witness asserts the adversarial pass condition (cite it).
> **PARTIAL** — a witness exists but asserts a weaker condition than the AC requires (name the missing
> assertion). **FAIL** — the mechanism exists but the adversarial case is not covered and would not
> pass. **ABSENT** — no mechanism. The fitness column is sourced from pass two; this pass adds the
> *criterion the fitness is measured against*.
>
> **Why this is the keystone.** The masterwork's §7 completeness checklist asks "every cell has a
> discriminating predicate." This suite asks the stronger question: **does a test actually exercise
> that predicate's adversarial case?** A green round-trip on the happy path with no adversarial witness
> is a *phantom green* — the exact failure mode (a silent erasure behind a passing test) the whole
> engine exists to foreclose. §7 here enumerates the phantom-green risks.

---

## 0 — How to read the tables

`Law` is the `P-*` code (resolved in masterwork §5.0). `Adversarial input → Pass iff` is the
lab-grade core: the discriminating case and the binary condition. `Fit` is the HEAD verdict. `Witness
or gap-to-green` cites the live test (PASS) or names exactly what must be added (PARTIAL/FAIL/ABSENT).

Fitness key: ✅ PASS · ◑ PARTIAL · ✗ FAIL · ⬚ ABSENT.

---

## 1 — Schema-move acceptance criteria (AC-S)

| AC | Law | Setup → Adversarial input → Pass iff | Fit | Witness or gap-to-green |
|---|---|---|---|---|
| **AC-S1 Add / no-rename-collision** | P-CMP | Two catalogs; B adds a kind whose **SsKey is new but whose Name collides with a kind removed in B**. Pass iff the diff reports **both a Remove and an Add** (matched by SsKey), never a Rename and never silence. | ◑ | `between` partitions by SsKey set-ops (correct by construction) but **no test feeds the name-collision adversarial input**. Add: `CatalogDiffTests` case with colliding names + distinct SsKeys asserting `Removed ∋ old ∧ Added ∋ new ∧ Renamed = ∅`. |
| **AC-S2 Add / isEmpty honesty** | P-CMP | Catalog with a single added FK (no kind/column change). Pass iff `isEmpty(between A B) = false`. | ✅ | `C1: isEmpty is honest — an added FK alone makes the diff non-empty` (`CatalogDiffTests.fs:650`). |
| **AC-S3 Remove / destructive refusal** | P-GATE | Diff with a dropped column, `LossDeclaration = DeclareNone`. Pass iff `preview` refuses **at plan/emit time** with a named error, **before** any statement executes. | ✅ | `a dropped column refuses fail-loud (no DROP emitted)` + `destructiveLosses spans the FK channel` (`SchemaMigrationEmitterTests.fs`, `MigrationRunTests`). |
| **AC-S4 Remove / granular declaration** | P-GATE | Diff with two drops; declare only one (`DeclareThese {t}`). Pass iff the declared drop emits and the **undeclared sibling still refuses**. | ✅ | `DeclareThese clears the named loss but refuses an undeclared sibling` (DECISIONS 2026-06-03). |
| **AC-S5 Rename / name-space** | P-RN | Same SsKey, changed logical Name, **physical column name unchanged in the source regime**. Pass iff a rename is detected in the **Designation** space (a refactorlog `sp_rename` entry is emitted), not missed by checking the physical name. | ✅ | `RefactorLogEmitter: a column rename produces a SqlSimpleColumn entry`. |
| **AC-S6 Rename ⊥ Reshape** | P-CH | One attribute **renamed and widened** in the same diff. Pass iff the rename emits **only** a refactorlog entry (no ALTER) **and** the widening emits **only** an `ALTER COLUMN` (no refactorlog), with neither channel seeing the other. | ◑ | `a rename alone emits no ALTER` (`SchemaMigrationEmitterTests.fs:138`) proves one direction; **no test feeds the simultaneous rename+widen** input asserting both channels fire disjointly. Add it. |
| **AC-S7 Rename / reference channel** | P-CH, N1 | A **renamed FK** (same `Reference` SsKey, changed Name). Pass iff a refactorlog entry is emitted for the reference rename. | ✗ | `ReferenceDiff.Renamed` is computed but `RefactorLogEmitter` never reads it (N1) — a renamed FK gets no `sp_rename`. Build the emitter leg + the test. |
| **AC-S8 Rename / data-norm zero** | A43 | Deploy a rename to a CDC-tracked table with data; redeploy. Pass iff **zero CDC capture rows** result from the rename (`‖rename‖_data = 0`). | ⬚ | No live CDC-on-rename canary. Build the Docker witness (deploy rename → assert `cdc.<t>_CT` count = 0). |
| **AC-S9 Reshape / facet completeness** | P-CMP | A column changed **`DECIMAL(10,2) → DECIMAL(18,4)`** (precision **and** scale). Pass iff the diff's `AttributeDiff.Changed` names **both** the Precision and Scale facets — a body comparing only `DataType` yields `isEmpty = true` and fails. | ◑ | All 9 facets are compared in `changedFacets`; trust/options/type facets have round-trip tests, but **no precision+scale-specific adversarial test**. Add the `DECIMAL(10,2)→(18,4)` case. |
| **AC-S10 Reshape / non-alterable refusal** | faithfulness | A change to a non-ALTER-able facet (DEFAULT / IDENTITY / PK / computed). Pass iff a **named refusal** is emitted (not a naive ALTER SQL Server would reject). | ✅ | `migration.unsupportedFacetChange` (`SchemaMigrationEmitter.fs:130-136`). |
| **AC-S11 Reshape / narrowing surfaced** | faithfulness | A narrowing (`NVARCHAR(256)→(50)`) on a populated column. Pass iff the loss is **surfaced** (a named Warning or refusal), never silent. | ◑ | A `Warning` is emitted but execution **proceeds** (`partial-named-gap`) — the spec wants `refuse-unless-declared`. Promote the narrowing Warning to a declared-loss gate, or document the weaker class as a named Tolerance. |
| **AC-S12 Declarative / DacFx-seam purity** | policy | Build the declarative dacpac model from a diff containing ALTER/DROP. Pass iff the imperative `AlterTable*`/`Drop*` statements are **excluded** from the dacpac model (DacFx computes the delta from CREATE+refactorlog). | ✅ | `DacpacEmitter.isSchemaStatement` excludes them (`:77-86`). |

---

## 2 — Data-move acceptance criteria (AC-D)

| AC | Law | Setup → Adversarial input → Pass iff | Fit | Witness or gap-to-green |
|---|---|---|---|---|
| **AC-D1 Update / null-safe change-detection** | P-NOOP | A row whose column transitions **`NULL → 'foo'`** (one side NULL). Pass iff the MERGE captures the change — a naive `Target.c <> Source.c` is `UNKNOWN` on the NULL side and **misses it**. | ✅ | The `IS DISTINCT FROM` three-arm expansion (`ScriptDomBuild.fs:763-789`); structural guard `Assert.Contains("WHEN MATCHED AND (")` (`CdcSilenceTests.fs:250`). |
| **AC-D2 Unchanged / silence floor** | P-DM | Deploy seed data; **redeploy byte-identical**. Pass iff the second deploy produces **exactly zero** CDC capture rows. | ✅ | `Slice γ: CDC-silence … zero CDC capture rows on idempotent redeploy` (`CdcSilenceTests.fs:239`) + 3 property sweeps + C0–C4 cross-emitter. |
| **AC-D3 Update / sensitivity** | P-NOOP | Redeploy with **exactly one** column changed in one row. Pass iff CDC fires (capture > 0) — proving the silence of AC-D2 is not vacuous. | ✅ | `Slice γ sensitivity: changed-content redeploy DOES fire` (`CdcSilenceTests.fs:274`). |
| **AC-D4 General minimality (`capture = k`)** | P-DM | Redeploy with **k** known changed rows (k > 1). Pass iff the CDC capture count **equals k** (not `> baseline`, not `2·|table|`). | ◑ | Only the inequality `post > baseline` is asserted (N8). Add a test asserting exact `count = k` for a known small delta. |
| **AC-D5 Comparable-set excludes computed** | P-DM | A kind with a **persisted computed column** that is not the PK; redeploy identical. Pass iff the computed column is **excluded** from `UPDATE SET` (so no spurious capture / no SQL error). | ✗ | `UpdColumns` filters PK + deferred only, **not computed** (N2). Add the `Computed ≠ None` filter + the test (currently dormant only because V1 JSON omits computed metadata). |
| **AC-D6 Representation-noise tolerated** | P-IF | Redeploy where only **ANSI-padding** (`'foo '` vs `'foo'`) or **decimal scale** (`1.0` vs `1.00`) differs. Pass iff **zero** captures (the difference is a named tolerance, not a change). | ⬚ | No `RTRIM`/scale normalization (N3). Build the tolerance in the comparable-column treatment + the test. |
| **AC-D7 Delete / scope gate** | P-DEL-SCOPE | A MERGE over a **partial** candidate set `S ⊂ T` with rows in `T−S` present and **out of declared scope**. Pass iff those rows **survive** (the DELETE arm fires only within `S`). | ⬚ | **No `WHEN NOT MATCHED BY SOURCE` arm exists anywhere** — the Delete move is absent. Build the scoped DELETE + the survival test. |
| **AC-D8 Drop fail-loud** | P-DROP | A transfer with an **unmatched FK / orphan row**. Pass iff the run exits **non-zero** (never exit-0 hiding a vanished row). | ✅ | `transfer with an unmatched FK exits non-zero` (`TransferCanaryTests.fs:389`, exit-9). |
| **AC-D9 FK ordering / two-phase** | P-ORD-DATA | A **self-referential / cyclic** FK row-set. Pass iff phase-1 inserts (deferred FK NULLed) and phase-2 re-points, with no mid-load FK violation; a non-deferrable cycle **refuses**. | ✅ | `deferred self-referential FK is re-pointed in phase 2` (`TransferCanaryTests.fs:277`); `DataLoadPlan.isSatisfiable` refusal. |
| **AC-D10 Wipe-and-load is a named mode** | policy | Choose the complete-replace mode on a small table. Pass iff a TRUNCATE+reload path exists and its `2·|table|` CDC cost is the **named** non-isometric fallback. | ⬚ | No wipe-and-load implementation exists in any emitter/Transfer path. Build it as an explicit `EmissionMode`. |
| **AC-D11 Fidelity proof / staging×load invariance** | T17, A47 | Run `check fidelity <flow>` on a data-filled estate across `--stage ddl\|dacfx` × `--data transfer\|lanes`, and under a FullRights (`PreferPreservedKeys`) target. Pass iff every combination exits **0 byte-identical**, and an FK-orphan / flipped source cell exits **non-zero** under each — the proof's verdict is invariant of how the stand-in was staged and loaded. | ✅ | `ReverseLegCanaryTests` — `B5` (DDL×transfer), `P1-S1 DacFx-staged proof` (DacFx×transfer), `P1-S4 lanes proof` (DDL×lanes), `P1-S3 preserved-keys proof` (PreferPreservedKeys); orphan arms exit 5/9. |
| **AC-D12 Offline manifest reconcile / soundness** | A48 | Capture a proof manifest (`check fidelity --capture`), **tear down the live source**, then `check fidelity --against <manifest> --target <ref>`. Pass iff a byte-identical target exits **0**, a **tampered** target exits **5** (a named per-kind divergence, never phantom-green), and an unreachable / model-hash-mismatched target exits **6**. | ✅ | `FidelityRowsDockerTests` `P2-S3 offline reconcile` (green + tamper-red, source torn down); `ProofManifestTests` (codec fail-closed on foreign version/plane/garbage). |

---

## 3 — Identity & reconciliation acceptance criteria (AC-I)

| AC | Law | Setup → Adversarial input → Pass iff | Fit | Witness or gap-to-green |
|---|---|---|---|---|
| **AC-I1 Match by SsKey, not name** | P-ID | Two kinds sharing a logical Name but with **distinct SsKeys**. Pass iff comparison treats them as distinct (matched by SsKey), never conflated by Name. | ✅ | `SsKey` is a closed DU with structural equality; `reconcileKind` indexes by column value not ordinal (`Reconciliation.fs:59-68`). |
| **AC-I2 Re-key is an Update, not Delete+Insert** | P-REKEY | The **same logical user** is `Id=280` (source) and `Id=18` (sink); transfer with reconciliation. Pass iff the row is an **Update** (relationship preserved modulo surrogate) — a surrogate-keyed `ON` would yield Delete+Insert (2× captures, broken FK). | ✅ | re-key canary `(Order → User-by-email)`; zero orders carry source surrogates after re-key (`TransferCanaryTests.fs:330`). |
| **AC-I3 Cyclic AssignedBySink refused** | faithfulness | A self-referential **IDENTITY-PK** kind (cyclic `AssignedBySink`). Pass iff the run **refuses before any write** (phase-2 would key on a source PK the sink replaced). | ✅ | `cyclic AssignedBySink is refused, not silently mis-keyed` (`TransferCanaryTests.fs:435`). |
| **AC-I4 Composite surrogate refused** | faithfulness | A **multi-column IDENTITY** `AssignedBySink` kind. Pass iff the run refuses (single-leg capture would truncate the surrogate). | ✅ | `composite-IDENTITY AssignedBySink is refused, not half-captured` (`TransferCanaryTests.fs:481`). |
| **AC-I5 validate-user-map pre-flight** | P-GATE | A user-map with an **unmapped orphan** source user. Pass iff the run **halts before emitting any SQL** (not a post-write exit-9). | ✗ | `isFullyMapped` exists but **no caller gates on it pre-write** (N4); orphans surface only post-write. Build `Preflight.validateUserMap` + the pre-SQL-halt test. |
| **AC-I6 Rename-aware re-point** | P-ID | A column **renamed** between source and sink contracts; transfer rows. Pass iff each value follows its **SsKey/Name** to the new column, **never matched by ordinal**. | ✅ | `a renamed column is re-pointed by the rename map, not matched by ordinal` (`RenameProjectionTests.fs:57`). |
| **AC-I7 Rename + reconcile composed** | P-ID, P-REKEY | A sprint with **both** a column rename **and** a Dev→UAT re-key. Pass iff the combined transfer re-points by name **and** re-keys by business key in one run. | ⬚ | `runWithRenames` is straight-load only; reconcile+rename is a named follow-on (`TransferRun.fs:421`). Build the combined path + the test. |

---

## 4 — Provenance, time & composition acceptance criteria (AC-P)

| AC | Law | Setup → Adversarial input → Pass iff | Fit | Witness or gap-to-green |
|---|---|---|---|---|
| **AC-P1 No-cheat applyDiff** | P-RT₂ | `apply(between(A,B), A')` for a **base `A' ≠ A`**. Pass iff the result **threads `A'`** (extra kinds of `A'` survive) — a body `apply d = target d` ignores its base and **fails**. | ✅ | `applyDiff threads the passed-in catalog, not the recorded target (no-cheat)` (`CatalogDiffTests.fs:314`). |
| **AC-P2 Round-trip (W3)** | P-RT₁ | Any A, B. Pass iff `apply(between(A,B), A) ≈ B` modulo the captured surface. | ✅ | `Time: applyDiff (between A B) A = B` (`AxiomTests.fs:857`). |
| **AC-P3 Identity diff (W1)** | T12 | Any A. Pass iff `between(A,A)` is empty and `apply(that, A) = A`. | ✅ | `applyDiff (between A A) A = A` (`AxiomTests.fs:864`). |
| **AC-P4 Composition (W2)** | T12 | A chain A→B→C. Pass iff `compose(δ₁,δ₂)` equals `between(A,C)` and `apply` composes (functor law). | ◑ | Law-tested (`compose: applyDiff (compose d1 d2) A = …`) but **`compose` has zero production callers** (N7) — the *law* passes, the *activation* is absent. Wire a consumer (multi-episode recombination) + an integration test. |
| **AC-P5 Durable FTC** | P-PROV | Save a 3-episode chain to disk; **reload from disk**; reconstruct. Pass iff `reconstructLatestSchema` over the **disk-loaded** chain reproduces the stored latest (not an in-memory-only fold). | ✅ | `6.H.2: reconstructLatestSchema over the persisted chain reproduces the stored latest schema (durable)` (`LifecycleStoreTests.fs:68`). |
| **AC-P6 Refactorlog against-prior + dedup** | P-PROV | Two sprints; sprint 2 re-emits a rename already in sprint 1's committed `.refactorlog`. Pass iff the entry is **not duplicated** (dedup by `OperationKey`) and a fresh-env replay of the **full** history reproduces the schema. | ✗ | No accumulate-against-prior path; `ChangeDateTime` pinned (G9/N6). Build the append+dedup + real episode clock + the fresh-env-replay test. |
| **AC-P7 T16 master equation (live)** | T16 | Deploy state A to SQL Server with data; `migrate --execute` to B across rename+widen+add. Pass iff B' reproduces B (schema-structural), **data survives**, and the re-run is idempotent. | ✅ | `migrate A B canary: one execute evolves A→B across three channels; data survives; re-run idempotent` (`MigrationCanaryTests.fs`). |
| **AC-P8 migrate records the episode** | P-PROV | `migrate --execute` a live run. Pass iff the run is **persisted as an Episode** such that the next sprint's diff loads it as the prior. | ✗ | The CLI execute path does **not** call `MigrationRun.record` (pass-two G8 caveat). Wire `record` into the execute path + the persistence test. |
| **AC-P9 Change-manifest is the displacement** | T14, G10 | An episode edge with renames + reshapes + a data delta. Pass iff the manifest carries **per-channel `‖δ‖`, the refactorlog xref, the per-run tolerance residual, the `AppliedTransforms` outcome, and the CDC series**. | ◑ | Carries channel counts + `SchemaNorm` + single `CdcCaptureCount`; **missing** tolerance residual + `AppliedTransforms` outcome + CDC *series* (G10) and has **no production consumer** (N7). Extend the type + wire a consumer. |

---

## 5 — Gate acceptance criteria (AC-G) — the P-GATE completeness suite

Each gate's AC is "refuses **first** (before any mutation) on the adversarial precondition." The
**meta-criterion AC-G0** is what T-VI spanning actually requires.

| AC | Gate | Adversarial input → Pass iff (refuses before any write) | Fit | Witness or gap-to-green |
|---|---|---|---|---|
| **AC-G0 Completeness composed** | P-GATE | Any `--execute` path. Pass iff a **single mandatory `Preflight.all`** composes the full gate suite, so no caller can skip a gate. | ✗ | `Preflight.all` exists but has **zero callers** (N5); gates are à la carte. Make `Preflight.all` the mandatory entry on every mutation verb. |
| **AC-G1 Connection** | conn | A **dead/misrouted** endpoint. Pass iff refusal (`connectionUnavailable`) before phase 1, on **both** `migrate` and `transfer`. | ◑ | Wired on `migrate` (`Program.fs:962`), **not on `transfer`**. Wire it on transfer + a Docker refusal witness. |
| **AC-G2 Permission** | perm | A sink login **denied INSERT on one target table**. Pass iff refusal (`insufficientGrant`) before phase 1 — never "zero rows, exit 0". | ◑ | Wired on `migrate`, **DB-scope only** (object-scope survey-gated); **not on `transfer`**. Add object-scope `sys.fn_my_permissions('<t>','OBJECT')` + transfer wiring. |
| **AC-G3 Declared-loss DROP** | drop | An undeclared destructive drop. Pass iff refusal at plan time. | ✅ | `closed` — mandatory in `preview` before `execute`. |
| **AC-G4 Delete-scope** | del-scope | An ungated table-wide DELETE intent. Pass iff the DELETE arm is suppressed absent a declared scope. | ⬚ | No DELETE arm exists (AC-D7). |
| **AC-G5 CDC-tracking** | cdc | `--execute` against a **CDC-tracked sink** without `--allow-cdc`. Pass iff refusal before any write. | ✅ | `closed` — wired on both paths (`TransferRun.fs:336`, `MigrationRun.fs:333`). |
| **AC-G6 CDC-silence-on-idempotence** | cdc-silence | Redeploy an **unchanged** schema to a tracked DB. Pass iff **zero DDL** emitted (`isEmpty(between A A) ⟹ 0 DDL`). | ✅ | `closed` — by construction + `CdcSilenceTests`. |
| **AC-G7 Data-compat NOT-NULL (cross-substrate)** | data-compat | A NOT-NULL tightening on a column whose **source rows carry NULLs**. Pass iff refusal (`notNullViolation`) before phase 1 — never a mid-load abort. | ✗ | `dataViolatesTightening` exists + pure tests, but **no `--execute` call site** (`built-not-wired`). Wire it into the execute pre-flight. |
| **AC-G8 Possible-data-loss** | data-loss | A narrowing that would truncate on-disk data. Pass iff refusal (the engine's per-object gate) with DacFx `BlockOnPossibleDataLoss=True` as the backstop. | ◑ | Narrowing emits a **Warning, not a refusal** (weaker than spec). Promote to a declared-loss gate. |
| **AC-G9 On-disk-bytes (in-place)** | data-compat | An `ALTER COLUMN … NOT NULL` against a column with **existing NULL rows**. Pass iff a pre-flight refuses before the ALTER is submitted (not caught post-facto as `ExecutionFailed`). | ⬚ | No pre-flight; the failure is only caught as a post-hoc exception. Build the `COUNT(*) WHERE col IS NULL` probe. |
| **AC-G10 Transactional / resumable** | atomicity | A transfer that **fails mid-load**. Pass iff the sink is left **clean or at a deterministic resume point** (no half-populated target, no duplicate rows on retry). | ⬚ (scaffold) | Documented scaffold only (`Preflight.fs:290-302`). Build the transaction/resumable-upsert envelope. |

---

## 6 — Protein (composition) acceptance criteria (AC-X)

End-to-end ACs: a single operator command runs the whole protein chain and the terminal observable
holds. These are the L3-composition criteria; pass two found most `partial`.

| AC | Protein | Adversarial / terminal observable → Pass iff | Fit | Gap-to-green |
|---|---|---|---|---|
| **AC-X1** | P-1/P-2 load | One command: snapshot → diff-vs-prior → publish schema → CDC-aware data load → measure → verify → record. Pass iff CDC-silent on a no-op re-run **and** an episode is recorded. | ◑ | `full-export` stops at publish-to-disk; no diff-vs-prior, data MERGE, measure, or record composed. |
| **AC-X2** | P-3 UAT re-key | One command: schema-migrate UAT **and** re-key user FKs by email. Pass iff schema+data+re-key compose with `validate-user-map` gating first. | ◑ | re-key reachable via `transfer`; `executeWithData` passes `Map.empty` (re-key not composed); N4 gate absent. |
| **AC-X3** | P-4 SSIS pub | One command emits CREATE bundle **+ accumulated refactorlog + per-sprint changelog/ChangeManifest** and records the episode. Pass iff a fresh consumer can reconstruct any prior state. | ✗ | Only CREATE files emit; refactorlog/changelog/ChangeManifest/Record not assembled. |
| **AC-X4** | P-5 redeploy | One command redeploys an unchanged model. Pass iff **zero ALTERs and zero CDC captures**, both **measured**. | ◑ | Schema idempotence reachable + verified; data CDC-silence **measure** not in the CLI path. |
| **AC-X5** | P-6 migrate | `migrate --execute` A→B with data. Pass iff schema (minimal touches) **and** the CDC-aware data delta apply, measured, recorded, idempotent. | ◑ | Schema evolution reachable (G8); Move (CDC-MERGE vs existing data), Measure, Record absent from CLI. |
| **AC-X6** | P-7 eject | One command freezes the model + emits the complete provenance package + verifies reconstruction from genesis + marks operator-owned. Pass iff `reconstructLatest` from genesis = the frozen state. | ⬚ | **No eject code path exists.** Resolve the append-forever-vs-collapsible fork first (masterwork §5.12 #3), then build. |
| **AC-X7** | P-8 drift | Read deployed DB, diff vs the model, classify every divergence (emit/tolerate), surface drift as a structured diagnostic. Pass iff no divergence is silent. | ◑ | `verify-data` does row/null counts only; no schema-drift-vs-model path. |
| **AC-X8** | P-9 canary | `projection canary` deploys, reads back, asserts `Ingest∘Project = id` modulo named tolerances, **and** asserts CDC-silence on idempotent redeploy. | ◑ | Schema round-trip is `operator-reachable`; the CDC-silence measure is not in the canary CLI path. |

---

## 7 — Completeness evaluation: phantom-green risks & coverage

The acceptance suite is **complete** when every cell's discriminating predicate has an AC whose
adversarial case is actually exercised. Two failure classes:

### 7.1 Phantom-green risks (a green happy-path test, no adversarial witness)

These cells have a *passing* test but **not** the adversarial witness the AC requires — the dangerous
class, because the green is misleading:

- **AC-S1 (Add / name-collision):** `between` is correct by construction but no test feeds the
  SsKey-collision input — a future refactor to name-matching would pass the existing tests.
- **AC-S6 (Rename ⊥ Reshape):** only the "rename alone emits no ALTER" half is witnessed; the
  simultaneous rename+widen disjointness is unexercised.
- **AC-S9 (precision+scale):** facet machinery is general but the `DECIMAL(10,2)→(18,4)` adversarial
  case is untested — a body comparing only `DataType` would pass today's suite.
- **AC-D4 (`capture = k`):** only the `> baseline` inequality is asserted; a body that over-captures
  (e.g., `2·k`) would pass.

**Action:** these four ACs are cheap, pure/Docker tests that convert phantom-green to real-green
without new production code. They are the highest-ROI items in the suite.

### 7.2 Coverage gaps (no mechanism, so no test can pass)

The ACs that are ABSENT/FAIL define **what must be built** (not just tested): AC-S7 (reference-rename
refactorlog), AC-S8 (`‖rename‖_data=0` canary), AC-D5 (computed-column exclusion), AC-D6
(representation tolerances), AC-D7/AC-G4 (Delete + scope gate), AC-D10 (wipe-and-load mode), AC-I5
(validate-user-map gate), AC-I7 (rename+reconcile), AC-P6 (refactorlog against-prior), AC-P8 (record
on execute), AC-P9 (full change-manifest), AC-G0 (mandatory `Preflight.all`), AC-G7 (wire tightening
gate), AC-G9 (on-disk-bytes), AC-G10 (transactional), AC-X3/AC-X6 (SSIS pub + eject).

### 7.3 The fitness verdict

Of ~57 acceptance criteria across the matrix: **~24 PASS** (the schema/diff plane, the torsor laws,
the FTC, the T16 live composition, the closed gates, the core data CDC-silence, the identity
refusals), **~13 PARTIAL** (a witness exists but asserts less than the AC — the phantom-green and
weaker-class cells), **~20 FAIL/ABSENT** (no mechanism or no call site). **The engine is provably
right where it is tested adversarially, and the untested edges cluster exactly where pass two located
the gaps:** the data destructive/general-case edges, the spanning-gate wiring, and the
provenance/publication leg above schema (culminating in the entirely-absent eject).

### 7.4 The path to green (ordered)

1. **Close the phantom-greens (§7.1)** — four pure/Docker tests, no production change. Converts
   misleading green to real green; cheapest fidelity gain.
2. **Close the one `partial-SILENT` (G4)** — surface the cross-schema FK filter as a diagnostic
   (AC for `ReadSide.fs:580`). The no-silent-drop boundary axiom demands it.
3. **Wire what already exists (`built-not-wired`)** — `Preflight.all` mandatory (AC-G0), the tightening
   gate into execute (AC-G7), `record` on execute (AC-P8), a `compose` consumer (AC-P4). High leverage:
   the mechanisms are built; only the call site is missing.
4. **Build the data destructive edge** — the scoped Delete (AC-D7/AC-G4) + the representation
   tolerances (AC-D6) + the computed-column exclusion (AC-D5) + `capture = k` (AC-D4).
5. **Build the spanning gates** — connection/permission on transfer (AC-G1/G2), on-disk-bytes (AC-G9),
   the transactional envelope (AC-G10) — gated by the PROD-empty premise re-prioritization (build when
   PROD gains data or a write-denied flow appears).
6. **Build the provenance/publication leg** — refactorlog against-prior (AC-P6), the full
   change-manifest + consumer (AC-P9), the SSIS changelog (AC-X3), and finally the **eject** (AC-X6),
   after resolving the append-forever-vs-collapsible fork.

---

— Pass three recorded for the receiving agent. The masterwork is the target; the fitness overlay is
the distance; **this is the falsifiable test of the distance** — every cell's adversarial acceptance
criterion and the code's verdict against it. Build down §7.4, refresh the fitness overlay as each AC
goes green, and the matrix becomes a self-measuring instrument: at any moment it can say which promises
are proven adversarially and which are not.
