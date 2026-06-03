# THE USE-CASE ONTOLOGY — Obligation Matrix (Pass Four: the test-obligation decomposition)

> **What this is.** One level below the pass-three acceptance suite. Pass three gave *one adversarial
> criterion per cell*; pass four decomposes **each criterion into its full partition of atomic test
> obligations** — the equivalence-class × boundary-value × cross-product expansion — and marks every
> obligation **COVERED / UNCOVERED** against the real test suite at HEAD, citing the test by name and
> line or confirming the absence in code. It turns "is there a discriminating test?" into a **count**:
> *k of n obligations covered* per criterion. The number is the instrument.
>
> **Why it matters — intra-AC phantom-greens.** A criterion pass three marked ✅ PASS can hide
> UNCOVERED obligations beneath it: a cell is green on the happy path but silent on half its
> equivalence classes. Those are *phantom-greens* — the precise place a silent erasure survives behind
> a passing test. §2 is the cross-plane register of every one this pass found. This is the deepest
> level at which completeness is checkable; below it, obligations become individual `[<Fact>]`s.
>
> **Provenance.** Pass one = the target (`THE_USE_CASE_ONTOLOGY.md`); pass two = code mapped onto the
> matrix (`.fitness.md`); pass three = the adversarial acceptance criteria (`.acceptance.md`); **pass
> four = this**, the obligation-level coverage census. Refresh §1's counts as obligations go green.

---

## 0 — The discernment discipline (read before any grade below)

§§1–6 were graded **test-first**: "a discriminating test is cited → COVERED." That makes the *test*
the authority and silently inherits two failure classes we have now hit in the wild:
- **HOLLOW** — the cited test is green but does not actually discriminate the criterion (the
  phantom-green: e.g. `post > baseline` where the criterion needs exact `= baseline + 2`).
- **silently RED** — the cited test does not even pass at HEAD (the 2026-06-02 VO-leak that red-ed the
  whole `CdcSilenceTests` class while it was cited as COVERED).

**The corrected unit of judgment is the acceptance criterion, not the test.** The criterion is the
subject on trial; it is discerned against **two independent legs of evidence**:
1. **Implemented reality** — does a real production code path do what the criterion demands? (read the
   code, judge it against the criterion's discriminating predicate, *independent of any test*)
2. **Implemented test** — does a test that *runs green at HEAD* actually *assert* that predicate?

**Direction is load-bearing: criterion → {reality, test}, never the other way around.** Do not read a
test and infer what the criterion must be; do not let the implementation define "correct." The
criterion is fixed; code and test are each measured against it. **Two *independent* legs guard against
co-wrongness** — a test written to match the implementation rather than the criterion. Checking
test-against-code (or trusting the test as the criterion's proxy) lets co-wrong code+test agree and
look green; only judging *each leg against the criterion* catches it.

### The grade (supersedes bare COVERED/UNCOVERED)

| Grade | Reality leg (criterion vs code) | Test leg (criterion vs live test) |
|---|---|---|
| **HELD** | satisfies | live-green AND discriminates |
| **CODE-ONLY** | satisfies | none / non-discriminating / not-live → *silent-regression risk* |
| **HOLLOW** | does NOT satisfy (or only weakly) | green but does not truly discriminate → *phantom-green* |
| **NEITHER** | gap | gap |
| **STRUCTURAL** | satisfied by construction (closed DU / smart ctor / type) | no runtime test possible (liveness N-A) |

**Liveness is the test-leg's currency check — necessary, subordinate, and it decays.** A green test
that doesn't discriminate is still HOLLOW; an implementation that passes a test without meeting the
criterion is still HOLLOW. Liveness must be *re-measured* — Docker tests no-op when the daemon is down;
typed-VO lifts introduce `String.Concat` leaks the build cannot catch — so this document carries a
stamp and a re-measure trigger. **The metric is the quality of the discernment, not the count:** the
aggregate % is an inspection upper bound; a cell is only as true as the two-leg judgment behind it.

### Liveness stamp — this refresh

Full suite at HEAD `3c2c854`: **pure 2679 passed / 0 failed / 207 deliberate Skip-stubs; Docker 121
passed / 0 failed / 0 skipped.** No matrix-cited class is in the skip set → **the test leg is verified
live for every cited mark.** The sole silent-RED (`CdcSilenceTests` VO-leak) is fixed. What §§1–6's
COVERED marks have NOT established is the **reality leg** and **discrimination** — supplied by the
criterion-anchored two-leg regrade in §7 below. **Re-measure trigger:** any typed-VO lift, IR change,
Docker-state change, or touch to a cited code path or test.

---

## 1 — The global obligation scorecard (the count)

422 atomic obligations across the 57 acceptance criteria. ~220 COVERED (52%). Coverage is **not**
uniform — it tracks exactly the gap regions pass two predicted, now quantified:

| Plane | ACs | Obligations | COVERED | Partial | Coverage | Shape |
|---|---|---|---|---|---|---|
| **Identity** (AC-I) | 7 | 45 | 36 | — | **80%** | strongest; refusals + re-point fully witnessed |
| **Provenance** (AC-P) | 9 | 63 | 42 | 4 | **67%** | torsor laws + FTC + live T16 solid; accumulate-leg 0% |
| **Schema** (AC-S) | 12 | 87 | 52 | — | **60%** | facet cross-product (AC-S9 = 25 obligations) thins it |
| **Gates** (AC-G) | 11 | 69 | 35 | 4 | **51%** | only G3/G5/G6 fully wired across verb paths |
| **Proteins** (AC-X) | 8 | 94 | ~35 | many | **37%** | chain-step reachability; data/measure/record legs break |
| **Data/CDC** (AC-D) | 10 | 64 | 20 | — | **31%** | type×null-state cross-product; the deepest hole |
| **Total** | **57** | **422** | **~220** | **~8** | **~52%** | |

**The reading.** The engine is ~80% adversarially proven on identity and ~67% on the schema-algebra/
provenance core, and it thins toward the edges: the **data plane's type cross-product (31%)** and the
**protein composition legs (37%)** are where the most obligations sit uncovered. Crucially, the
*headline* coverage of pass three (~24/57 cells PASS) overstates depth: when each PASS cell is exploded
into obligations, **~30 obligations beneath PASS cells are UNCOVERED** (§2) — the cells are green, the
classes beneath them are not.

---

## 2 — The cross-plane phantom-green register (the crown jewel)

These obligations are UNCOVERED yet sit **under a cell pass three marked ✅ PASS**. Ranked by danger:
how plausibly a wrong-but-named implementation passes today's suite.

### Tier 1 — actively misleading (a passing test for a rejected behavior)

- **OB-I5.3 / AC-I5 (validate-user-map).** `TransferCanaryTests.fs:389` is green and *looks* like
  coverage — but it witnesses the **post-write exit-9**, the exact behavior the criterion rejects in
  favor of a **pre-write halt**. The green test asserts the behavior the spec says is insufficient.
  This is the most dangerous phantom-green in the matrix: it will read as "covered" to anyone not
  decomposing to obligations.

### Tier 2 — a wrong implementation passes the whole suite

- **OB-D4.2–4.5 / AC-D4 + AC-D3 (capture = k).** *Every* CDC assertion in the suite is `post > baseline`
  (an inequality); none pins the exact `post − baseline = 2k`. An unconditional `WHEN MATCHED` MERGE
  (the V1 over-capture shape, capturing all matched rows) satisfies `> baseline` whenever the baseline
  inserted anything. The single highest-value test in the matrix.
- **OB-D1.3 + OB-D1.6–1.15 / AC-D1 (null-safe predicate × type).** The null-safe predicate is proven
  structurally and operationally **for `nvarchar` only**. The 9 other `SqlLiteral` types (int, decimal,
  bit, datetime, date, guid, varbinary) have **zero** null-state-transition tests, yet
  `perColumnChangeDetection` is type-agnostic — a regression in the `SqlLiteral` null-encoding for
  `IntegerLit`/`DecimalLit` breaks null-safe detection for those types with no failing test.
- **OB-P1.4–1.6 / AC-P1 (no-cheat).** The no-cheat law (P-RT₂) is genuinely witnessed on the **kind
  channel only**. Reference, index, and sequence each have a W3 round-trip test but **zero no-cheat
  tests** — a body `applyDiff base d = { …kinds… ; References = target(d).refs; Indexes = target(d).idx;
  Sequences = target(d).seq }` passes every existing C1 test while violating no-cheat on three channels.

### Tier 3 — narrower, lower-probability, but real

- **OB-S9.19 / AC-S9 + OB-P2.8 / AC-P2 (DECIMAL precision+scale).** All 9 facets are compared in code,
  but no test feeds `DECIMAL(10,2)→(18,4)`; a body comparing only `DataType` yields `isEmpty=true` and
  passes both the diff and the round-trip vacuously.
- **OB-I2.11 / AC-I2 (re-key strategies).** PASS covers `MatchByColumn`+`ManualOverride`; the
  `ByEmail`/`BySsKey`/`FallbackToSystemUser` strategies are exercised only at the `UserFkReflowPass`
  layer, never through the `Transfer.runReconciling` execution path (a different, narrower DU).
- **OB-S12.6 / AC-S12 (DacFx-seam).** `isSchemaStatement` is correct by closed-DU match, but no test
  feeds a mixed `CreateTable + AlterTableAlterColumn` stream and asserts the imperative variant is
  excluded — a refactor adding ALTER to the schema branch corrupts the dacpac silently.
- **OB-S4.4/S4.5, OB-S10.3/S10.4, OB-I4.3, OB-D2.4** — stale-token & attribute-level declaration,
  PrimaryKey/Computed refusal, 3-leg composite, nullable-stays-NULL silence: mechanisms correct by
  construction, no adversarial fixture; a narrowing refactor passes.

**The lesson pass four exists to teach:** "✅ PASS" at the cell level is a coverage *claim*; the
obligation count is the coverage *fact*. ~30 obligations beneath green cells are unproven.

---

## 3 — The gate wiring matrix (10 gates × 3 mutation verbs)

`W` wired-and-fires-before-write · `P` partial (weaker than spec, or some sub-cases) · `–` not wired ·
`ABS` mechanism absent · `N-A` gate inapplicable. (MC = `migrate --execute --conn`; MX = `migrate
--execute --source/--sink-conn`; TR = `transfer --execute`.)

| Gate | MC | MX | TR |
|---|---|---|---|
| G0 `Preflight.all` mandatory | – | – | – |
| G1 Connection | W | W | – |
| G2 Permission | P (db-scope only) | P (sink only) | – |
| G3 Declared-loss DROP | W | W | N-A |
| G4 Delete-scope | ABS | ABS | ABS |
| G5 CDC-tracking | W | W | W |
| G6 CDC-silence-on-idempotence | W | W | W |
| G7 Data-compat NOT-NULL | – (built-not-wired) | – | – |
| G8 Possible-data-loss | P (warn, not refuse) | P | N-A |
| G9 On-disk-bytes (in-place) | ABS | ABS | N-A |
| G10 Transactional/resumable | P (SQL implicit rollback) | P | – |

Only **G3, G5, G6** are fully wired across every applicable path. `transfer --execute` carries **one**
gate (CDC). The master gap is **G0**: `Preflight.all` exists (`Preflight.fs:310`) with **zero callers**,
so the suite is à la carte and a new gate can be added to `all` yet fire nowhere.

---

## 4 — Protein chain-completeness (the break-points)

Each protein's §3 chain with ✅/✗ per step; `<<<` marks the first break (where the one-command path
stops). Reachable-steps / total in parentheses.

```
P-1/P-2 load (full-export)                                              (3/11)
  Snapshot ✅ → Diff-vs-prior ✗ <<< → Gate-loss ✗ → Rename-accum ✗ → Reshape ✅ →
  Add ✅ → Publish ✅ → Insert/Update/Unchanged ✗ → Measure ✗ → Verify ✗ → Record ✗

P-3 UAT re-key (migrate --execute --source/--sink-conn)                 (6/13, not composed)
  Snapshot ✅ → Diff ✅ → Gate-schema ✅ → Gate-validate-user-map ✗ <<< →
  Rename ✅ → Reshape/Add ✅ → Reconcile ✗(separate verb) → Reidentify ✗(Map.empty) →
  Publish-schema ✅ → Insert ✗ → Measure ✗ → Verify ✗ → Record ✗

P-4 SSIS pub (emit/full-export)                                        (2/9)
  Snapshot ✅ → Diff-vs-prior ✗ <<< → Declare-loss ✗ → Rename-partial ✅ →
  Reshape-preview ✗ → Add ✅ → Accumulate ✗ → Publish-full-bundle ✗ → Record ✗

P-5 redeploy (migrate --execute --conn)                                (5/8 schema-only)
  Snapshot ✅ → Diff-empty ✅ → Gate ✅ → Tolerate ✅ → Publish-zero-DDL ✅ →
  Unchanged-data ✗ <<< → Measure-CDC=0 ✗ → Verify ✅

P-6 in-place migrate (migrate --execute --conn)                        (7/12 schema-only)
  Snapshot-A ✅ → Snapshot-B ✅ → Gate-conn+perm ✅ → Diff ✅ → Gate-loss+compat ✅ →
  Rename ✅ → Reshape ✅ → Add ✅ → Move-CDC-MERGE ✗ <<< → Measure ✗ → Verify ✅ → Record ✗

P-7 eject (no verb)                                                    (0/8)
  Snapshot ✗ <<< → Diff-genesis ✗ → Accumulate ✗ → Declare-loss ✗ →
  Gate-provenance ✗ → Publish-terminal ✗ → Record ✗ → Verify ✗

P-8 drift (verify-data, partial)                                       (1/8)
  Snapshot-deployed ✅(data-only) → Snapshot-model ✗ <<< → Diff ✗ → Tolerate ✗ →
  Declare-drift ✗ → Gate ✗ → Publish-remediation ✗ → Measure-drift-norm ✗

P-9 canary (canary)                                                    (5/8 schema-only)
  Snapshot ✅ → Publish-deploy ✅ → Snapshot-readback ✅ → Diff-roundtrip ✅ →
  Tolerate ✅ → Gate-isEmpty ✅ → Measure-CDC-silence ✗ <<< → Verify-migration ✗
```

**Five load-bearing seams block most chains:** (1) `MigrationRun.record` has zero production callers —
blocks Record on P-1/P-3/P-4/P-6/P-7. (2) No CDC-count read in any CLI verb — blocks Measure on five
proteins. (3) No data-MERGE in the `migrate` path (`executeWithData` passes `Map.empty`) — blocks Move.
(4) No diff-vs-prior in `full-export` — every run is genesis (P-1/P-4). (5) No eject verb / unresolved
fork — P-7 is 0/8.

---

## 5 — The ordered build-test plan (obligation-level)

### Group 1 — close phantom-greens (pure/Docker tests, **no production code**)

These convert misleading green to real green. Highest ROI; do first.

1. **OB-D4.2–4.5** — assert exact `post − baseline = 2k` for known k (the single most important test).
2. **OB-D1.6–1.15** — nullable fixtures per `SqlLiteral` type (int/decimal/bit/datetime/date/guid/
   varbinary) × null-state transition under live CDC; **OB-D1.3** NULL→NULL silence; **OB-D2.4**
   nullable-stays-NULL.
3. **OB-P1.4–1.6** — no-cheat (base-divergence) tests on the reference/index/sequence channels;
   **OB-P1.2/1.3/1.7** the other base-divergence forms.
4. **OB-S9.19 / OB-P2.8** — `DECIMAL(10,2)→(18,4)` diff + round-trip; **OB-S9.11/13/14/16/17/24** the
   Length-narrow/Precision/Scale/Computed facet cases.
5. **OB-S6.3** simultaneous rename+widen disjointness; **OB-S1.2** name-collision; **OB-S12.6**
   mixed-stream DacFx exclusion; **OB-S4.4/4.5**, **OB-S10.3/10.4**, **OB-I4.3** the construction-correct
   cells; **OB-I2.11** the four strategies through `runReconciling`.

### Group 2 — wire what exists (`built-not-wired`, one call site each, small code)

6. **AC-I5 / OB-I5.1** — build `Preflight.validateUserMap` and gate pre-write (and *replace* the
   misleading post-write test OB-I5.3).
7. **AC-G0 / OB-G0.1-3** — make `Preflight.all` the mandatory entry on MC/MX/**TR**.
8. **AC-G7 / OB-G7.6-8** — wire `tighteningPreflight` into the execute paths.
9. **AC-P8 / OB-P8.4 + AC-X record steps** — call `MigrationRun.record` on `--execute`.
10. **AC-P4.5** — land a production consumer of `compose`/`netDiff`.

### Group 3 — build new mechanism (then test)

11. **AC-S7 / OB-P6.5** reference-rename refactorlog leg (N1); **AC-D5** computed-column exclusion (N2);
    **AC-D6** representation tolerances (N3).
12. **AC-D7 / AC-G4** scoped Delete arm + gate; **AC-D10** wipe-and-load mode; **AC-G9** on-disk-bytes
    probe; **AC-G10** transactional envelope.
13. **AC-P6** refactorlog accumulate-against-prior + real episode clock; **AC-P9** the manifest's
    `ToleranceResidual`/`AppliedTransforms` fields + a consumer; **AC-S8** the `‖rename‖_data=0` canary.
14. **Protein composition** — diff-vs-prior in `full-export` (AC-X1/X3); compose re-key with schema
    migrate (AC-X2); CDC-measure in the CLI verbs (AC-X4/X5/X8); finally **AC-X6 eject** after the
    append-vs-collapse fork is resolved.

---

## 6 — The full obligation tables (per plane)

The complete per-obligation census follows, one section per plane. Each row: obligation ID · concrete
input (the equivalence-class representative) · expected observable + pass-iff · coverage (test:line or
absence) · why the class is distinct. `N-A` marks obligations inapplicable to a path.

> Sections 6.1–6.6 below carry the full tables. They are the raw instrument; §§1–5 are the read.

### 6.1 Schema plane (AC-S1…AC-S12) — 87 obligations, 52 COVERED (60%)

| OB | Input → pass-iff | Coverage |
|---|---|---|
| **AC-S1 Add / no-collision (3/5)** | | |
| S1.1 | new SsKey + new Name → `Added`, no Remove/Rename | COVERED `CatalogDiffTests.fs:54` |
| S1.2 | **drop K1(Name="Foo") + add K2(Name="Foo", new SsKey)** → `Removed∋K1 ∧ Added∋K2 ∧ Renamed=∅` | **UNCOVERED** — the named adversarial name-collision case |
| S1.3 | add kind sharing Name with an Unchanged kind | COVERED (by construction; `Catalog.create` rejects dup Name) |
| S1.4 | empty source → all Added | COVERED `:54` |
| S1.5 | empty target → all Removed | COVERED `:65` |
| **AC-S2 isEmpty honesty (7/7)** | | |
| S2.1 | added FK only → `isEmpty=false` | COVERED `:650` |
| S2.2 | added index only → false | COVERED `:577` |
| S2.3 | added sequence only → false | COVERED `:615` |
| S2.4 | FK trust change only → false | COVERED `:546` |
| S2.5 | renamed kind → false | COVERED `:481` |
| S2.6 | identical catalogs → `isEmpty=true` | COVERED `:44,235` |
| S2.7 | changed attribute facet only → false | COVERED `:175` |
| **AC-S3 destructive refusal (8/8)** | | |
| S3.1 | dropped column, DeclareNone → `destructiveColumnDrop` before DDL | COVERED `SchemaMigrationEmitterTests.fs:84` |
| S3.2 | dropped kind → RefusedByViolations | COVERED `MigrationRunTests.fs:87` |
| S3.3 | dropped FK → `destructiveReferenceDrop` | COVERED `SME.fs:209`; `MigrationTests.fs:142` |
| S3.4 | dropped index → refuse | COVERED `SME.fs:214` |
| S3.5 | dropped sequence → refuse | COVERED `SME.fs:221` |
| S3.6 | dropped kind, DeclareAll → emits | COVERED `MigrationTests.fs:122` |
| S3.7 | dropped column, DeclareAll → DROP COLUMN | COVERED `SME.fs:266` |
| S3.8 | refusal precedes any live write | COVERED `MigrationCanaryTests.fs:152` |
| **AC-S4 granular declaration (3/5)** | | |
| S4.1 | two drops, DeclareThese{t1} → t1 emits, t2 refuses | COVERED `MigrationTests.fs:154` |
| S4.2 | DeclareThese{t1,t2} → both emit, safe | COVERED `MigrationTests.fs:171` |
| S4.3 | DeclareThese{} → still refuses | COVERED (implicit via DeclareNone) |
| S4.4 | **DeclareThese{stale-token} → violation remains** | **UNCOVERED** (phantom-green) |
| S4.5 | **DeclareThese on attribute-level drop** | **UNCOVERED** (phantom-green; only kind-drops tested) |
| **AC-S5 rename name-space (6/6)** | | |
| S5.1 | kind logical rename → SqlTable refactorlog entry | COVERED `RefactorLogEmitterTests.fs:86` |
| S5.2 | column logical rename → SqlSimpleColumn entry | COVERED `:183` |
| S5.3 | no rename → no entry | COVERED `:77,208` |
| S5.4 | physical rename → sp_rename + logical re-bind | COVERED `MigrationRunTests.fs:174` |
| S5.5 | logical+physical rename together | COVERED `MigrationCanaryTests.fs:89` |
| S5.6 | logical-only rename → re-bind, no sp_rename (adversarial) | COVERED `MigrationRunTests.fs:207` |
| **AC-S6 rename ⊥ reshape (3/5)** | | |
| S6.1 | rename only → zero ALTER | COVERED `SME.fs:138` |
| S6.2 | widen only → one ALTER, zero refactorlog | COVERED `SME.fs:48-66` |
| S6.3 | **same column renamed AND widened** → refactorlog⊥ALTER both fire disjoint | **UNCOVERED** — the named adversarial input |
| S6.4 | kind rename + column add (disjoint channels) | COVERED `MigrationCanaryTests.fs:89` |
| S6.5 | rename + non-alterable facet change | **UNCOVERED** |
| **AC-S7 reference-rename refactorlog (1/5)** | | |
| S7.1 | renamed FK → `ReferenceDiff.Renamed` entry | **UNCOVERED** |
| S7.2 | renamed FK → refactorlog sp_rename | **UNCOVERED** — mechanism absent (N1) `RefactorLogEmitter.fs:254-267` |
| S7.3 | renamed FK surfaced in artifacts | **UNCOVERED** (N1) |
| S7.4 | unchanged FK → no spurious entry | COVERED (indirect) |
| S7.5 | renamed FK + renamed column together | **UNCOVERED** |
| **AC-S8 rename data-norm zero (0/4)** | | |
| S8.1 | rename on CDC-tracked table w/ data → capture=0 | **UNCOVERED** — `AxiomTests.fs:973` marks ⬚ |
| S8.2 | rename then redeploy → 0 captures | **UNCOVERED** |
| S8.3 | rename CDC vs data-update CDC boundary | **UNCOVERED** |
| S8.4 | table sp_rename + CDC capture-instance follows | **UNCOVERED** |
| **AC-S9 reshape facet completeness (12/25)** | 9 facets × {widen,narrow,no-op} | |
| S9.1 | DataType change, empty table | COVERED `:175` |
| S9.2 | DataType widen, populated | COVERED `SchemaMigrationCanaryTests.fs:53` |
| S9.3 | DataType narrow, populated | COVERED `SME.fs:113` |
| S9.4 | DataType no-op | COVERED `:235` |
| S9.5 | Nullability widen (NOTNULL→NULL) | COVERED `:211` |
| S9.6 | Nullability narrow (NULL→NOTNULL), populated | COVERED `SME.fs:113` |
| S9.7 | Nullability no-op | COVERED (construction) |
| S9.8 | **PrimaryKey change → unsupportedFacetChange** | **UNCOVERED** (mechanism present) |
| S9.9 | PrimaryKey no-op | COVERED (construction) |
| S9.10 | Length widen | COVERED `:200` |
| S9.11 | **Length narrow** | **UNCOVERED** |
| S9.12 | Length no-op | COVERED (construction) |
| S9.13 | **Precision widen** | **UNCOVERED** |
| S9.14 | **Precision narrow** | **UNCOVERED** |
| S9.15 | Precision no-op | COVERED (construction) |
| S9.16 | **Scale widen** | **UNCOVERED** |
| S9.17 | **Scale narrow** | **UNCOVERED** |
| S9.18 | Scale no-op | COVERED (construction) |
| S9.19 | **DECIMAL(10,2)→(18,4): both Precision AND Scale** | **UNCOVERED** — the named adversarial input |
| S9.20 | Identity change → refuse | COVERED `SME.fs:106` |
| S9.21 | Identity no-op | COVERED (construction) |
| S9.22 | DefaultValue change → refuse | COVERED `SME.fs:95` |
| S9.23 | DefaultValue no-op | COVERED (construction) |
| S9.24 | **Computed change** | **UNCOVERED** |
| S9.25 | Computed no-op | COVERED (construction) |
| **AC-S10 non-alterable refusal (4/6)** | | |
| S10.1 | DefaultValue change → unsupportedFacetChange | COVERED `SME.fs:95` |
| S10.2 | Identity change → refuse | COVERED `SME.fs:106` |
| S10.3 | **PrimaryKey change → refuse** | **UNCOVERED** (phantom-green; mechanism present) |
| S10.4 | **Computed change → refuse** | **UNCOVERED** (phantom-green) |
| S10.5 | multiple non-alterable facets together | **UNCOVERED** |
| S10.6 | non-shape change → RefusedBySchemaErrors | COVERED `MigrationRunTests.fs:97` |
| **AC-S11 narrowing surfaced (1/5)** | | |
| S11.1 | NULL→NOTNULL tightening → Warning | COVERED `SME.fs:113` |
| S11.2 | Length narrow → Warning | **UNCOVERED** |
| S11.3 | NVARCHAR(256)→(50) populated → Warning, proceeds (weaker than spec) | **UNCOVERED** |
| S11.4 | narrowing on empty table | **UNCOVERED** |
| S11.5 | narrowing promoted to declared-loss gate | **UNCOVERED** — the structural spec gap |
| **AC-S12 DacFx-seam purity (4/6)** | | |
| S12.1 | ALTER/DROP excluded from dacpac model | COVERED (structural) `DacpacEmitter.fs:77-86` |
| S12.2 | AlterTableAddColumn excluded | COVERED (structural) |
| S12.3 | AlterTableAddForeignKey excluded | COVERED (structural) |
| S12.4 | CreateTable included | COVERED `DacpacEmitterTests.fs:111` |
| S12.5 | CreateIndex included | COVERED (construction) |
| S12.6 | **mixed CreateTable+AlterColumn stream → ALTER absent from model** | **UNCOVERED** — the adversarial filtering test |

### 6.2 Data/CDC plane (AC-D1…AC-D10) — 64 obligations, 20 COVERED (31%)

| OB | Input → pass-iff | Coverage |
|---|---|---|
| **AC-D1 null-safe change-detection (4/16, 1 N-A)** | null-state × type | |
| D1.1 | nvarchar `NULL→'foo'` (left-null arm) → fires | COVERED `StaticSeedsEmitterTests.fs:305` |
| D1.2 | nvarchar `'foo'→NULL` (right-null arm) → fires | COVERED `:306` |
| D1.3 | nvarchar `NULL→NULL` → predicate false, 0 CDC | **UNCOVERED** (no operational witness) |
| D1.4 | nvarchar `'foo'→'foo'` → 0 CDC | COVERED `CdcSilenceTests.fs:271` |
| D1.5 | nvarchar `'foo'→'bar'` → fires | COVERED `:317` |
| D1.6–1.7 | **INTEGER** `NULL→5` / `5→NULL` | **UNCOVERED** (no nullable INT fixture) |
| D1.8–1.9 | **DECIMAL** `NULL→1.50` / `1.50→NULL` | **UNCOVERED** (Price is mandatory) |
| D1.10–1.11 | **BIT** `NULL→1` / `1→NULL` | **UNCOVERED** (Active is mandatory) |
| D1.12–1.13 | **DATETIME/DATE** null transitions | **UNCOVERED** (no temporal fixture) |
| D1.14 | **UNIQUEIDENTIFIER** `NULL→guid` | **UNCOVERED** |
| D1.15 | **VARBINARY** `NULL→0xCAFE` | **UNCOVERED** |
| D1.16 | MigrationDependencies MERGE null-arm | COVERED (structural) `MigrationDependenciesEmitterTests.fs:162` |
| D1.17 | Transfer BulkCopy null transition | N-A (BulkCopy is fresh-load; CDC-gate-guarded) |
| **AC-D2 silence floor (5/8)** | | |
| D2.1 | single-row Text redeploy → post=baseline | COVERED `CdcSilenceTests.fs:271` |
| D2.2 | multi-type row redeploy → 0 captures | COVERED `CdcSilencePropertyTests.fs:298` |
| D2.3 | 10-row identical redeploy → 0 | COVERED `:310` |
| D2.4 | **nullable column staying NULL → 0** | **UNCOVERED** (no nullable fixture) |
| D2.5 | **MigrationDependencies non-degenerate UpdColumns silence** | **UNCOVERED** (LegacyOrder UpdColumns empty) |
| D2.6 | Transfer CDC-gate refuses before write | COVERED `TransferCanaryTests.fs:285` |
| D2.7 | Phase-2 UPDATE (self-FK) silence | COVERED `CdcSilenceCrossEmitterTests.fs:405` |
| D2.8 | exact `post−baseline=0` for k=5 | COVERED (subsumed by exact-equality asserts) |
| **AC-D3 sensitivity (2/5)** | | |
| D3.1 | one Text column change → post>baseline | COVERED `CdcSilenceTests.fs:317` |
| D3.2 | composer-path change → post>baseline | COVERED `CdcSilenceCrossEmitterTests.fs:476` |
| D3.3 | **nullable NULL→value fires (null-arm operationally)** | **UNCOVERED** |
| D3.4 | MigrationDependencies sensitivity | **UNCOVERED** |
| D3.5 | exactly k=1 of n changed → exactly 2 captures | **UNCOVERED** |
| **AC-D4 capture=k general (1/5)** | | |
| D4.1 | k=0 (floor) | COVERED (via AC-D2) |
| D4.2 | **1 of n changed → post−baseline=2 exactly** | **UNCOVERED** — over-capture passes `>baseline` |
| D4.3 | **k=3 changed → post−baseline=6 exactly** | **UNCOVERED** |
| D4.4 | **all rows changed → 2n exactly** | **UNCOVERED** |
| D4.5 | **k=2 inserts → 2 captures exactly** | **UNCOVERED** |
| **AC-D5 computed excluded (0/3, 1 N-A)** | | |
| D5.1 | persisted computed col → excluded from UPDATE SET | **UNCOVERED** — no `Computed≠None` filter (N2) `StaticSeedsEmitter.fs:160` |
| D5.2 | same for MigrationDependencies emitter | **UNCOVERED** |
| D5.3 | virtual computed col → excluded | **UNCOVERED** |
| D5.4 | `Computed=None` (V1 dormant case) | N-A |
| **AC-D6 representation tolerated (0/6)** | | |
| D6.1 | `char(10)` ANSI-pad `'foo  '` vs `'foo'` → 0 CDC | **UNCOVERED** — no RTRIM (N3) |
| D6.2 | varchar trailing-space (SQL-native tolerance) | **UNCOVERED** |
| D6.3 | decimal scale `1.0` vs `1.00` → 0 CDC | **UNCOVERED** |
| D6.4 | empty-string↔NULL boundary | **UNCOVERED** |
| D6.5 | case-insensitive collation tolerance | **UNCOVERED** |
| D6.6 | unicode normalization | **UNCOVERED** |
| **AC-D7 Delete scope gate (0/5)** | | |
| D7.1 | no scope → no DELETE arm, T-rows survive | **UNCOVERED** — no `NotMatchedBySource` arm `ScriptDomBuild.fs:844-862` |
| D7.2 | scope S, r∈S not in source → deleted | **UNCOVERED** (mechanism absent) |
| D7.3 | **scope S, r∈T−S → survives (adversarial)** | **UNCOVERED** |
| D7.4 | scope S=T (full replace) → table-wide delete | **UNCOVERED** |
| D7.5 | DELETE arm present, no scope → refuse | **UNCOVERED** |
| **AC-D8 drop fail-loud (4/4)** | | |
| D8.1 | unmatched FK orphan → non-zero exit | COVERED `TransferCanaryTests.fs:389` |
| D8.2 | `--allow-drops` → exit 0 | COVERED `:423` |
| D8.3 | no orphans → exit 0 | COVERED `:259` |
| D8.4 | mid-load orphan tracked in SkippedReferences | COVERED `:365` |
| **AC-D9 FK ordering two-phase (4/6, 1 N-A)** | | |
| D9.1 | acyclic chain → no FK violation | COVERED `TransferCanaryTests.fs:273` |
| D9.2 | self-FK → phase-2 re-point | COVERED `:277` |
| D9.3 | **mutual cycle (A→B→A) operational** | **UNCOVERED** (structural only `StaticSeedsEmitterTests.fs:515`) |
| D9.4 | non-deferrable cycle → refuse | COVERED `DataLoadPlan.fs:162,188` |
| D9.5 | deletes reverse-ordered | N-A (pending AC-D7) |
| D9.6 | StaticSeeds 2-cycle MERGE path | COVERED `CdcSilenceCrossEmitterTests.fs:361` |
| **AC-D10 wipe-and-load mode (0/5)** | | |
| D10.1 | `EmissionMode.WipeAndLoad` → TRUNCATE+insert | **UNCOVERED** (no EmissionMode type) |
| D10.2 | `2·\|table\|` CDC cost named | **UNCOVERED** |
| D10.3 | mode distinct in type system | **UNCOVERED** |
| D10.4 | wipe-and-load CDC-gated | **UNCOVERED** |
| D10.5 | FK-ordered TRUNCATE | **UNCOVERED** |

### 6.3 Identity plane (AC-I1…AC-I7) — 45 obligations, 36 COVERED (80%)

| OB | Input → pass-iff | Coverage |
|---|---|---|
| **AC-I1 match by SsKey not name (5/6)** | | |
| I1.1 | two kinds same Name, distinct GUIDs → distinct | COVERED `SsKeyTests.fs:92-118` |
| I1.2 | `OssysOriginal(g)` ≠ `V1Mapped(g,_)` | COVERED `SsKeyTests.fs:92` |
| I1.3 | Synthesized vs OssysOriginal related content ≠ | COVERED `:100` |
| I1.4 | DerivedFrom different reasons ≠ | COVERED `:114` |
| I1.5 | reconcile indexes by column value not Name | COVERED `Reconciliation.fs:59-68`; `ReconciliationTests.fs:25` |
| I1.6 | **new SsKey + Name collides w/ removed → both, not rename** | **UNCOVERED** (phantom-green) |
| **AC-I2 re-key is Update not Delete+Insert (9/11)** | disposition × strategy | |
| I2.1 | ReconciledByRule + MatchByColumn full match | COVERED `TransferCanaryTests.fs:330` |
| I2.2 | ReconciledByRule + ManualOverride CSV | COVERED `:692` |
| I2.3 | partial match (orphan) → unmatched surfaces | COVERED `:330` |
| I2.4 | ManualOverride miss → unmatched | COVERED `ReconciliationTests.fs:43` |
| I2.5 | zero match | COVERED `:35` |
| I2.6 | AssignedBySink round-trip modulo remap | COVERED `TransferCanaryTests.fs:635` |
| I2.7 | PreservedFromSource (no remap) | COVERED `:273` |
| I2.8 | ByEmail strategy → mapping | COVERED `UserFkReflowPassTests.fs:75` |
| I2.9 | BySsKey strategy → mapping/diagnostic | COVERED `:249,276` |
| I2.10 | FallbackToSystemUser guarantee | COVERED `:353,370` |
| I2.11 | **ByEmail/BySsKey/Fallback through `runReconciling`** | **UNCOVERED** (phantom-green; Transfer-layer DU narrower) |
| **AC-I3 cyclic AssignedBySink refused (6/6)** | | |
| I3.1 | self-ref AssignedBySink Execute → refuse, 0 rows | COVERED `TransferCanaryTests.fs:435` |
| I3.2 | pure executeGate cyclic refusal | COVERED `TransferRefusalTests.fs:101,61` |
| I3.3 | DryRun does not refuse | COVERED `:435` |
| I3.4 | PreservedFromSource self-FK not flagged | COVERED `TransferRefusalTests.fs:72` |
| I3.5 | unbreakable cycle refuses first | COVERED `:92` |
| I3.6 | refusal precedes any write (0 rows) | COVERED `:435` |
| **AC-I4 composite surrogate refused (5/6)** | | |
| I4.1 | single-col IDENTITY → passes | COVERED `TransferRefusalTests.fs:114,85` |
| I4.2 | 2-col composite IDENTITY → refuse | COVERED `TransferCanaryTests.fs:481`; `TRT.fs:108,80` |
| I4.3 | **3-leg composite → refuse** | **UNCOVERED** (same predicate, no fixture) |
| I4.4 | composite PreservedFromSource → passes | COVERED `SurrogateRemapContextTests.fs:72` |
| I4.5 | refusal precedes write | COVERED `:481` |
| I4.6 | disposition survives ReadSide round-trip | COVERED `:481` |
| **AC-I5 validate-user-map pre-flight (3/6)** | | |
| I5.1 | **orphan → pre-write halt** | **UNCOVERED** — no `validateUserMap` gate (N4) |
| I5.2 | **fully-mapped → pre-flight passes** | **UNCOVERED** (gate absent) |
| I5.3 | orphan → post-write exit-9 (current, weaker class) | COVERED `TransferCanaryTests.fs:389` — **misleading: witnesses the rejected behavior** |
| I5.4 | isFullyMapped true on empty Unmatched | COVERED `UserRemapContextTests.fs:42` |
| I5.5 | isFullyMapped false on non-empty | COVERED `:46` |
| I5.6 | orphan drop fail-loud (P-DROP) | COVERED `TransferCanaryTests.fs:389` |
| **AC-I6 rename-aware re-point (6/6)** | | |
| I6.1 | rename EMAIL→CONTACT → value follows name | COVERED `TransferCanaryTests.fs:599` |
| I6.2 | pure repointRow by name | COVERED `RenameProjectionTests.fs:58` |
| I6.3 | reversed insertion-order → same result (adversarial) | COVERED `:72` |
| I6.4 | renames extraction from diff | COVERED `:47` |
| I6.5 | empty rename map = identity | COVERED `:84` |
| I6.6 | end-to-end diff-derived re-point | COVERED `:90` |
| **AC-I7 rename + reconcile composed (2/4)** | | |
| I7.1 | **rename + re-key in one run** | **UNCOVERED** — no composed entrypoint `TransferRun.fs:421` |
| I7.2 | rename-only sub-path | COVERED (via I6.1) |
| I7.3 | reconcile-only sub-path | COVERED (via I2.1) |
| I7.4 | **rename+reorder+reconcile (ordinal-collision adversarial)** | **UNCOVERED** |

### 6.4 Provenance plane (AC-P1…AC-P9) — 63 obligations, 42 COVERED (67%)

| OB | Input → pass-iff | Coverage |
|---|---|---|
| **AC-P1 no-cheat applyDiff (1 full + 3 W3-only/3 unc)** | channel × base-divergence | |
| P1.1 | base A' has extra **kind** not in diff → survives | COVERED `CatalogDiffTests.fs:314` |
| P1.2 | base missing a kind the diff removes | **UNCOVERED** |
| P1.3 | base with divergent attribute facet | **UNCOVERED** |
| P1.4 | **no-cheat on reference channel** (A'≠source) | **UNCOVERED** — W3 only `:531` |
| P1.5 | **no-cheat on index channel** | **UNCOVERED** — W3 only `:577` |
| P1.6 | **no-cheat on sequence channel** | **UNCOVERED** — W3 only `:615` |
| P1.7 | no-cheat on attribute channel (extra attr) | **UNCOVERED** (always A'=source) |
| **AC-P2 round-trip W3 (6/8)** | | |
| P2.1 | kind rename + attr changes round-trip | COVERED `:263` |
| P2.2 | reference channel round-trip | COVERED `:531,546,562` |
| P2.3 | index channel (incl. ALLOW_PAGE_LOCKS) | COVERED `:577,588,598` |
| P2.4 | sequence channel | COVERED `:615,625` |
| P2.5 | all four channels at once | COVERED `:636` |
| P2.6 | A=B degenerate (W1) | COVERED `:294` |
| P2.7 | genesis (empty A) round-trip | **UNCOVERED** |
| P2.8 | **DECIMAL(10,2)→(18,4) round-trip** | **UNCOVERED** — phantom-green §7.1 |
| **AC-P3 identity diff W1 (4/4)** | | |
| P3.1 | between(A,A) isEmpty | COVERED `:44` |
| P3.2 | apply(between(A,A),A)=A | COVERED `:294` |
| P3.3 | isEmpty over ref/index/seq for identity | COVERED `:355` |
| P3.4 | norm(between(A,A))=0 | COVERED `:355` |
| **AC-P4 composition W2 (3/6)** | | |
| P4.1 | 2-episode compose functor law | COVERED `:399` |
| P4.2 | 3-episode associativity | COVERED `:419` |
| P4.3 | non-adjacent → None (fail-loud) | COVERED `:413` |
| P4.4 | genesis (empty A) compose | **UNCOVERED** |
| P4.5 | **production consumer of compose** | **UNCOVERED** — zero callers (N7) |
| P4.6 | compose over ref/index/seq channels | **UNCOVERED** |
| **AC-P5 durable FTC (8/8)** | | |
| P5.1 | in-memory fold | COVERED `LifecycleTests.fs:156` |
| P5.2 | save→reload→reconstruct | COVERED `LifecycleStoreTests.fs:68` |
| P5.3 | all planes-but-Profile survive | COVERED `:78` |
| P5.4 | corrupt file → ParseFailure | COVERED `:148,140` |
| P5.5 | missing file → ParseFailure | COVERED `:133` |
| P5.6 | save byte-deterministic (T1) | COVERED `:120` |
| P5.7 | Named environment round-trips | COVERED `:92` |
| P5.8 | record→reload→reconstruct reproduces B | COVERED `MigrationRunTests.fs:117` |
| **AC-P6 refactorlog against-prior (0/5)** | | |
| P6.1 | dedup by OperationKey vs prior log | **UNCOVERED** (no accumulate path) |
| P6.2 | real episode timestamp threaded | **UNCOVERED** — pinned constant (N6) `RefactorLogRender.fs:55` |
| P6.3 | fresh-env full-history replay | **UNCOVERED** |
| P6.4 | engine loads prior refactorlog as dedup base | **UNCOVERED** (fork #1) |
| P6.5 | reference-rename → refactorlog entry | **UNCOVERED** (N1) |
| **AC-P7 T16 master equation live (10/10)** | | |
| P7.1 | regime (c) two-model pure | COVERED `MigrationTests.fs:59` |
| P7.2 | regime (b) live read-back | COVERED `MigrationCanaryTests.fs` execute |
| P7.3 | regime (a) previewFromStore | COVERED `MigrationRunTests.fs:151` |
| P7.4 | P-CH channel disjointness | COVERED `MigrationTests.fs:78,91,99` |
| P7.5 | live 3-channel execute, data survives, idempotent | COVERED `MigrationCanaryTests.fs:89` |
| P7.6 | column rename via sp_rename, data preserved | COVERED `:182` |
| P7.7 | executeWithData (cross-substrate) | COVERED `:213` |
| P7.8 | migrate A A idempotent | COVERED `MigrationTests.fs:67` |
| P7.9 | destructive drop refuses before write | COVERED `MigrationCanaryTests.fs:152` |
| P7.10 | CDC-tracking gate on migrate | COVERED `:248` |
| **AC-P8 migrate records episode (4/5)** | | |
| P8.1 | record opens timeline at genesis | COVERED `MigrationRunTests.fs:108` |
| P8.2 | record→reload→reconstruct=B | COVERED `:117` |
| P8.3 | non-monotonic version refused | COVERED `:217` |
| P8.4 | **CLI `--execute` calls record** | **UNCOVERED** — never called `Program.fs:1028` |
| P8.5 | idempotent previewFromStore after record | COVERED `:151` |
| **AC-P9 change-manifest is displacement (6/10)** | | |
| P9.1 | per-channel move counts | COVERED `ChangeManifestTests.fs:50` |
| P9.2 | refactorlog xref | COVERED `:56` |
| P9.3 | CDC series per edge | COVERED `:72` |
| P9.4 | path length > net under churn | COVERED `:86` |
| P9.5 | idempotent edge norm 0 | COVERED `:64` |
| P9.6 | **ToleranceResidual field** | **UNCOVERED** — field absent from type |
| P9.7 | **AppliedTransforms outcome field** | **UNCOVERED** — field absent |
| P9.8 | CDC structured handle (not just count) | PARTIAL — count only |
| P9.9 | **production consumer of manifest** | **UNCOVERED** — zero callers (N7) |
| P9.10 | ref/index/seq channels in manifest | COVERED `CatalogDiffTests.fs:658` |

### 6.5 Gate plane (AC-G0…AC-G10) — 69 obligations, 35 COVERED (51%)

| OB | Input → pass-iff (refuses before any write) | Coverage |
|---|---|---|
| **AC-G0 Preflight.all mandatory (0/4)** | | |
| G0.1 | MC composes via `Preflight.all` | **UNCOVERED** — hand-chained (N5) |
| G0.2 | MX composes via `Preflight.all` | **UNCOVERED** |
| G0.3 | TR composes via `Preflight.all` | **UNCOVERED** — TR has no gates at all |
| G0.4 | a gate in `all` cannot be bypassed | **UNCOVERED** (structural) |
| **AC-G1 Connection (5/8)** | | |
| G1.1 | MC source dead → refuse | COVERED `PreflightTests.fs:81` |
| G1.2 | MC sink dead → refuse | COVERED (both roles) |
| G1.3 | MC login NULL → refuse | COVERED `:89` |
| G1.4 | MX source dead | COVERED `Program.fs:1094` |
| G1.5 | MX sink dead | COVERED |
| G1.6 | **TR source dead** | **UNCOVERED** (no preflight on TR) |
| G1.7 | **TR sink dead** | **UNCOVERED** |
| G1.8 | MC Docker refusal witness | **UNCOVERED** (pure only) |
| **AC-G2 Permission (4/9, 1 P)** | | |
| G2.1 | db-scope grant → passes | COVERED `PreflightTests.fs:113` |
| G2.2 | object-scope grant → passes | COVERED `:107` |
| G2.3 | **db-scope grant but object-scope DENY → refuse** | **UNCOVERED** — probe is `fn_my_permissions(NULL,'DATABASE')` only |
| G2.4 | SELECT-only sink, INSERT denied → refuse | COVERED `:119,129` |
| G2.5 | INSERT but not ALTER → refuse | COVERED `:129` |
| G2.6 | MX source login insufficient | PARTIAL (sink grants only) |
| G2.7 | **TR write-denied sink → zero rows exit-0** | **UNCOVERED** (no perm gate on TR) |
| G2.8 | **TR object-scope partial write** | **UNCOVERED** |
| G2.9 | MC Docker refusal witness | **UNCOVERED** (pure only) |
| **AC-G3 declared-loss DROP (8/8, 1 N-A)** — CLOSED | column/FK/index/seq/kind drop × Declare{None,These,All} | COVERED `MigrationRunTests.fs:87`, `SchemaMigrationEmitterTests.fs:209,214,221`, `MigrationTests.fs:122,154`, `MigrationCanaryTests.fs:152`; TR N-A |
| **AC-G4 delete-scope (0/5)** — ABSENT | no-scope/scoped/T−S-survive/full-replace/refuse-unscoped | **UNCOVERED** — no `NotMatchedBySource` arm |
| **AC-G5 CDC-tracking (7/7)** — CLOSED | TR refuse/override/untracked; MC tracked+DDL refuse; MC empty-diff skip; MX schema+data legs | COVERED `TransferCanaryTests.fs:285,320`, `MigrationCanaryTests.fs:248,268` |
| **AC-G6 CDC-silence-on-idempotence (5/5)** — CLOSED | MC idempotent zero-DDL; data silence; sensitivity; MX; null-arm fires | COVERED `MigrationRunTests.fs:75,263`, `CdcSilenceTests.fs:239,274,250` |
| **AC-G7 data-compat NOT-NULL (5/8)** | | |
| G7.1 | pure: nullCount>0 + EnforceNotNull → violation | COVERED `PreflightTests.fs:29` |
| G7.2 | pure: nullCount=0 → no violation | COVERED `:42` |
| G7.3 | pure: NULLs but not tightened → no violation | COVERED `:48` |
| G7.4 | Docker: live source NULLs → refuse | COVERED `TransferCanaryTests.fs:557` |
| G7.5 | Docker: empty overlay → passes | COVERED `:577` |
| G7.6 | **MC `--execute` calls tighteningPreflight** | **UNCOVERED** — built-not-wired |
| G7.7 | **MX `--execute` calls it** | **UNCOVERED** |
| G7.8 | **TR `--execute` calls it** | **UNCOVERED** |
| **AC-G8 possible-data-loss (1/4, 2 P, 1 N-A)** | | |
| G8.1 | NULL→NOTNULL narrowing → Warning (proceeds, weaker) | PARTIAL `SchemaMigrationEmitterTests.fs:113` |
| G8.2 | **VARCHAR(256)→(50) → refuse** | **UNCOVERED** (warn only; DacFx backstop at publish) |
| G8.3 | safe widening → no false-fire | COVERED |
| G8.4 | MX narrowing | PARTIAL |
| G8.5 | TR narrowing | N-A |
| **AC-G9 on-disk-bytes in-place (0/3, 1 N-A)** — ABSENT | MC NULL rows + ALTER NOTNULL → refuse pre-flight; pass-case; MX | **UNCOVERED** — no `COUNT(*) WHERE col IS NULL` probe (caught post-facto as ExecutionFailed) |
| **AC-G10 transactional/resumable (0/5, 1 P)** — SCAFFOLD | crash after phase-1; retry idempotent; crash mid-phase-2; MC mid-ALTER (P: SQL implicit rollback); MX between legs | **UNCOVERED** — no envelope `Preflight.fs:290-302` |

### 6.6 Protein plane (AC-X1…AC-X8) — 94 obligations, ~35 reachable (37%)

Obligations are the ordered §3 chain steps + terminal observables. `✗` = chain breaks (one-command path
stops). Reachable counts in §4's diagrams.

| OB | Chain step / terminal → pass-iff | Coverage |
|---|---|---|
| **AC-X1 P-1/P-2 load (3/11)** | | |
| X1.1 Snapshot | `full-export`→`Compose.runWithConfig` | COVERED `FullExportRun.fs:161` |
| X1.2 **Diff-vs-prior** | between current prior | **UNCOVERED** — no LifecycleStore read; every run genesis |
| X1.3 Gate-loss | declared-loss before publish | **UNCOVERED** (migrate path only) |
| X1.4 Rename-accum | refactorlog append-dedup | **UNCOVERED** (G9/N6) |
| X1.5 Reshape / X1.6 Add / X1.7 Publish | CREATE files to disk | COVERED `SsdtDdlEmitter` |
| X1.8 Insert/Update/Unchanged | CDC-aware MERGE | **UNCOVERED** — no data leg in full-export |
| X1.9 Measure / X1.10 Verify / X1.11 Record | CDC count / readback / episode | **UNCOVERED** |
| X1.T1 CDC-silent re-run / X1.T2 episode recorded | terminal | **UNCOVERED** |
| **AC-X2 P-3 UAT re-key (6/13, not composed)** | | |
| X2.1 Snapshot / X2.2 Diff / X2.3 Gate-schema | migrate cross-substrate | COVERED `Program.fs:1088-1103` |
| X2.4 **Gate-validate-user-map** | halt before SQL | **UNCOVERED** (N4) |
| X2.5 Rename / X2.6 Reshape/Add | schema channels | COVERED `MigrationRun.fs:324-351` |
| X2.7 Reconcile / X2.8 Reidentify | re-key user FKs | COVERED on `transfer` only — `executeWithData` passes `Map.empty` (not composed) |
| X2.9 Publish-schema | schema DDL | COVERED (not composed w/ data) |
| X2.10 Insert / X2.11 Measure / X2.12 Verify / X2.13 Record | data leg + audit | **UNCOVERED** in composition |
| X2.T1 one-command compose / X2.T2 no orphan FK | terminal | **UNCOVERED** / PARTIAL (post-write exit-9) |
| **AC-X3 P-4 SSIS pub (2/9)** | | |
| X3.1 Snapshot / X3.6 Add | emit CREATE | COVERED |
| X3.2 Diff-vs-prior / X3.3 Declare-loss / X3.5 Reshape-preview / X3.7 Accumulate / X3.8 Publish-bundle / X3.9 Record | | **UNCOVERED** (fork #1 unresolved) |
| X3.4 Rename | per-sprint refactorlog | PARTIAL (not accumulated) |
| X3.T1 fresh reconstruct / X3.T2 P-PROV dedup | terminal | **UNCOVERED** |
| **AC-X4 P-5 redeploy (5/8)** | | |
| X4.1 Snapshot / X4.2 Diff-empty / X4.3 Gate / X4.5 Publish-zero-DDL / X4.8 Verify | | COVERED `MigrationRun.fs`; T1 `MigrationCanaryTests.fs:146` |
| X4.4 Tolerate | named substrate noise | PARTIAL (N3 absent) |
| X4.6 Unchanged-data / X4.7 Measure-CDC=0 / X4.T2 | data MERGE + CDC | **UNCOVERED** (no data leg in migrate) |
| **AC-X5 P-6 migrate (7/12)** | | |
| X5.1-5.2 Snapshot×2 / X5.3 Gate-conn+perm / X5.4 Diff / X5.6 Rename / X5.7 Reshape / X5.8 Add / X5.11 Verify | | COVERED `Program.fs:997-1025`, `MigrationCanaryTests.fs:89`; T1/T3 idempotent `:142` |
| X5.5 Gate-loss+compat | | PARTIAL (compat built-not-wired) |
| X5.9 **Move-CDC-MERGE vs existing data** | | **UNCOVERED** — `executeWithData` Map.empty; in-place has no data leg |
| X5.10 Measure / X5.12 Record / X5.T2 / X5.T4 | CDC + episode | **UNCOVERED** |
| **AC-X6 P-7 eject (0/8)** — ABSENT | Snapshot/Diff-genesis/Accumulate/Declare/Gate-provenance/Publish-terminal/Record/Verify + T1/T2 | **UNCOVERED** — no eject verb (fork #3 unresolved) |
| **AC-X7 P-8 drift (1/8)** | | |
| X7.1 Snapshot-deployed | verify-data | PARTIAL (data-only row/null) |
| X7.2 Snapshot-model / X7.3 Diff / X7.4 Tolerate / X7.5 Declare-drift / X7.6 Gate / X7.7 Publish-remediation / X7.8 Measure / X7.T1 | schema-drift-vs-model | **UNCOVERED** — no such path |
| **AC-X8 P-9 canary (5/8)** | | |
| X8.1 Snapshot / X8.2 Publish-deploy / X8.3 Snapshot-readback / X8.4 Diff-roundtrip / X8.6 Gate-isEmpty | | COVERED `Program.fs:349-365`, `Deploy.runWideCanary` |
| X8.5 Tolerate | named erasures | PARTIAL (N3 absent) |
| X8.7 Measure-CDC-silence / X8.8 Verify-migration / X8.T2 | CDC in canary CLI | **UNCOVERED** (test-only `CdcSilenceTests.fs:239`) |
| X8.T1 Ingest∘Project=id mod tolerances | terminal | COVERED `Program.fs:360-365` |

---

— Pass four recorded for the receiving agent. The four passes now compose into a self-measuring
instrument: **target** (the masterwork) → **distance** (the fitness overlay) → **falsifiable test of the
distance** (the acceptance suite) → **the obligation-level census of that test** (this). At any moment
the matrix can state not just which promises are made, but which are adversarially proven, which are
green-but-unproven (the phantom-greens), and which are unbuilt — as a count. Build down §5, refresh
§1's numbers as obligations go green, and the engine's fidelity becomes a measured quantity rather than
an assertion.

---

## 7 — The criterion-anchored two-leg regrade (measured, supersedes the §1 count)

Per the §0 discipline, each of the 57 acceptance criteria was judged **criterion-first** against two
independent legs — implemented reality (the code path, read independent of any test) and the live test
(does the green test actually discriminate). Liveness was verified (full suite green at HEAD `3c2c854`).
This is the measured truth; §1's 52% was an inspection upper bound.

### Global grade scorecard

**Batch 1 landed:** +7 to HELD. Track A closed S1/S9/P1/P2; Track B closed S6 + S12 (STRUCTURAL→HELD)
+ the S10.3/4 sub-gaps; Track C wired `MigrationRun.record` into `migrate --execute --lifecycle-store`
(P8 HOLLOW→HELD; record-leg of proteins X1/X5 advanced, both still HOLLOW pending CDC-measure +
diff-vs-prior).

**Batch 2 landed (HEAD after this commit):** +3 to HELD. Track D — CDC type×null-state sweep (8 types ×
{NULL→value, value→NULL, NULL→NULL}, exact ±2/0) closed **D1 CODE-ONLY→HELD** (spike correction: nullable
columns set `Column.IsNullable=true`, not `IsMandatory`). Track E — narrowing promoted from Warning to a
declared-loss refusal (gated on the existing `allowDrops`) closed **G8 HOLLOW→HELD** and **S11
CODE-ONLY→HELD**. Integration caught real cross-batch blast radius: E's gate refused a narrowing in
Track C's AC-P8 fixture (`verifiedOutcome` now uses `DeclareAll`).

**Batch 2b landed (Track F; HEAD after this commit):** +3 to HELD. Gate wiring — **G7 CODE-ONLY→HELD**
(`tighteningPreflight` wired into `migrate --execute` MC + MX, guarded by an empty-overlay short-circuit,
with a diff-derived `tightenedToNotNull` overlay), **G1 + G2 CODE-ONLY→HELD** (a `spanningPreflight`
connection + permission gate wired into the transfer execute path before any write). **G0 deferred**
(stays NEITHER): routing all verbs through `Preflight.all` is a reporting refactor that would risk
regressing the per-gate exit codes — the obligation ("refuse before write") is already met by the
per-gate seams; G0 is a composition convenience, queued as its own slice with a `code→(exit,label)`
mapping. Integrated via git 3-way cherry-pick (F's worktree base far-drifted); the AC-I5/Track-C edits
in `Program.fs`/`TransferRun.fs` were preserved (G7 wraps the record-wiring; `validateUserMap` and
`spanningPreflight` coexist). Scorecard below is post-Batch-2b.

| Plane | HELD | CODE-ONLY | HOLLOW | NEITHER | STRUCTURAL |
|---|---|---|---|---|---|
| Schema (AC-S) | 10 | 0 | 0 | 2 | 0 |
| Identity (AC-I) | 5 | 1 | 0 | 1 | — |
| Gates (AC-G) | 7 | 0 | 0 | 4 | — |
| Provenance (AC-P) | 6 | 1 | 1 | 1 | — |
| Data/CDC (AC-D) | 3 | 3 | 0 | 4 | — |
| Proteins (AC-X) | 0 | 0 | 6 | 2 | — |
| **Total (57)** | **31** | **5** | **7** | **14** | **0** |

*(Baseline HELD 18 → B1 → 25 → B2 → 28 → B2b → 31 (54%). CODE-ONLY 15→10→8→5; HOLLOW 9→8→7.)*

**The reading (post-Batch-2b).** Genuinely solid (HELD) = **31 of 57 (54%)** — up from 19 (33%) at the
baseline, *still not* the ~24 PASS the test-first pass implied (those were generous in the wrong cells).
**7 cells (12%) are HOLLOW** — green tests that do not establish the criterion (the phantom-greens).
**5 (9%) are CODE-ONLY** — the code is correct but no test discriminates the criterion's adversarial
input, so a plausible wrong refactor passes the whole suite. **14 (25%) are NEITHER** — genuine gaps.
The HOLLOW mass concentrates exactly where the criteria demand *composed,
operator-reachable* behavior (proteins) or *wired* behavior (gates/provenance) — the places a green
unit/harness test exercises a function in isolation that the production path never composes.

### Per-AC grades

- **Schema:** HELD S1, S2, S3, S4, S5, S6, S9, S10, S11, S12 · NEITHER S7, S8. *(Batch 1: S1, S6, S9, S12→HELD. Batch 2: S11 CODE-ONLY→HELD.)*
- **Identity:** HELD I1, I3, I4, **I5** (fixed this session — now genuinely HELD), I6 · CODE-ONLY I2 · NEITHER I7.
- **Gates:** HELD G1, G2, G3, G5, G6, G7, G8 · NEITHER G0, G4, G9, G10. *(Batch 2: G8→HELD. Batch 2b: G1/G2/G7 wired→HELD; G0 deferred — `Preflight.all` reporting refactor, obligation already met by per-gate seams.)*
- **Provenance:** HELD P1, P2, P3, P5, P7, P8 · CODE-ONLY P4 · HOLLOW P9 · NEITHER P6. *(Batch 1: P1, P2 CODE-ONLY→HELD; P8 HOLLOW→HELD.)*
- **Data/CDC:** HELD D1, D8, D9 · CODE-ONLY D2, D3, D4 · NEITHER D5, D6, D7, D10. *(Batch 2: D1 CODE-ONLY→HELD — 8 types × null-state, exact ±2/0. D2/D3 retain only the MigrationDependencies-emitter live witness; D4 the k>1 exact-count.)*
- **Proteins:** HOLLOW X1, X2, X4, X5, X7, X8 · NEITHER X3, X6.

### The HOLLOW register (7) — green tests that don't establish the criterion (top priority)

| AC | Why hollow | Shared root cause |
|---|---|---|
| ~~**G8** narrowing~~ | **RESOLVED Batch 2** → HELD: narrowing promoted from Warning to a declared-loss refusal (gated on `allowDrops`) | ~~gate emits wrong severity~~ |
| ~~**P8** migrate-records-episode~~ | **RESOLVED Batch 1** → HELD: `record` wired into `migrate --execute --lifecycle-store` via the tested `executeAndRecord`/`recordVerified` seam | ~~record-not-wired~~ |
| **P9** change-manifest | type is *missing* `ToleranceResidual` + `AppliedTransforms`; tests assert only what exists | incomplete type + no consumer |
| **X1** P-1/P-2 load | `full-export` halts at publish; record + CDC-measure tested via harness only | record-not-wired · no-CDC-in-CLI · no-diff-vs-prior |
| **X2** P-3 UAT re-key | `executeWithData` passes `Map.empty`; the canary passes `Map.empty` too — **co-wrong** | **re-key-not-composed** |
| **X4** P-5 redeploy | schema idempotence reachable; CDC=0 measure is harness-only | no-CDC-in-CLI |
| **X5** P-6 in-place migrate | 7/12 schema steps reachable; Move-data + Measure-CDC + Record harness-only | record-not-wired · no-CDC-in-CLI · no-data-leg |
| **X7** P-8 drift | `verify-data` compares two substrates, not deployed-vs-model; test asserts the weaker behavior | no-diff-vs-model |
| **X8** P-9 canary | PhysicalSchema round-trip HELD; CDC-silence measure harness-only | no-CDC-in-CLI |

The protein HOLLOWs are **not 6 independent problems** — they collapse onto four shared seams:
**record-not-wired** (P8 → X1, X5), **no-CDC-count-in-any-CLI-verb** (X1, X4, X5, X8), **re-key-not-
composed** (X2), **no-diff-vs-prior / vs-model** (X1, X3, X7). Fix the seam, lift several cells.

### The CODE-ONLY register (5) — correct but unguarded (a wrong refactor passes the suite)

*Batch 1 closed S1/S6/S9/P1/P2; Batch 2 closed S11/D1; Batch 2b wired G1/G2/G7.* Remaining — pure-test
closers (no production code): **D2/D3** (only the MigrationDependencies-emitter live witness now missing —
the nullable null-state gaps D2.4/D3.3 closed in Batch 2), **D4** (k>1 exact-count). Wiring/consumer
closers: **I2** (ByEmail/BySsKey/Fallback never reach `runReconciling` — a *reality* gap), **P4**
(compose has zero production callers). All five fall to **Round 4**.

---

## 8 — The re-ranked queue (HOLLOW-first, by leverage)

Recovering falsely-claimed value (HOLLOW) and guarding correct-but-unverified code (CODE-ONLY) beats
greenfield (NEITHER). Ordered by ROI = (criterion impact × shared-seam leverage) ÷ (cost × risk).

**✅ Batch 1 complete:** Track A (S1/S9/P1/P2) · Track B (S6/S12 + S10.3/4) · Track C (P8 record-wiring).
+7 to HELD.

**✅ Batch 2 complete:** Track D (CDC type×null-state sweep → D1) · Track E (narrowing declared-loss
refusal → G8 + S11). +3 to HELD; suite green (2694 pure / 145 Docker / 0 fail). Cross-batch blast radius
caught + fixed (E's gate vs Track C's AC-P8 narrowing fixture → `DeclareAll`).

**Next — Batch 2b: Track F** gate wiring (G7 tightening on the execute verbs; G1/G2 connection/permission
on `transfer`; G0 `Preflight.all` mandatory) — Docker-witness-heavy + touches `Program.fs`/`Preflight.fs`/
`TransferRun.fs`, so it runs **solo** (Docker free now that D is done). Then **Batch 3**: protein
composition (the shared seams — CDC-measure-in-CLI lifts X4/X8/X1/X5; re-key compose X2; diff-vs-prior/
model X1/X3/X7) + remaining NEITHER mechanisms + the two remaining HOLLOWs (P9 manifest fields; the
protein cells). **Integration discipline (learned Batch 1–2):** worktree subagents must report
diffs/snippets to apply onto HEAD — wholesale file copies revert recent work when the worktree base drifts.

**Round 5–6 forks — RESOLVED (operator, this session):**
- **Eject (X6): append-forever.** The terminal bundle preserves every episode + the full accumulated
  refactorlog; any prior state is reconstructable (a downstream consumer may DacFx-publish an
  intermediate pre-freeze state). No collapse at freeze.
- **Refactorlog accumulation (P6 + X3): engine-input.** The engine reads the prior committed
  `.refactorlog` at emit time and accumulates/dedups against it by `OperationKey`, and threads the
  *real* episode clock (retire the pinned `2000-01-01` constant). The engine owns the merged log
  (not repo-merge-time).
- **Crash safety (G10): resumable/idempotent.** A mid-load failure is recoverable by re-running the
  same command (idempotent upsert + phase tracking) — not a single all-or-nothing transaction
  envelope, not document-only.
- **Wipe-and-load (D10): explicit named mode.** Build a named `EmissionMode` the operator selects;
  document the `2·|table|` CDC cost; gate it like other destructive ops. Incremental MERGE stays the
  default. (Not the PROD-empty default; not deferred.)
- **Protein composition (X1/X2/X4/X5/X7/X8), G9, I7 — build per the criteria** by *extending existing
  verbs* (migrate grows measure+record legs; full-export grows diff-vs-prior; a deployed-vs-model
  drift check), not new verbs, unless the operator redirects.

**Tier A — convert HOLLOW→HELD with a small wiring fix (highest ROI):**
1. **P8 — wire `MigrationRun.record` into `runMigrateExecute`** (+ a test asserting the CLI persists the
   episode). Same shape as this session's AC-I5 fix (function exists & is tested; one call site). Lifts
   P8 *and* the record-leg of proteins X1/X5. (Recon caveat: confirm the store-path argument/convention.)
2. **G8 — promote the narrowing `Warning` to a declared-loss refusal** (mirror the G3 gate). Converts the
   one gate HOLLOW to HELD; small.

**Tier B — close CODE-ONLY with pure/Docker tests (no production code; `runScenario` now fixed):**
3. Schema adversarial pure tests: **S6.3** (rename+widen disjoint — do first, highest refactor-risk),
   **S9.19** (DECIMAL), **S1.2** (name-collision), **S12.6** (mixed-stream), **S10.3/4** (PK/Computed).
4. **P1** no-cheat tests on the reference/index/sequence channels; **P2.8** DECIMAL round-trip.
5. **D1** type×null-state CDC sweep (the 9 untested types) + **D4** k>1 exact-count — now unblocked.

**Tier C — wire built-not-wired gates (small–medium code):**
6. **G7** tightening into `migrate --execute` (recon already scoped); then **G1/G2** connection/permission
   into `runTransfer`; then **G0** make `Preflight.all` the mandatory entry on all three verbs.
7. **I2** route the three user-match strategies through `runReconciling`; **P4** land a compose consumer.

**Tier D — build new mechanism / compose the protein chains (larger; shared-seam order):**
8. **no-CDC-count-in-CLI** seam → lifts X4, X8, and the measure-leg of X1/X5. **re-key compose** (X2:
   thread a non-empty reconciliation through `executeWithData`). **diff-vs-prior/model** (X1, X3, X7).
9. The remaining NEITHER mechanisms: S7 (reference-rename refactorlog), S8 (rename-CDC canary), D5
   (computed exclusion), D6 (representation tolerances), D7/G4 (delete-scope), D10 (wipe-and-load), G9
   (on-disk probe), G10 (transactional), P6 (refactorlog accumulate + real clock), I7 (rename+reconcile
   compose), X6 (eject — after the append-vs-collapse fork is resolved).

**The single highest-leverage next move:** Tier A #1 (wire `record`) — it is a one-call-site fix that
flips a HOLLOW to HELD and simultaneously advances two protein cells, exactly the shared-seam leverage
the regrade exposed.

---

— Five passes now compose: **target** (masterwork) → **distance** (fitness) → **falsifiable test of the
distance** (acceptance) → **obligation census** (§§1–6) → **criterion-anchored two-leg grade** (§§0,7).
The instrument no longer asserts coverage; it measures it, against the criterion, on two independent
legs, with a liveness stamp that must be re-earned. Refresh §7's grades whenever a code path or test
changes; re-run the liveness stamp after any typed-VO lift, IR change, or Docker-state change.
