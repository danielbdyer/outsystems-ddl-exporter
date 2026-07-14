# Cutover Reconciliation — the estate-convergence mode (ideation)

> **Status.** Ideation, not a decision. This document proposes no axiom, opens no chapter, and
> changes no code on its own authority. It is the synthesis of a five-surface read (the use-case
> ontology, the cross-environment readiness corpus, the CLI surface, the Core profiling/policy
> algebra, and the V1 multi-environment donors) toward one question the operator asked:
> *what would a verb/mode look like that reconciles dev / QA / UAT toward full unification —
> schema, data, and interim DDL posture — for the cutover?*
>
> **Provenance.** Anchors cite `file:line` at the tree this was written against; re-verify before
> acting (the tree moves). Canonical surfaces this defers to: `THE_USE_CASE_ONTOLOGY.md` (the
> target), `CROSS_ENVIRONMENT_READINESS.md` (the shipped `check shape` design),
> `THE_CLI.md` (the verb grain), `THE_CONFIG_CONTROL_PLANE.md` (A44), `DECISIONS.md` (standing law).

---

## 0 — The ask, in one sentence

Query dev, QA, and UAT together; notate every schema divergence between them (target: one
shape); profile all three; measure every place each environment's *data* contradicts the target
DDL (target: no NULLs in NOT NULL columns, no FK orphans, no AutoNumber static entities, …);
and emit, from those findings, both the SQL that would repair the data **and** the interim DDL
posture (e.g. untrack an FK carrying an excessive orphan count) with a named path back to full
compliance.

The one-sentence thesis of this document: **almost every primitive this mode needs already
exists — what is missing is the estate-level composition, the consensus semantics, and the
prescriptive output surfaces.** The mode is a new *protein* folded from existing amino acids
(Snapshot · Diff · Tolerate · Declare · Gate · Measure · Publish · Record), introducing one
genuinely new comparison regime (deployed↔deployed across the environment lattice) and one
genuinely new output plane (recommendations *back into* the model/config, not just refusals).

---

## 1 — What the estate already knows how to do

The inventory that makes this mode mostly composition rather than construction:

| Capability | Where | What the new mode takes from it |
|---|---|---|
| Pairwise env compare (schema delta + data dealbreakers) | `Compare.fs` (`compare <A> <B>` → `compare.json`) | The per-pair report shape; `isCompatible`; the `Operand = Catalog × Profile option` carrier |
| N-env readiness gate vs an agreed shape | `Readiness.fs` + `Faces/Diff.fs:129` (`check shape` → `readiness.json`, exit 0/5/6) | The verdict vocabulary (`Ready / Paused / Blocked`), the `readiness` config block, espace-safe OSSYS reads, `toLogicalShape` |
| The dealbreaker engine | `ModelFidelity.fs:57` (`ViolationKind`: NULLs-in-NOT-NULL, dupes-under-UNIQUE/PK, FK orphans, length/type overflow) | The four core data-vs-DDL detectors, the count-first rollup, `fidelity.json` codec |
| Evidence union across environments | `Profile.merge` (`Profile.fs:1391`) — commutative, associative, `Profile.empty` identity; built explicitly for "dev + UAT + prod evidence unioned" | The lawful worst-case join the estate decision runs on; `SqlProfilerOptions.EnvironmentTag` labels the source env |
| Tightening decision rules with structured outcomes | `NullabilityRules` / `ForeignKeyRules` / `UniqueIndexRules` / `CategoricalUniquenessRules` (`Strategies/`) | `DoNotEnforce (DataHasOrphans n)`, `EnforceConstraint (ScriptWithNoCheck n)`, `RequireOperatorApproval (MandatoryButHasNullsBeyondBudget …)`, `KeepNullable (RelaxedUnderEvidence)` — the *interim-posture vocabulary already exists as decision outcomes* |
| Remediation SQL emission | `RemediationEmitter.fs` (`manifest.remediation.sql`; SELECT active, UPDATE/DELETE commented) | The operator-safety contract and per-finding 3-option block shape |
| Static-lookup dataset identity across two envs | `Reconciliation.staticLookupIdentity` (`Reconciliation.fs:378`, `StaticLookupDivergence`: column drifts + missing/extra rows by business key) | The exact primitive the AutoNumber-static-entity check generalizes N-way |
| Two-env row/null integrity diff | `DataIntegrityChecker.fs` (`check data --before --after`) | The per-env evidence-capture loop, incl. `Catalog.stripStaticPopulations` so static tables profile at all |
| Per-env strictness | `Tolerance` (`Tolerance.fs`) + the R4 ladder (`MultiEnvironmentPromotionTests.fs`: `Dev ⊇ QA ⊇ UAT ⊇ PROD = strict`, monotonically tightening) | The lattice the interim posture must respect |
| Config-suggestion emission | `SuggestConfigEmitter.fs` + `suggest-config` | The precedent for "findings → an editable config fragment" (A44: expressible ⇔ reachable) |
| Capability probes across all envs | `survey` (`CapabilitySurvey.fs`) | The operational-plane divergence axis (grants, reachability) |
| Provenance / burndown substrate | episode store, `seal` / `report`, `RunLedger`, `@runId` refs | Where convergence-over-time lives |
| V1 editorial donors | `src/Osm.Pipeline/Profiling/MultiEnvironmentConstraintConsensus.cs` + `MultiEnvironmentProfileReport.cs` | Consensus ratio, safe/unsafe digest, primary-vs-secondary framing, worst-case aggregation vocabulary — donate the *reporting shapes*, not the physical-name keying |

Two hard-won identity disciplines bind everything above: cross-environment schema comparison is
only sound over **OSSYS-read catalogs** (native GUID `SsKey` identity; two `live:` reads
synthesize keys from physical `OSUSR_*` coordinates and diff as unrelated Add+Drop —
`CROSS_ENVIRONMENT_READINESS.md §1`), and the physical-name keying the V1 consensus donor uses
is exactly the espace-unsafe move the F# side structurally fixed. Read every environment via
`ossys:`; borrow V1's *vocabulary*, not its keys.

---

## 2 — The gap, stated precisely

Four things do not exist yet.

**2.1 — The fourth comparison regime.** The ontology names three (`THE_USE_CASE_ONTOLOGY.md`
§5.9): prior-snapshot vs model, live-readback vs model (drift), two authored models. The estate
mode introduces **deployed ↔ deployed across the environment lattice** — N cells compared
mutually *and* against the unification target. `check shape` is the closest prior art but it is
strictly star-shaped (N envs each against one agreed shape) and schema-first; it does not say
*who is the odd one out*, *in which direction the divergence points* (promotion lag vs fork), or
anything cross-environment about the data plane beyond per-env dealbreakers.

**2.2 — Estate consensus.** `ModelFidelity` is single-profile. `Profile.merge` exists but has no
consumer that folds N environments and decides once. The cutover-grade question is not "does dev
pass?" three times — it is "**what is the tightest DDL every environment's evidence licenses**"
(the decision meet over the evidence join), with per-env attribution preserved so the operator
sees *which* environment blocks *which* tightening.

**2.3 — Prescriptive projections.** Today's cross-env surfaces are advisory-read-only by design
(`check shape` §6). Remediation SQL exists but rides the publish path against one source. Nothing
emits (a) cross-environment data-repair SQL, (b) an **interim DDL posture** as an editable
artifact, or (c) the probes that prove a relaxation can be retired. "Recommend DDL fine-tuning
back to the model" has no ontology home at all — the engine refuses, tolerates, and remediates,
but never *recommends*; the nearest substrate is FK-trust, which is read but ungated.

**2.4 — Convergence over time.** Every existing verdict is one-shot. Cutover reconciliation is a
*burndown*: findings must carry stable keys so two runs diff into fixed / new / still-open, and
the readiness ladder can gate on "N consecutive unified runs," the same way the R6 cutover gates
already work.

---

## 3 — The shape of the mode: one findings source, four projections

The Projection Principle, applied to this mode from the start: measure the estate **once** into a
single typed findings source, and derive every consumer surface as a lawful projection of it.
No hand-authored sibling views.

**3.1 — The source.** One run produces one `EstateFindings` value (name illustrative):

- N resolved operands — `(env label, OSSYS catalog → logical shape, Profile, CapabilityReport)`,
  plus the **unification target** (see §6 for what "target" means).
- A findings list, each finding shaped roughly as:
  `{ Key : FindingKey; Plane; Axis; Envs : per-env evidence; Severity; Disposition }`
  where `FindingKey` is stable across runs (SsKey-rooted + axis discriminator — the burndown
  depends on this), `Plane` ∈ {Schema, Data, Identity, Operational}, and `Disposition` is the
  closed outcome (`RepairData`, `RelaxInterim of Relaxation`, `AmendModel`, `Tolerate of
  ToleratedDivergence`, `Refuse`).

**3.2 — The four projections.**

1. **π₁ The report** — TTY Surface + `estate.json` (the `compare.json` / `readiness.json`
   sibling). Count-first, THE_VOICE register, per-env columns, odd-one-out attribution, the
   consensus digest (donating V1's safe/unsafe tables and consensus-ratio vocabulary).
2. **π₂ Remediation SQL** — `estate.remediation.sql`, extending `RemediationEmitter`'s
   per-finding 3-option contract (SELECT active; UPDATE/DELETE commented) with per-environment
   sections. New block classes: sentinel-zero FK repair (UPDATE … SET fk = NULL — see D3a),
   static-row alignment (MERGE by business key), user-FK re-key candidates. Emission should ride
   the typed codec surface (the recon already flags `RemediationEmitter` as a bare-`sprintf`
   emitter — new block classes are the natural moment to route through ScriptDom builders).
3. **π₃ The interim posture** — `estate.overlay.json`: a *config fragment* the operator can merge
   into `projection.json` — tightening exceptions (`KeepNullable` overrides, FK
   `EnableCreation=false` / `AllowNoCheckCreation=true` per reference), tolerance additions,
   sampling pins. A44 makes this the load-bearing choice: if every interim relaxation is
   *expressible as config*, then the relaxed state is *reachable by the ordinary pipeline* and
   nothing about the interim period is hand-edited DDL. This is `suggest-config`'s precedent
   promoted to a first-class output.
4. **π₄ Re-compliance probes** — `estate.probes.sql` (or a section of the report): for every
   relaxation in π₃, the exact probe (a `COUNT_BIG` shape the profiler already owns) and
   threshold that retires it. The Active-deferrals discipline — *every deferral carries its
   re-open trigger* — applied to data: **every relaxation carries its re-tighten trigger,
   executably.**

Coherence law: π₂, π₃, π₄ are projections of the same findings list π₁ counts — a finding with
`Disposition = RelaxInterim` appears in all four or in none. That is the property test.

**3.3 — Verdict vocabulary.** Extend, don't replace, the `Readiness` verdicts. Per environment:
`Ready / Paused / Blocked` as today. Estate-level, three states worth naming:

- **Unified** — every env `Ready` *and* the outstanding relaxation set is empty. (Unification is
  not just "one shape + clean data" — it is *zero interim posture left*. This is what makes
  "full-unification" a checkable terminus rather than a mood.)
- **Converging** — divergences exist, every one carries a disposition (repair emitted, relaxation
  named, or tolerance matched). Nothing unexplained.
- **Forked** — at least one finding with no disposition: contradictory schema shapes
  (both-changed-differently), or a data contradiction with no lawful repair. Blocks the ladder.

**3.4 — Naming.** `reconcile` is the natural English word and the wrong token: it is already
a flow-config field + `--reconcile` flag (the `MatchByColumn` identity re-key), and an amino acid
meaning *identity correspondence across substrates*. Reusing it for estate convergence would
overload the ubiquitous language at its busiest point. `check ready` is also taken (run-ledger
gauge). Candidates, in rough order of fit with the four framing questions (`THE_CLI.md §8`):

- **`check estate`** *(recommended)* — the estate-level "is it right?"; sits beside `check shape`
  (which it subsumes or wraps), keeps the read-only-check grain, and the artifacts (π₁–π₄) are
  outputs of the check the way `check go --sql` already emits SQL. Concept-shaped: the estate as
  one lattice.
- `check convergence` — says the burndown out loud; mild collision with the MERGE
  "convergent-delete" vocabulary (a different plane, arguably tolerable).
- A top-level `align` / `unify` verb — earns its keep only if the mode ever *executes* repairs
  (it should not; execution stays with the existing gated verbs — see §5).

Pillar 8 makes this the operator's call; the recommendation is `check estate`, with π₂–π₄ written
as artifacts on every run and no new execution surface.

**3.5 — CLI sketch.**

```
projection check estate                      # uses the readiness/estate config block
projection check estate --envs dev,qa,uat   # explicit override
projection check estate --against model     # target = authored model (default: see §6)
projection check estate --format json
projection check estate --grep Customer     # scope, same grain as diff --only/--module
```

Config grows the existing `readiness` block rather than a new one — `schema:` (the agreed/target
operand) + `confirm: [envs]` are already right; add optional `estate:` knobs (consensus
threshold, orphan-ratio bands, relaxation expiry policy). Exit codes: 0 unified · 5 not-unified
(converging or forked; the `check` fidelity-divergence class) · 6 config/unreadable — identical
to `check shape` today, so the ladder can consume it unchanged.

---

## 4 — The heuristic catalog

The requested brainstorm. Organized by plane; each entry says what it measures, why it is
cutover-load-bearing, and where the evidence already lives. Tags: **[exists]** shipped detector,
**[compose]** existing primitives needing only composition, **[new]** genuinely net-new.

### 4.1 Schema plane (env ↔ env, logical shape)

- **S1 · Presence divergence** [compose] — entity / attribute / index / FK / sequence present in
  some envs, absent in others. `CatalogDiff.between` per pair over `toLogicalShape` catalogs;
  N-way roll-up new. Report with **odd-one-out attribution** (majority vote across cells) and
  **direction classification** (see T1): dev-ahead = promotion lag (expected), UAT-only = drift
  (dangerous — the exact class P-8 calls "deployed-ahead").
- **S2 · Facet divergence** [compose] — type / length / precision / scale / default-value /
  collation mismatches on the same `SsKey` attribute. The `Reshaped` channel already carries
  before/after; the estate view adds "which env holds the narrowest declaration" (the meet
  matters: unification must not silently narrow anyone's data — S2 findings pair with D4/D9
  probes automatically).
- **S3 · Nullability divergence** [compose] — NOT NULL in dev, NULL in UAT. Special-cased out of
  S2 because it couples directly to D1 and to the tightening overlay: the target nullability is
  a *decision*, and the evidence for it is per-env.
- **S4 · Identity/AutoNumber divergence** [new] — `IsIdentity` differing across envs for the same
  attribute; IDENTITY reseed drift (`IDENT_CURRENT` vs `MAX(id)`) per env. Feeds I2/D13.
- **S5 · Delete-rule divergence** [compose] — Protect/Delete/Ignore (→ NO ACTION / CASCADE /
  untracked) differing per env for the same reference. A cascade present in one env only is a
  data-loss asymmetry waiting for cutover day.
- **S6 · Index divergence** [compose] — uniqueness flips, key-column order, missing indexes.
  Uniqueness flips couple to D2/D6 (an index unique in QA only is either a QA-only guarantee or
  a dev data problem — the data plane disambiguates).
- **S7 · FK trust divergence** [compose] — constraint present but `WITH NOCHECK` / untrusted
  (`ForeignKeyReality.IsNoCheck`, FK-trust readback already shipped) in some envs. These pass
  presence diffs while lying about semantics; the unification target wants *trusted* constraints,
  so untrusted ones join the interim posture with a re-trust probe (`ALTER … WITH CHECK CHECK
  CONSTRAINT` costs a scan — forecast it).
- **S8 · Physical residue** [new] — objects physically present but absent from the model:
  dropped-entity leftovers, stale physical names after renames, abandoned `OSUSR_*` tables from
  renamed espaces. Unification would silently orphan them; no-silent-drop demands they be named
  (archive / drop / adopt). Requires a physical sweep beside the OSSYS read — the one place the
  mode *should* look at `INFORMATION_SCHEMA`, precisely to find what OSSYS doesn't know.
- **S9 · Active/inactive divergence** [compose] — `IsActive` facet + `IsPresentButInactive`
  reality: an attribute inactive in dev but active (and populated) in UAT is a modeling fork.
- **S10 · Static-modality divergence** [new] — entity marked Static in one env's model era,
  regular in another; or static in the model while an env's copy carries per-env mutations.
  Gateway to D10/D11.

### 4.2 Data plane (per env, measured against the target DDL)

- **D1 · NULLs in NOT NULL** [exists] — `ModelFidelity.notNullViolations`. Estate addition:
  per-env counts side by side, and the **""≡NULL interplay** (below, D5) folded in before the
  verdict.
- **D2 · Duplicates under UNIQUE/PK** [exists] — plus estate consensus: a column clean in every
  env tightens; dirty anywhere blocks (or relaxes with a dedup plan).
- **D3 · FK orphans** [exists], with two new subclasses worth first-class treatment:
  - **D3a · Sentinel-zero orphans** [new] — OutSystems writes `0` for "no reference" on unset
    FKs. A `fk = 0` row with no target row 0 is an orphan *by convention, not by corruption*;
    the correct repair is `UPDATE … SET fk = NULL` (+ keep the column nullable), never DELETE.
    Splitting these out changes remediation from scary to mechanical, and typically collapses
    the orphan headline number.
  - **D3b · Orphan asymmetry** [new] — orphans in one env only ⇒ data hygiene there; orphans
    everywhere at similar ratios ⇒ a modeling reality (the FK was never real) ⇒ candidate for
    `AmendModel` or a durable NOCHECK posture rather than a repair.
- **D4 · Length/type overflow** [exists] — plus **headroom percentiles** [compose]: not just
  `MaxObservedLength > declared` but P99-vs-declared from the distributions, so π₃ can recommend
  *right-sizing* (widen to observed envelope) vs π₂ recommending truncation.
- **D5 · Empty-string ↔ NULL interplay** [new] — the named tolerance (`EmptyTextNormalizedToNull`,
  NM-18) composed with tightening: a column whose NULL count is 0 but whose `''` count is large
  will fail NOT NULL *after* V2's normalization. Any NOT-NULL decision made on NULL evidence
  alone is unsound for text columns; the estate check must count `''` wherever a NOT-NULL
  tightening is proposed. This is a real phantom-green in the current single-env flow.
- **D6 · Collation-sensitive uniqueness** [new] — values distinct case-/accent-sensitively that
  collide under the target collation (`matchKey`'s case-fold + trailing-trim discipline exists
  for reconcile; apply it as a *probe*). A unique index that will fail on the unified collation
  is a cutover-day surprise; find it now. Same family: trailing-padding collisions
  (`CharAnsiPaddingTolerated`).
- **D7 · Type-narrowing residue** [compose] — quantify per env what each named tolerance absorbs:
  decimal-scale truncation counts, datetime-out-of-range counts, unicode-loss counts. Today a
  tolerance is boolean-matched at canary time; the estate view wants *how much* each env leans on
  each tolerance (a tolerance carrying 4M rows in UAT and 0 elsewhere is a finding).
- **D8 · Date sentinels** [new] — OutSystems null-date conventions (`1900-01-01`, `1753-01-01`)
  per date column per env. Interacts with D1 (a NOT NULL date column full of sentinel dates is
  "clean" and meaningless) and with any semantic consumer downstream.
- **D9 · Numeric domain drift** [compose] — negative values in id-like columns, out-of-enum
  integers in status-like columns (categorical distributions exist; a value in env X absent from
  every other env's categorical set for a low-cardinality column is a data smell).
- **D10 · Static-entity content divergence** [compose→new] — generalize
  `staticLookupIdentity` N-way: per static entity, match rows by business key across all envs;
  report missing/extra rows and column drifts per env against the blessed seed (the model's
  `Static populations`).
- **D11 · AutoNumber static entities** [new] — *the user's named example, and it is really an
  identity finding*: a static entity whose PK is IDENTITY mints different surrogates per env for
  the same logical row, so every FK into it means something different per environment. Detect:
  `Modality.Static ∧ pk.IsIdentity`, then (a) row alignment by business key (D10), (b) the
  **ID-alignment matrix** — for each business key, the per-env surrogate tuple; misaligned
  tuples are the finding. Target state: *no AutoNumber static entities* — seeds ship explicit
  IDs (`SET IDENTITY_INSERT` under the existing write-signoff act) or the entity gains a natural
  key + every inbound FK re-keys through it at transfer (the `SurrogateRemap` machinery exists).
  π₃ recommendation: pin explicit IDs in the seed; π₂: the alignment MERGE per env; π₄: the
  misalignment count probe.
- **D12 · Rowcount profile** [compose] — zero-row tables in one env where others are populated
  (seeding gap); gross volume asymmetry (UAT 10M vs dev 10k) — feeds transfer windowing and,
  crucially, **evidence confidence**: a "clean" verdict from 12 rows must not license an
  estate-wide tightening (`ProbeStatus.SampleSize` already carries the number; the consensus
  should weight or floor it — `NumericDistribution` already enforces a sample-size floor ≥ 5).
- **D13 · Identity headroom** [new] — `MAX(id)` vs type ceiling per table per env (the
  int→bigint forecast, already on the backlog as "concrete storage width"); IDENTITY-current vs
  MAX(id) reseed drift; cross-env range overlap for any table that will ever merge data.
- **D14 · User-FK resolution readiness** [compose] — the P-3 protein's precondition, measured:
  distinct users referenced per env, % resolvable by email against the target directory,
  duplicate-email ambiguity count (`Ambiguous` / `AmbiguousTargetKeys` in reconcile), orphaned
  user references. This is the one data axis with its own machinery (`UserPopulation`,
  `validateUserMap`) that no cross-env report yet aggregates.
- **D15 · Uniqueness-candidate consensus** [compose] — `SuggestUnique` (every value distinct) in
  *every* env ⇒ a natural-key candidate with estate-grade evidence; suggested in dev, refuted in
  UAT ⇒ reject and say why. Today the suggestion is per-run advisory; consensus makes it real.

### 4.3 Identity plane

- **I1 · Espace-invariance audit** [compose] — the A45-candidate law says same-model envs diff to
  zero after `toLogicalShape`; the estate run should *assert* it and name any residue (the
  normalization gaps the two-DB canary caught are the precedent).
- **I2 · Surrogate correspondence for reference rows** [compose] — D11's matrix, extended to any
  low-churn lookup-like table (not just declared-Static): high FK in-degree + low rowcount +
  low drift ⇒ "behaves like reference data" ⇒ candidate for static promotion (`AmendModel`).
- **I3 · Synthesized-key instability** [exists] — `identity.synthesizedRenameUnstable` per env;
  estate view: any kind whose identity is synthesized in *any* env is a rename landmine for the
  whole lattice.

### 4.4 Operational plane

- **O1 · CDC parity** [compose] — `Profile.CdcAwareness` per env: tables CDC-enabled here, not
  there; instance-name drift. Cutover requires knowing exactly which cells carry CDC consumers
  before any wipe-and-load or convergent delete (the `2·|table|` capture-cost forecast exists).
- **O2 · Grant/capability parity** [exists] — `survey` already probes every env in parallel;
  fold its matrix into π₁ so "UAT can't ALTER" sits beside the findings that need ALTER.
- **O3 · Untrusted-constraint census** [compose] — S7's operational half: the re-trust plan and
  its scan cost, per env.
- **O4 · Unmanaged physical features** [new] — triggers, computed columns, rowversion columns
  present per env that the model doesn't express (S8's column-grain sibling). Unification drops
  what nobody names; name them.

### 4.5 Temporal plane

- **T1 · Divergence direction** [new] — for each S-finding, classify *lag* (behind the promotion
  wave; will resolve by ordinary publish) vs *fork* (changed differently on both sides; needs a
  decision) vs *drift* (changed where nothing should change, the P-8 dangerous class). Evidence:
  espace/version metadata from OSSYS and the episode store's dated deltas. The torsor algebra
  already gives the consistency check: with a common ancestor A, `env ⊖ A` per env decomposes
  every pairwise diff; a divergence that no path through the promotion order explains is a fork
  by construction.
- **T2 · Convergence burndown** [new] — findings diffed across runs by `FindingKey`: fixed /
  new / still-open, with age. The ladder consumes it: N consecutive `Unified` runs (the same
  N-green-canaries shape R6 already uses) flips the estate gate.

---

## 5 — The interim posture calculus (the DDL fine-tuning forecast)

The user's second ask — "forecast recommendations to fine-tune the DDL in the interim … and
bring it back into compliance" — deserves to be a first-class object, not prose in a report.

**A relaxation is a typed value:**

```
Relaxation =
  { Scope        : FindingKey                  // what it covers
    Action       : RelaxationAction            // closed DU, see below
    Evidence     : per-env counts that forced it
    ReopenProbe  : Probe                       // the SQL + threshold that retires it
    Expiry       : ExpiryPolicy option }       // e.g. "revisit at T-15" — never silent-forever
```

`RelaxationAction` maps one-to-one onto outcomes the decision rules already speak, which is the
strongest sign the design is with the grain:

| Situation (evidence) | Action (existing vocabulary) | π₂ pairs with | π₄ probe retires it when |
|---|---|---|---|
| FK orphan ratio > band (the "excessive rows" case) | *Untrack*: `ForeignKey.EnableCreation = false` for that reference — or *track-untrusted*: `AllowNoCheckCreation = true` → `EnforceConstraint (ScriptWithNoCheck n)` | orphan repair blocks (D3, D3a split) | `COUNT(orphans) = 0` → re-enable + `WITH CHECK CHECK CONSTRAINT` |
| NULLs beyond budget in a mandatory column | `KeepNullable` override (`OverrideAction.KeepNullable`) instead of `RequireOperatorApproval` limbo | backfill UPDATE plan | `COUNT(NULLs) = 0` (and `''` count, per D5) → tighten |
| Values past declared length | widen to observed envelope (P99 + margin) *or* keep + truncate plan | truncation UPDATE (commented) | `MAX(LEN) ≤ declared` → narrow back if desired |
| Duplicates under a proposed unique index | demote to non-unique now; note filtered-index as the halfway house | dedup ROW_NUMBER plan | duplicate groups = 0 → promote |
| AutoNumber static entity misaligned | pin explicit seed IDs; inbound FKs re-key via remap at transfer | alignment MERGE per env | ID-alignment matrix clean |
| int headroom < margin | schedule int→bigint (the deferred concrete-width slice) | — | headroom recovered (unlikely) or widened |

Three laws should govern the calculus:

1. **Monotone toward prod.** The relaxation set must respect the R4 ladder: anything relaxed in
   UAT is relaxed in QA and dev; prod's set is empty. A relaxation that only makes sense
   downstream-of-dev is a fork, not a posture.
2. **Expressible ⇔ reachable (A44).** Every relaxation is emitted as config (π₃) the ordinary
   pipeline can consume — never as hand-applied DDL. The interim estate is then just *another
   reachable configuration*, publishable and canary-checkable like any other.
3. **Named and expiring.** Every relaxation carries its reopen probe (π₄) and appears in the
   burndown until retired. "Unified" (§3.3) requires the set to be empty — relaxations are debt
   with a meter, not settings.

Worked example, end to end: `Order.CustomerId` carries 3.2M orphans in UAT (14% of rows; dev and
QA are clean — a D3b asymmetry, so this is UAT data hygiene, not a modeling fork). The finding's
disposition: `RelaxInterim`. π₁ shows the asymmetry and the band; π₂ emits the three-option
repair block *scoped to UAT* (with the sentinel-zero subquery split out — suppose 3.1M of the
3.2M are `fk = 0`, so the honest headline is 100k); π₃ emits the overlay:
`tightening.foreignKey: { ref: "Sales.Order:CustomerId", enableCreation: false } // interim,
reopen: orphans=0`; π₄ emits `SELECT COUNT_BIG(*) … WHERE fk IS NOT NULL AND NOT EXISTS …` with
threshold 0. The next `check estate` run shows the orphan count falling; at zero, the relaxation
leaves the overlay, the FK tracks `WITH CHECK`, and the finding closes in the burndown.

---

## 6 — Decision semantics across N environments

Two operations, cleanly separated:

- **Evidence joins.** `Profile.merge` folds per-env profiles into the worst-case union —
  booleans OR, counts MAX, distributions by larger sample. Lawful (commutative, associative,
  identity), already built. Run the existing single-profile engines (`ModelFidelity`, the
  tightening rules) once against the merged profile and the answer is automatically
  estate-safe: **deciding on the join is the unanimity consensus.**
- **Decisions meet, with attribution.** The merged run answers "may we tighten?"; the report
  additionally wants "who says no?" — so also evaluate per env and present the V1-donor
  consensus shape (safe-count / total / ratio / blocking envs). Keep the *apply* threshold at
  unanimity (1.0); lower thresholds are a report-only lens (the V1 doc itself warns what
  sub-unanimity deployment means).

Two honesty rules the consensus must carry:

- **Sample-size honesty.** A clean verdict from an env whose probe saw 12 rows is weak evidence;
  carry `ProbeStatus.SampleSize` into the consensus line ("clean in dev (n=12) — advisory") and
  never let a sampled probe (`SamplingPolicy`) silently stand in for an exact one on a
  tightening decision. The existing probe-outcome vocabulary (`Succeeded` vs `TrustedConstraint`
  vs `FallbackTimeout`) is exactly the right carrier — surface it, per env.
- **What "the target" is.** Three candidate targets, and the mode should be explicit which one a
  run used: (a) the **authored model** (the default — the cutover's declared destination), (b)
  the **agreed-shape env** (`readiness.schema`, today's `check shape` semantics), (c) the
  **meet of the deployed cells** (useful only for the S-plane "who is odd" attribution, never
  for data verdicts). The unification target for data is always (a)-or-(b)'s DDL *after* the
  estate-consensus tightening pass — i.e., the tightest shape the whole estate's evidence
  licenses today, which is exactly what gets tighter as remediations land.

---

## 7 — Convergence over time

The run is re-runnable and each run is an episode: stamp `estate.json` into the run ledger the
way `compare`/`readiness` artifacts already land, key findings stably (§3.1), and let `report`
answer "what changed since the last estate check" for free via the existing since-last-seal
machinery. The gate shape is already house style: the estate rung flips on **N consecutive
Unified runs** (N=10 green canaries is the R6 precedent), and un-flips on any regression. T-30 /
T-15 reviews read the burndown chart, not a fresh investigation.

---

## 8 — A conservative build order

Each slice shippable alone, every one with a consumer on day one:

1. **α — N-way report over existing primitives.** `check estate` = `check shape`'s loop +
   pairwise `CatalogDiff` roll-up + per-env `ModelFidelity`, one `estate.json` + Surface. No new
   detectors. (Mostly a Faces/Pipeline composition; the Readiness types widen.)
2. **β — Evidence union + consensus.** Fold `Profile.merge`; run fidelity + tightening rules on
   the merged profile; add the consensus digest with per-env attribution and sample-size honesty.
3. **γ — The high-value new detectors.** D3a sentinel-zero split, D5 ''-vs-NULL interplay, D10/D11
   static alignment via `staticLookupIdentity` N-way, D12 rowcount profile, S7/O3 trust census.
   (Each is a pure derivation over evidence already captured or one cheap probe away.)
4. **δ — π₂ remediation extension.** Per-env sections; sentinel-zero and static-alignment block
   classes; route new blocks through the typed SQL surface.
5. **ε — π₃/π₄ the posture.** The `Relaxation` type, overlay emission (suggest-config precedent),
   reopen probes, the Unified/Converging/Forked verdict upgrade.
6. **ζ — The burndown.** Stable keys diffed across runs; ladder integration.
7. **η+ — The long tail.** T1 direction classification (episode-dated), D6 collation probes, D13
   headroom, S8/O4 physical-residue sweep, D14 user-FK aggregation.

α+β alone already answer the operator's first paragraph (query all three, notate schema
differences, profile all three, report divergences per DDL); γ–ε deliver the second (fix-SQL and
interim recommendations).

---

## 9 — The traps this mode inherits (paid-for lessons that bind the design)

- **Never diff two `live:` reads** — OSSYS/GUID identity only; `toLogicalShape` before comparing
  (espace-invariance). The C# donor's physical-name keying is the anti-pattern here.
- **Survival rule 8** — a ReadSide-derived catalog profiles to an *empty* evidence cache unless
  static marks are cleared (`Catalog.stripStaticPopulations`); the static-alignment checks
  (D10/D11) depend on doing this deliberately.
- **The G4 silent filter** — the cross-schema FK readback drop (`ReadSide.fs:580` at the audit)
  is exactly the kind of silence an estate census would launder into "no finding"; de-silence it
  on the way in.
- **Real estates are dirty** — entity-less espaces, orphan attributes, duplicate/inactive-shadow
  SS_Keys, stale physical column names after renames: the partial-transfer log's entries 11/12/
  22–24 are the checklist; the estate read must survive all of them.
- **Managed-grant reality** — no `IDENTITY_INSERT`, no ALTER, `VIEW DEFINITION`-less check
  constraints on managed cloud cells: π₂'s per-env sections must respect each env's capability
  survey (a repair that needs a grant the env lacks is a *finding about the env*, not a script).
- **Collation-aware matching** in every business-key join (the T0.1 scar).
- **Advisory means advisory** — the mode reads and writes artifacts; it never executes repairs.
  Execution stays with the existing gated verbs (`revert`, transfer flows, migrate `--go` +
  `PROJECTION_ALLOW_EXECUTE`), so the consent model is untouched.

---

## 10 — Open questions for the operator

1. **Verb + naming** — `check estate` vs a sibling; does it subsume `check shape` or wrap it?
   (Pillar-8 call.)
2. **The target operand default** — authored model vs agreed-shape env (§6)? Today's `check
   shape` says env; the cutover's destination says model.
3. **Consensus threshold** — is unanimity-to-apply / ratio-for-display the right split, or is a
   per-axis threshold (e.g. uniqueness suggestions at ⅔) ever acceptable?
4. **Orphan-ratio bands** — what counts as "excessive" for the untrack forecast (absolute count,
   ratio, or both), and should the band itself live in config (A44 says yes)?
5. **Relaxation expiry** — calendar-based (T-15 review), run-count-based, or probe-only?
6. **Static-entity ID policy** — is "no AutoNumber static entities" a hard target-state law
   (pin explicit IDs everywhere) or per-entity operator choice? This decides whether D11 findings
   default to `AmendModel` or `RelaxInterim`.
7. **Where the burndown lives** — the run ledger (cheap, session-scoped) vs the episode store
   (durable, provenance-grade). The ladder gate suggests the latter.

---

*Companion provenance: `CROSS_ENVIRONMENT_READINESS.md` (the shipped star-shaped gate this
generalizes), `THE_USE_CASE_ONTOLOGY.md` §5.9 (comparison regimes; this proposes the fourth),
`MultiEnvironmentPromotionTests.fs` (the tolerance ladder), `Profile.fs:1391` (`merge`),
`ModelFidelity.fs` / `RemediationEmitter.fs` (the engines π₁/π₂ extend), and V1's
`MultiEnvironmentConstraintConsensus.cs` (editorial donor for the consensus vocabulary). No code
was changed in the writing of this document.*
