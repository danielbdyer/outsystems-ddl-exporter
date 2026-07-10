# THE TRANSFER MANIFEST

*The operator comprehension-and-consent instrument for partial transfer.*
*A design document, to be built. 2026-07-10.*

This document is authoritative for **structure and moves**. `THE_VOICE.md` is authoritative for **every string**. Where a sketch here shows copy, the copy is illustrative and must be re-derived from the twelve rules, the banned list, and the closed verdict set before it ships.

---

## 0. Reading this document

The masterwork is not a new model of the transfer. It is **one lens over three values that already exist and already agree**: the relational segmentation (`TransferImpact.Segment`), the plan's typed write units (`TransferResume.wipeTargets`, minted keys, phase-2 re-points, drops), and the escape evidence (`PeerTransfer.EvidenceVerdict`). The lens folds the certain to a glance, foregrounds the uncertain, makes the one real choice a selection over pre-computed authoritative consequence, and refuses execution until each destructive and creative act is individually blessed against the exact effect it will have.

The document is nine sections and one ledger:

1. The space and the confidence equation.
2. The core model — a transfer as a reviewable, coupling-structured changeset.
3. Pillar I — comprehension by coupling.
4. Pillar II — the decision workbench.
5. Pillar III — per-act consent.
6. Rendering — the interactive surface and its headless-total twin.
7. Fidelity and parity — preserving board = run.
8. Named refusals and Voice copy obligations.
9. The slice plan — shippable, individually-witnessed slices, slice 1 fully specified.

Section 10 is the **stress-resolution ledger**: every high- and medium-severity stress finding, the design decision that resolves it, and the section that carries the resolution. A reviewer verifies coverage there.

---

## 1. The space, and the confidence equation

### 1.1 The space

An operator is asked to transfer a partial subset of a live OutSystems estate — a set of tables and the rows within them — from one environment to another. The subset is not a flat list. It is a graph: foreign keys couple the tables into components, some rows reference rows outside the subset, some target tables already hold data the transfer will overwrite, some surrogate keys will be minted at run time and re-pointed in a second phase. A real transfer is *thousands of interlinked rows across dozens of tables*.

The operator's question is not "what does the tool do." It is **"can I transact this — and know exactly, and only, what will happen."** Today the instrument answers this across two disjoint surfaces at two grains (the go board's count-grain forecast table, the `--impact` artifact's row-grain document tree), with no triage, no collapse of the proven-safe, and one blunt mode-level greenlight standing in for consent. The operator faces a flat wall and signs a blanket cheque.

### 1.2 The confidence equation

> **confidence = comprehension × control × consent × fidelity**

The equation is **multiplicative**: any factor at zero collapses the whole. A perfectly comprehended, perfectly controlled, perfectly consented transfer whose preview does not match the run is worthless (fidelity = 0). A perfectly faithful transfer the operator cannot comprehend is equally worthless (comprehension = 0). The design's job is to hold **all four non-zero**, and to make each a real seam rather than an assertion.

- **Comprehension** — attention is spent only where uncertainty lives. Proven-identical static seeds and self-contained safe subtrees each collapse to one line, the proof one reveal beneath; the scariest coupled component opens first; the count grain and row grain join by shared `SsKey`.
- **Control** — the one place a genuine choice exists (an escaping reference) is a live selection over named archetypes, each carrying a real, authoritative `ForecastDelta` and a proven strength verdict, with coupled edges recomputed together so the control never lies about independence.
- **Consent** — the blunt mode greenlight is decomposed into enumerated, individually-blessed acts, each bound by an effect fingerprint to the exact effect reviewed. The engine refuses **by name** until the plan's act set equals the blessed act set, and a source or sink change re-opens the *affected* act rather than rubber-stamping a different one.
- **Fidelity** — the act set the operator walks *is* the act set the engine executes, via the shared `scope.WriteKinds` / `wipeTargets` Core derivation. The dry run **is** the run under two-traversal parity, so there is nothing unseen and nothing can drift. There is exactly one forecast derivation feeding both the board and the run; the instrument introduces no second one.

The rest of this document is the construction that keeps each factor non-zero, and the proof, act by act, that it does.

---

## 2. The core model — a transfer as a coupling-structured changeset

One held value, a lens over existing ones, never a parallel model of run data.

```
TransferManifest = TransferUnit list          // ranked, triaged; renders to one View.Doc

TransferUnit = {
    Segment        : TransferImpact.Segment    // the weakly-connected FK component — reused as-is
    Triage         : TriageClass               // computed from signals already on the model
    CouplingWeight : int                       // Σ Context deltas + escape/red-verdict presence
    Acts           : Act list                  // the plan's typed write units, scoped to this unit
    Decisions      : DecisionSet               // present only when Triage = OpenEscaping
}

TriageClass =
  | SettledStatic        // all members static-lookup kinds; StaticLookupDivergences empty
  | SettledClosed        // self-contained subtree, act set entirely faithfulnessClass=Safe
  | SettledNoop          // every TableContext has Added = Deleted = Changed = 0
  | OpenEscaping         // a member sources an unresolved PeerTransfer escape (the hard case)
  | OpenDestructive      // contains ANY Wipe / Mint / Rekey / IdentityInsert / DeleteScope / Drop

Act =                                          // the universal consent + execution atom
  | Wipe           of table * rowSample * count
  | Mint           of kind * count             // AssignedBySink surrogate keys
  | Rekey          of column * count           // phase-2 FK re-points
  | Match          of kind * strategy * verdict
  | IdentityInsert of table * count            // onto IDENTITY — collision-prone, its own arm
  | DeleteScope    of table * predicate * count // WHEN NOT MATCHED BY SOURCE … DELETE
  | Drop           of kind * count * reason     // skipped refs ∪ unmatched ∪ ambiguous
  | Resolve        of DecisionSet               // the escaping-reference choice, first-class

Act carries: blastRadius    (before/after Context tally + first-N row sample)
             faithfulness   (Safe | ApprovalRequired | Paused)
             fingerprint    (an ActFingerprint — see §5.2)
             blessing       (bound to that fingerprint)

DecisionSet = Map<targetSsKey, {
    question   : EscapingFk                     // X.col -> T
    answers    : Answer list                     // reconcile | pin | widen | static-lookup
    selected   : Answer
    perAnswer  : Map<Answer, EvidenceVerdict * ForecastDelta>   // ForecastDelta is AUTHORITATIVE (§4)
}>

ForecastDelta = {
    rowsRekeyed   : int
    rowsDropped   : int
    tablesTouched : int
    spawnedKeys   : SsKey list       // decision keys this answer OPENS (Widen cascade — §4.5)
    resolvedKeys  : SsKey list       // decision keys this answer CLOSES
}
```

**The load-bearing invariant.** `Acts` derives from the *same* Core `scope.WriteKinds` / `wipeTargets` derivation the engine executes from (`TransferResume.fs:32-42`; `TransferRun.fs:1384-1396`). The walked set and the run cannot drift. Triage, ranking, and folds are **presentation-only**; the blessed set is keyed by **act fingerprint**, never by screen position. A folded, reordered, or collapsed render can never mis-map or execute an unblessed act.

**One difference from the first synthesis, stated plainly.** The Act DU here has **eight** arms, not seven: `DeleteScope` is admitted as a first-class destructive act (resolves the act-outside-the-manifest finding, §10-H). And the `Answer` list has **four** arms — `reconcile | pin | widen | static-lookup` — the four real `SupportingRelationship` cases the surface can actually resolve. **`Drop` is not an answer.** Accepting an escape as row loss maps to no `SupportingRelationship` case, has no desugar, and the engine refuses it by name at exit 9 (`PeerTransfer.fs:386-393`). It therefore appears in the model only as the `Drop` *act* (row loss the plan already implies) and, at the decision surface, only as a **named refusal** — never as a blessable, gate-clearing proceed (resolves §10-A).

---

## 3. Pillar I — comprehension by coupling

Thousands of interlinked rows *are* weakly-connected FK components. Making the component the primary grain makes the instrument isomorphic to the data. Four moves, all over the already-computed segmentation (`segmentKinds` / `rootsOf` / `documentOf`, `TransferImpact.fs:170-338`), none re-segmenting.

### 3.1 Triage before enumeration

Classify each `Segment` into a `TriageClass` with **conservative predicates that fail toward foregrounding**. The classifier is a total function `Segment -> TriageClass`. The safety hole is exactly one direction — mis-classing an `Open` unit as `Settled` — so classification errs toward showing more.

The force-OPEN predicate (any one forces `Open`):

- any escaping FK sourced by a member (`escapingFks`, `PeerTransfer.fs:319`) → `OpenEscaping`;
- any red `RelationalRole.Verdict` (`TransferImpact.fs:80`) → `OpenEscaping`;
- any non-zero `StaticLookupDivergence` (`report.StaticLookupDivergences`) → `OpenEscaping`;
- **any destructive or creative act** — `Wipe`, `Mint`, `Rekey`, `IdentityInsert`, `DeleteScope`, `Drop` → `OpenDestructive`.

**Wipe is in the force-OPEN set.** This is the correction to the naive triage: a Replace-mode wipe deletes every row in an in-scope table and is the most destructive act in the flow; it must never reach the one-gesture roll-up path. `SettledClosed` is therefore **restricted to units whose act set is entirely `faithfulness = Safe`** — a self-contained reconcile-or-noop subtree with no wipe, no mint, no re-point, no identity insert, no delete-scope, no drop (resolves §10-K). `TriageClass` precedence, highest-severity wins: `OpenEscaping` > `OpenDestructive` > `SettledStatic` ≈ `SettledClosed` ≈ `SettledNoop`. A unit is `Settled*` only when **no** force-OPEN predicate holds.

This classification runs **before any act is put on stage**. It is the cure for per-act overwhelm: the certain collapses to one blessable line before a single act demands a keystroke.

### 3.2 Collapse the certain to one line

- A `SettledStatic` unit folds to one line — *"N reference tables verified identical; no rows move"* — backed one reveal beneath by the zero-divergence `StaticLookupDivergences` proof (reusing `confirmHtml`'s clean predicate, `TransferImpactView.fs:334`).
- A `SettledClosed` subtree folds to one line — *"Order and its 3 owned tables, self-contained, 214 rows move"* — its act set proven entirely `Safe`.
- A `SettledNoop` unit folds to one line — *"no change"* — every `TableContext` at `Added = Deleted = Changed = 0`.

A 40-table transfer with 35 proven-settled tables opens as ~5 `Open` units plus one folded, cap-and-named tail (*"and 33 more settled units"* — searchable, not scrollable). This is the constant-size discipline of `THE_VOICE.md` §12: a 3-unit run and a 3,000-unit run open on the same calm screen.

### 3.3 Foreground the hard case in relational context

`OpenEscaping` units rank to the top and open by default via the `openFirst` / `RenderOptions.OpenPath` seam that is plumbed but hardcoded false (`TransferImpactView.fs:373`; `View.fs:249, 269-272`). The escape renders **inside** its coupled document — `documentOf` already inlines the referenced parent as *"(matched, kept)"* — so an out-of-scope reference is comprehended relationally, not as a detached warning.

### 3.4 Bridge the two grains

The board's count grain (`GoBoard.ForecastLine`) and the impact's row grain (`documentOf` `EntityNode`) never join today. Here a forecast count line links to its unit and its acts by the **shared `SsKey`** both sides already carry; the count line and the row detail become the same object at two `View.Disclosure` depths. Constant-size discipline extends the existing *"+N unchanged not listed"* fold to the changed/added document set (first-N + count) so a large delta never dumps every row.

**The felt arc.** `Open` units resolve one at a time; the manifest shrinks until every unit is `Settled`; the final all-green manifest is one blessable line per unit — the runbook's red → green in miniature. (Caveat: `Widen` can *grow* the manifest before it shrinks it; §4.5 makes that growth first-class rather than a surprise.)

---

## 4. Pillar II — the decision workbench

The `relationships` axis is today the one decision axis with no typed body: `narrateEscapes` and `narrateEvidence` are appended into one flat, interleaved `string list` (`Faces/Transfer.fs:1125`), forcing the operator to string-match an option to its verdict. The fix is a **typed `ItemBody.Decisions`** (sibling to `ItemBody.Scope` / `ItemBody.Forecast`, `GoBoard.fs:101-103`), carried only by `OpenEscaping` units.

### 4.1 The decision table

For each escaping edge, a `View.Table` whose rows are the four archetypes and whose columns are already-computed values, currently lost at flattening:

| Answer | Maps to `SupportingRelationship` | Basis | Sink-unique | Sample-hit | Strength | ForecastDelta |
|---|---|---|---|---|---|---|
| **Reconcile** | `existing-reference` | candidate column | ✓/✗ | `hits/size` | STRONG/PARTIAL/unproven | +214 re-keyed |
| **Pin** | `shared-anchor` (match-then-pin) | anchor | … | … | … | +214 re-keyed, 0 dropped |
| **Widen** | `reference-seed` / `LoadSetAdditions` | seed / add to `tables` | … | … | … | +18 enter scope, opens 2 edges |
| **Static-lookup** | `static-lookup` | candidate column | ✓/✗ | `hits/size` | held to zero divergence | 0 rows move |

Every one of these four maps to a real `SupportingRelationship` DU case with a pure, re-runnable desugar (`SupportingScope.fs:194-260`). There is a resolve, a desugar, and a forecast for each. **No row is a placeholder.** The `Sample-hit` and `Strength` columns are the probe's TOP-200 heuristic (`probeReconcileEvidence`, `PeerTransfer.fs:472-534`); the `ForecastDelta` column is an **exact count** from a full match pass (§4.3) — the two are distinct signals in distinct columns and never share a number format (resolves §10-M).

### 4.2 The toggle is a pure selection over pre-computed authoritative deltas

The naive design made the toggle re-run `resolve → dryRun` inside the redraw loop and stamped the result PROVISIONAL. That is unsound three ways: `dryRun` is `Task`-returning IO reading live sink counts (so it cannot live in a pure, total `ConsoleKey -> Model -> Model` reducer, §10-C); it is a *second* forecast derivation distinct from the authoritative pass (violating two-traversal single-source parity, §10-D); and a "provisional" delta computed from a TOP-200 sample cannot yield an exact `+214` (§10-M). All three collapse under one decision:

> **Every archetype's `ForecastDelta` is computed once, authoritatively, at board build, over an in-memory `EvidenceCache`. The toggle is a pure lookup that selects a pre-computed delta. No IO enters the reducer, and there is no second forecast derivation.**

The `EvidenceCache` is the substrate (built in Slice 2, §9). At board build the instrument reads the **row substrate once** — source rows plus the sink match-columns for each escaping edge's candidate — into an in-memory evidence set, and computes each archetype's match/rekey/drop counts **purely** against it using the same `reconcileKindWith` logic the engine uses (`Reconciliation.fs:165`). This is the house's discover-once / derive-pure pattern. Consequences:

- **Reducer purity restored.** `step : ConsoleKey -> Model -> Model` stays total and pure — a toggle mutates `DecisionSet.selected`, and the `View` re-renders by selecting the already-computed `perAnswer` delta (resolves §10-C).
- **Instant, honestly.** The toggle is a map lookup, not a dry run, so it is genuinely instant — and instant because the *rows* are cached, not because the *schema* is cached. Caching contracts (schema) while re-reading rows on every keystroke would have been a category error: the schema read is negligible against reading thousands of interlinked rows (resolves §10-L).
- **Exact, not sampled.** Because the cache holds the full candidate rowset, the delta is an exact count from a full match pass — never a sample extrapolation dressed as a count (resolves §10-M, §10-P).
- **All four rows are real.** All archetype deltas for an edge are computed at board build, so the side-by-side compares four real numbers at equal fidelity, not one real number against three placeholders (resolves §10-L side-by-side).

### 4.3 Coupled recompute unit = the segment

The recompute unit is the **weakly-connected FK component** (`segmentKinds`), not the single edge. Within a component, selecting `Pin` on edge X changes edge Y's matched/dropped counts (shared-anchor, cycles; `MissingPinnedOwners` is a live check inside `reconcileKindWith`). So:

- On any in-component toggle, the **entire component's** deltas recompute together from the `EvidenceCache` — pure, fast, IO-free (resolves §10-Q the coupled-edges tension).
- Across independent components, "hold others fixed" is honest, because those edges genuinely do not share match state.
- The instrument **never renders a per-edge delta computed under a now-superseded sibling selection** — a component's `perAnswer` map is recomputed as a unit or not at all.

The copy scopes "in isolation" to genuinely independent edges; coupled escapes always recompute and render together.

### 4.4 The ForecastDelta is authoritative and single-source

There is no "provisional" tier. Because the `EvidenceCache` is read from the *same connections* the authoritative `dryRun` uses, and matches are computed by the *same* `reconcileKindWith` logic, the pre-computed deltas and the authoritative board forecast are one derivation, not two (resolves §10-D). A blessing (§5) binds to a fingerprint computed in this same pass. The board pass **is** the run under two-traversal parity; the workbench does not introduce an advisory shadow of it.

Schema-drift honesty: the `EvidenceCache` and the authoritative pass are both taken at board build from the same connections and the same contracts. If schema or data drift mid-session, the *next* board build recomputes everything and re-opens any act whose fingerprint changed (§5.4). The instrument never shows current rows against a stale schema (resolves §10-R).

### 4.5 Widen is a decision-set fixpoint, not a value toggle

`escapingFks` is computed over `loadSet` (`PeerTransfer.fs:319`). The `Widen` answer adds the target to `tables`, growing `loadSet`; `TransferSubset.escapingEdges` then evaluates the newly in-scope kind's own outbound FKs and can introduce **new** escaping edges. So `Widen` is not a value change on one key — it is a **structural mutation of the decision-set keyset that can grow the manifest** (resolves §10-N).

The design models the decision set as a **fixpoint**, not a flat map. Selecting `Widen` recomputes `(loadSet, escapes)` and surfaces the keyset diff as first-class: `ForecastDelta.spawnedKeys` and `resolvedKeys`. The decision table shows *"resolves this edge, opens these N new edges"* before `Widen` is blessable. A `Widen` whose cascade is itself unresolved is **not** all-green; the manifest is all-green only at the fixpoint where every spawned edge is also resolved. This makes the felt arc honest: the manifest may grow under a `Widen` before it shrinks, and the growth is shown, never hidden.

### 4.6 The toggle mechanics

A new Navigator `step` arm cycles `DecisionSet.selected` for the unit under the cursor. The `View` re-renders by selecting the chosen answer's pre-computed `ForecastDelta` and rendering its before → after as two `ForecastLine` tables or a `View.Lane`. The DROP row is absent from the answer set; the option to "accept as loss" is rendered only as the named refusal (§8), with the paste-able config token one reveal beneath the Voice statement.

---

## 5. Pillar III — per-act consent

Today `WriteSignoff.WriteApproval` blesses a whole *mode* optionally narrowed to a table set (`WriteSignoff.fs:47-52`): `{"mode":"replace"}` blanket-authorizes every wipe, and staleness is caught only when the table *set* widens (`ScopeMismatch`), never when the *row population* inside a covered table changes. The same signoff silently re-blesses a different wipe. That is the blanket-consent hole. The refinement is additive in spirit but honest about where it is a rewrite.

### 5.1 The gate is act-set equality, computed over all eight arms

The naive design folded per-act verdicts into the existing six-way, first-`Some` refusal match (`TransferRun.fs:1435-1442`) via the `signoffRefusal` / `identityInsertRefusal` slots. That match has write-consent slots for only **two** of the eight act arms (Wipe, IdentityInsert). Mint, Rekey, Match, DeleteScope, and Drop have no slot — so "the engine refuses unless plan-act-set == blessed-act-set" would be enforceable only over the wipe/identity subset, and a transfer whose only creative acts are re-points and minted keys would run vacuously un-refused (resolves §10-F).

So this is scoped **honestly as a rewrite**, not a fold:

- Replace the two mode-specific slots' consent role with **one aggregate verdict**: derive the plan's full act set (all eight arms, each deriving its act set from the plan the way `wipedTables` and `identityInsertTables` already do, `TransferRun.fs:760-770`), compare to the blessed act set, and refuse when they differ.
- The refusal reports the **full unblessed-act set**, not the first fault. The engine surfaces the same set the board enumerates, so board and engine cannot diverge on what is unblessed (resolves §10-G the divergence risk).
- `WriteMode`'s closed-DU discipline (`WriteSignoff.fs:30`) extends to `Act`: a new act arm is a compiler event that forces its enforcement.

`WriteApproval` extends from `(mode × table)` to `(mode × ActFingerprint)`. The `--go` + `PROJECTION_ALLOW_EXECUTE=1` pair stays as the coarse **outer envelope**; the act-set-equality check is the **inner** gate.

### 5.2 The fingerprint tells the truth about each act's effect

A single fingerprint scheme (first/last PK + raw count, the streaming-journal chunk fingerprint, `TransferRun.fs:1909-1921`) is sound only for delete-by-identity. It is unsound for creative and content-dependent acts three ways: minted rows and re-pointed keys **do not exist before execution**, so there is no PK to bind at consent time (§10-B); reconcile matches by **business key**, so a source edit that changes which sink row a source row matches leaves PK-range and count invariant (§10-I); and sink-side changes (a new duplicate match key) alter the effect with no source-side change at all (§10-O). So `ActFingerprint` is **two schemes by act class**:

- **Population fingerprint** — `(first PK, last PK, raw count)` — for acts whose population is readable **before** the write: `Wipe` (existing sink rows to delete), `IdentityInsert` (source PKs), `DeleteScope` (the sink rows the scope predicate deletes, previewed by the `WHEN NOT MATCHED BY SOURCE` predicate). This is the streaming-journal precedent, reused as-is.
- **Effect fingerprint** — a hash of the content that **determines the act's effect**, computed pre-write from the `EvidenceCache` — for `Match`, `Rekey`, `Mint`, and `Resolve`:
  - the matched **business-key column values** and the **resolved target identities** (so a source edit changing row #57's `BizKey` from `A` to `B`, which re-points it onto a different sink identity with PK-range and count unchanged, changes the fingerprint and re-opens the act — resolves §10-I);
  - the **sink-side uniqueness evidence** (`COUNT_BIG` vs `COUNT_BIG(DISTINCT)`, already computed in `probeReconcileEvidence`, `PeerTransfer.fs:439-443`), so a sink change introducing a new duplicate match key breaks the sink-unique premise and re-opens the `Match` act (resolves §10-O);
  - for `Mint` / `Rekey`, the **pre-write source-selection signature + planned count + resolved strategy identity** — never a post-mint PK — so a creative act binds to something computable before the rows exist, and the board (a zero-write dry run) and the engine compute the *same* identity from the *same* pre-write derivation (resolves §10-B);
  - for `Resolve`, the **desugared `ReconcileAdditions` / `LoadSetAdditions` identity plus the resolved strategy/column**, and every downstream act (`Rekey` / `Match` produced by that resolution) folds the resolution's strategy/column into its own effect fingerprint. So re-toggling reconcile-on-column-A to reconcile-on-column-B — same source population, same PK range, same count — changes the downstream `Rekey` fingerprint and invalidates its blessing, even though the population is identical (resolves §10-J, §10-R the Resolve-cannot-fingerprint gap).

`impactOf` is **verified** against the act's canonical impact — verify act identity and fingerprint, surface canonical prose — not string-diffed against free operator text (`WriteSignoff.fs:86-99` today only echoes).

### 5.3 Consent evaluates on every Execute, not only WipeAndLoad

`signoffRefusal` today is gated on `mode = Execute && Emission = WipeAndLoad` (`TransferRun.fs:1385`), so on a convergent-MERGE (Incremental) transfer it returns `None` unconditionally — yet phase-2 re-points and minted keys still occur there. The per-act consent aggregate is **decoupled from the WipeAndLoad guard**: `Wipe`-specific signoff may stay WipeAndLoad-gated (a wipe only happens then), but `Rekey` / `Mint` / `Match` / `DeleteScope` / `Drop` consent evaluates on **every** `Execute`, mirroring `identityInsertRefusal` which already fires on any `Execute` (`TransferRun.fs:1421-1434`). No creative act on any emission is unblessed-but-unrefused (resolves §10-G the emission gate).

### 5.4 Grain rides triage class; staleness re-opens only the affected act

- A `SettledClosed` unit blesses with **one roll-up gesture** — its subtree is proven self-contained and its act set proven entirely `Safe`, so one signature atomically covers its act set.
- An `OpenDestructive` / `OpenEscaping` unit enumerates acts under the **strict one-lever discipline**: the most-severe unblessed act is on stage; the rest are held beneath; `d` = declare-this, `a` = declare-all-remaining-safe. `IdentityInsert` onto IDENTITY is its own collision-prone arm and is **never** swept into declare-all-safe.
- **Declare-all and the roll-up capture the exact enumerated fingerprint SET at gesture time and persist those fingerprints** — never a stored "all currently-safe" predicate. A re-run producing any act whose fingerprint is not in the captured set **re-opens** rather than being silently re-covered by the old bulk gesture (resolves §10-T the declare-all-snapshot gap).
- **Staleness is scoped per act.** A source or sink change that alters an act's effect invalidates *that act's* fingerprint and re-opens *that gate* only. A benign change never re-walls the whole manifest — only affected acts re-open (resolves the per-act-staleness requirement). Positive outcome: when the unblessed list empties, the gate elides silently.

### 5.5 Durable, mirrored, single-pass-bound

Blessings persist via `RelaxationStore.setFlowSignoff`'s surgical `projection.json` write (`RelaxationStore.fs:118-148`), now emitting per-act acknowledgements with fingerprints. The **board mirror** (`Faces/Transfer.fs:1792-1829`) re-derives the same aggregate per-act verdict so RED shows *before* `--go`.

**Blessings bind only to authoritative-pass fingerprints.** Because the workbench deltas and the fingerprints are computed in the same single authoritative pass (§4.4), there is no provisional/authoritative split to reconcile: the bless-as-you-go arc within one board session binds to real fingerprints, and the **re-open contract** is explicit — a subsequent board build (a fresh authoritative pass) recomputes fingerprints, and any act whose fingerprint changed re-opens, never silently re-covered (resolves §10-S the provisional-vs-authoritative binding gap).

---

## 6. Rendering — one lens, two velocities, a free headless-total twin

**One lens on the existing `View` ADT — never a parallel model.** The `TransferManifest` is assembled into a `View.Doc`; because it is a `View` value, the pretty / plain / `toJson` lenses fall out for free and *cannot* drift (`toJson` never caps; depth/width caps are pretty-only, `View.fs:289-294`).

Node types reused, by name:

- **`View.Doc`** — the manifest, a block sequence of units.
- **`View.Disclosure`** — each unit; a `Settled*` unit is one calm headline, `--depth` or `→` reveals its row-grain documents one level at a time. The cursor path *is* `RenderOptions.OpenPath`.
- **`View.Table`** — the decision table (§4.1) and the two `ForecastLine` before/after tables.
- **`View.Lane`** — a single act's before → after re-key, depth-gated.
- **`View.Hero`** — the lead verdict line for the manifest (closed verdict set, §8).
- **`View.Panel`** / **`PanelRow`** (`Labeled` / `Gauge` / `Next`) — the per-unit verdict panel and the readiness ladder; the closed `PanelRow` keeps the machine twin from dropping a row the pretty lens shows.
- **`View.Note`** and **`View.Action`** — the substantiation reveal and the named next-move imperative.
- **`View.Rule`** — the titled divider between triage tiers.
- **`Status`** (`Ok | Warn | Bad | Pending | Neutral`) — drives glyph + color in one `presentationOf` match, so every color rides a glyph and survives `NO_COLOR`.

`GoBoard.ItemBody` gains **`Decisions`** as a third arm beside `Scope` and `Forecast` (`GoBoard.fs:101-103`), carrying the typed per-edge decision table — replacing the flat prose concatenation.

**Interactive delivery is the one reachable pattern** — the Navigator `Model → View → Console.Clear → ReadKey` redraw loop (`Navigator.fs:152-249`), never a Spectre `Live` region (a live region plus a blocking `ReadKey` is the terminal-exclusivity hazard the house forbids):

- `Model = { Manifest; Cursor; DecisionSet; Blessings }`; a **total** `step : ConsoleKey -> Model -> Model` reducer; an impure `Clear + writeWith + ReadKey` loop.
- Every gesture is a pure `Model -> View` recompute. New `step` arms: navigate-unit, expand/collapse-unit, cycle-answer (select pre-computed delta, §4.2), bless-act, bless-all-safe.
- **Live-animation-while-awaiting-a-key is deliberately NOT built.** This is a redraw TUI. Because the toggle is a pure lookup over the `EvidenceCache` (§4.2), it is genuinely instant; there is no stalled-animation case to paper over.

**Headless-total is structural, not bolted on.** `Navigator.present` (`Navigator.fs:407-416`) routes a redirected stdout, `--json`, or `--query` to a one-shot `renderAnswer` over the *same document*: settled units folded, open units rendered with their decision tables and consent lines as a declarative artifact. Blessings and selections in a non-TTY come from `projection.json` (already-expressible config). Every choice degrades through `Intervene.chooseOn` to a **named `Decision.Degraded` fallback** — the paste-able config edit — so CI never blocks on stdin and a pipe never silently downgrades.

**The headless DROP rule, disambiguated.** The rule is **absence-of-decision refuses**, not "DROP refuses" (resolves §10-B headless / A44 expressible ⇔ reachable):

- An escaping edge with **no decision** in `projection.json` degrades to the named refusal `transfer.peer.subsetFkEscapes` (exit 9). Correct: an unresolved escape blocks.
- A `reconcile` / `pin` / `widen` / `static-lookup` decision **authored in `projection.json`** proceeds headless exactly as the TTY bless would — the configured decision is honored.
- "Accept as drop" is **not an expressible proceed** on either surface, because the engine has no capability to genuinely NULL/drop an escaping FK (`PeerTransfer.fs:377-393`). It is a named refusal in both the TTY and the pipe. There is no expressible-but-unreachable path: what cannot be reached is also not expressible as a proceed.

---

## 7. Fidelity and parity — preserving board = run

Two-traversal parity means **one forecast derivation feeds both the board and the run** — the dry run *is* the run. Every design decision above is chosen to keep that single source intact:

1. **Acts derive from `scope.WriteKinds` / `wipeTargets`** (`TransferRun.fs:1384-1396`; `TransferResume.fs:32-42`), the same Core derivation the engine executes from. The walked act set == the executed act set, by construction.
2. **No second forecast derivation.** The workbench deltas are pre-computed at board build from the `EvidenceCache`, read from the same connections and matched by the same `reconcileKindWith` the authoritative `dryRun` uses (§4.4). There is no "provisional" shadow forecast to diverge from the committed one. The PROVISIONAL tier of the first synthesis is **removed**, not labelled.
3. **Fingerprints are computed in the authoritative pass** and bind both the bless gesture and the enforcement gate, so what a human blesses and what the engine checks are the same artifact (§5.5).
4. **The board mirror re-derives the same aggregate per-act verdict** (`Faces/Transfer.fs:1792-1829`) over the same `scope.WriteKinds`, so RED shows pre-`--go` and the board and engine cannot drift on what is unblessed.
5. **The full unblessed-act set is reported identically** by board and engine (§5.1), not first-fault on one side and full-set on the other.
6. **Schema/data drift is a hard re-open**, not a silent skew: the next board build recomputes deltas and fingerprints from fresh contracts and re-opens any changed act (§4.4, §5.5).

The operator transacts because they have **seen** each coupled unit in relational context, **chosen** each escaping reference over authoritative consequence, and **personally blessed** each act — and the machine has proven, by act identity and effect fingerprint, that it will perform exactly, and only, what was blessed.

---

## 8. Named refusals and Voice copy obligations

### 8.1 Named refusals (the closed set the gate composes)

Every refusal is by name, with the exit code / gate label in the substantiation, never on the statement line:

- **`transfer.peer.subsetFkEscapes`** (exit 9) — an escaping FK with no resolution, or an "accept-as-drop" that the engine cannot honor. The absence-of-decision refusal (§6).
- **`transfer.writeSignoff.actUnblessed`** (new) — the plan's act set ≠ the blessed act set; the refusal carries the **full** unblessed-act set (§5.1).
- **`transfer.writeSignoff.ungreenlit`** — the existing mode-level signoff refusal, retained for the outer envelope.
- **`transfer.staticLookup.diverged`** — a static-lookup unit whose held-identical premise broke.
- **`transfer.supportingScope.inboundOrphan`** — a wipe that would orphan an out-of-subset sink dependent (already witnessed).
- **`identityInsert`** refusal — unblessed identity-insert onto IDENTITY.
- **`Decision.Degraded`** named fallback — the non-TTY degrade for every choice, carrying the fallback code so a pipe emits a named note, never a silent downgrade.

### 8.2 Voice copy obligations

Every string is derived from `THE_VOICE.md` — the twelve rules, the banned list, the closed verdict set — not lifted from `THE_INSTRUMENT.md`'s retired figurative sketches. Concretely:

- **Statement-on-top, proof-beneath.** Each lead is a complete sentence readable with no prior context; notation, exit codes, gate labels, and the paste-able `reconcile 'M.T:col'` token live one reveal beneath, in the substantiation.
- **The closed verdict set only** (`THE_VOICE.md` §3): the consent lead is *"Paused. Approval required before removal."* (`▲`); a clean settled unit is *"Safe to apply: fully reversible, with no data loss."* (`✓`) or *"Verified. The database is provably unchanged."* (`✓`); one item to review is *"Ready. One item requires review."* (`▲`); a refusal is *"Stopped before any change was applied. The cause is shown below."* (`✕`). No fourth tone is invented.
- **The Gate law** (`THE_VOICE.md` §5): state the consequence as meaning, name the one lever, hand the bare imperative, wait. One lever on stage even when several acts are unblessed — the most honest single lever, the rest held beneath. The positive outcome is the act *empty*: the gate elides silently.
- **The twelve rules** are hard gates — no pronouns (never "your ok"), the true verb (`Drops · Deletes · Narrows · Rewrites`, never "cleans up" or "destroys"), stative agentless voice, concrete definite subjects, no antithesis tic, no leaked internals (`OSUSR_*`, raw `SsKey`, exit codes) on the statement line.
- **The banned figurative lexicon is retired**: "dig", "the jewel", "the green hush", "blast radius", "your data". Disclosure is *"Show detail"*; the layer beneath is *"the substantiation"*.
- **Color always rides a glyph** (`●`/`✕`, `✓`/`▲`/`○`), so the signal survives `NO_COLOR` and colorblindness.
- **One register, two velocities.** The newcomer and the hundredth-run operator read the same lead verdict and the same options table; mastery is fluency at the toggle, never a denser expert screen.

---

## 9. The slice plan

Each slice lands whole on the existing seams, degrades headless-total from day one, carries its uncapped JSON twin for free, and ships with **property tests + a docker witness** (`PeerEstateHarness.run2Cell`, the established convention in `PeerWitnessDockerTests.fs`). Ordered by **confidence-leverage** — the cheapest, most independently valuable, lowest-risk factor first.

| Slice | Confidence factor | Net-new risk | Independently shippable value |
|---|---|---|---|
| **1 — Triage / comprehension** | comprehension | none (presentation-only) | a calmer board; zero consent or decision change — **BUILT 2026-07-10** (`TransferTriage.fs`; 9 pure witnesses + `TriageWitnessDockerTests` live) |
| **2 — EvidenceCache + authoritative deltas** | fidelity substrate | read-once/derive-pure | exact per-answer deltas; foundation for 3 and 4 — **BUILT 2026-07-10** (`EvidenceCache.fs`; 8 pure witnesses + the cache≡dry-run `EvidenceCacheDockerTests` live) |
| **3 — Decision workbench** | control | typed `ItemBody.Decisions` + toggle | options + side-by-side, no execution change — **BUILT 2026-07-10** (`ReviewNavigator.fs` + `--review`; 7 pure witnesses + the headless-twin docker witness) |
| **4 — Per-act consent** | consent + fidelity | act-set-equality gate rewrite | the every-act-blessed execution gate |

Slice 1 is the cheapest and safest (pure reads, no execution path touched) and delivers the largest immediate comprehension gain — the highest confidence-leverage. Slice 2 is the substrate that makes both the decision deltas (Slice 3) and the consent fingerprints (Slice 4) honest; it is witnessed on its own so the read-once/derive-pure claim is proven before anything depends on it. Slices 3 and 4 layer control and consent onto that proven substrate.

### 9.1 Slice 1 — the triage / comprehension layer (fully specified)

**Goal.** Replace the alphabetical, uniform-weight, all-collapsed segment render with a triaged, coupling-ranked, proven-safe-folded, foreground-the-hard-case render. No decision surface, no consent change, no execution-path change. Ships as a calmer board and a calmer `--impact` artifact.

**Files.**

- **New: `src/Projection.Pipeline/TransferTriage.fs`** — the triage layer. Pure, no IO.
- **Edit: `src/Projection.Cli/TransferImpactView.fs`** — rank segments by `CouplingWeight`; render `Settled*` units as one folded line; open the top `Open*` unit via `openFirst`; extend the changed-document fold to first-N + count.
- **Edit: `src/Projection.Pipeline/GoBoard.fs`** — no type change in Slice 1; the forecast table optionally clusters lines by unit `SsKey` (kept minimal; the impact artifact carries the comprehension gain in Slice 1).
- **New tests: `tests/Projection.Tests/TransferTriageTests.fs`** — property tests.
- **New witness: `tests/Projection.Tests.Integration/TriageWitnessDockerTests.fs`** — one `run2Cell`.

**Types (`TransferTriage.fs`).**

```fsharp
module Projection.Pipeline.TransferTriage

type TriageClass =
    | SettledStatic
    | SettledClosed
    | SettledNoop
    | OpenEscaping
    | OpenDestructive

type TransferUnit =
    { Segment        : TransferImpact.Segment
      Triage         : TriageClass
      CouplingWeight : int }

/// Total. Fails toward foregrounding: any force-OPEN signal ⇒ an Open* class.
/// Precedence: OpenEscaping > OpenDestructive > Settled*.
val classify :
    escapes:      Set<SsKey> ->          // members sourcing an unresolved escape
    redVerdicts:  Set<SsKey> ->          // members with a red RelationalRole.Verdict
    divergences:  Set<SsKey> ->          // members with a non-zero StaticLookupDivergence
    destructive:  Set<SsKey> ->          // members with any Wipe/Mint/Rekey/IdentityInsert/DeleteScope/Drop
    staticKinds:  Set<SsKey> ->          // members that are static-lookup kinds
    Segment -> TriageClass

/// Σ over Context of (Added+Deleted+Changed), plus a fixed penalty for an
/// escape or a red verdict, so the scariest coupled component ranks first.
val couplingWeight : Segment -> int

/// Stable, total order: CouplingWeight descending, tiebreak by the existing
/// SsKey order so both the pretty and JSON lenses are deterministic.
val rank : TransferUnit list -> TransferUnit list
```

**Gates (what must hold).**

- **Totality.** `classify` is total over every `Segment`; `rank` is a total order.
- **Fail-toward-foregrounding.** If any of `escapes`/`redVerdicts`/`divergences`/`destructive` intersects a segment's members, `classify` returns an `Open*` class — never a `Settled*` class. This is the single safety invariant.
- **Determinism.** `rank` is invariant under input permutation (the SsKey tiebreak makes the sort stable), so pretty and JSON agree.
- **Total-preserving fold.** The folded render's summed counts equal the unfolded counts — no row is hidden from the tally, only from the scroll.
- **Twin parity.** `toJson` at `--depth all` carries every unit and its `Triage` / `CouplingWeight` uncapped; the pretty lens caps depth only.
- **No execution touch.** No change to `TransferRun.fs`, `WriteSignoff.fs`, or any gate; Slice 1 cannot alter what the engine does.

**Tests (`TransferTriageTests.fs`, property style — the totality-loop convention of `NavigatorTests.fs`).**

1. `classify is TOTAL over every generated Segment (never throws)` — generate arbitrary segments; assert `classify` returns for all.
2. `any force-OPEN signal forces an Open* class (fail-toward-foregrounding)` — for every generated segment and every non-empty intersection with `escapes`/`redVerdicts`/`divergences`/`destructive`, assert the class is `OpenEscaping` or `OpenDestructive`, never `Settled*`.
3. `a segment with a Wipe is never SettledClosed` — the specific regression the naive triage allowed.
4. `rank is permutation-invariant (deterministic on both lenses)` — shuffle the unit list; assert `rank` yields an identical order.
5. `the fold preserves the tally` — sum of folded `Context` counts == sum of unfolded.
6. `SettledStatic requires empty divergences` — a static unit with any divergence classifies `OpenEscaping`.
7. `JSON twin carries every unit uncapped at --depth all` — assert count and `Triage`/`CouplingWeight` fields present for every segment.

**Witness (`TriageWitnessDockerTests.fs`, one `run2Cell`).**

```
witness: an impact artifact folds a proven-identical static-lookup unit to one
line and foregrounds an escaping unit — over a live two-cell peer estate.
```

Bootstrap two SsKey-aligned cells. Declare a source with (a) one static-lookup reference table holding the **identical** dataset on both sides, and (b) one payload table with an FK escaping the subset. Build the `--impact` artifact. Assert:

- the static unit classifies `SettledStatic` and renders as a single folded line (its `StaticLookupDivergences` empty);
- the escaping unit classifies `OpenEscaping`, ranks first, and opens by default (`openFirst`);
- the JSON twin carries both units with their `Triage` and `CouplingWeight`;
- the summed row tally over folded + open units equals the unfolded tally (nothing hidden).

**Voice check (Slice 1 copy).** The folded static line uses *"Verified. The database is provably unchanged."* / *"N reference tables verified identical; no rows move."*; the foregrounded escaping unit leads with *"Ready. One item requires review."* (`▲`), the escaping edge named concretely, the exit-9 label one reveal beneath. No banned figurative term; color rides a glyph.

### 9.2 Slice 2 — the EvidenceCache and authoritative per-answer deltas

**Goal.** Read the row substrate once (source rows + sink match-columns per candidate) into an in-memory `EvidenceCache` from the same connections the authoritative `dryRun` uses; compute each escaping edge's four archetype `ForecastDelta`s **exactly** and **purely** against it, per coupled component. No decision UI yet — the deltas land in the JSON twin and a plain table.

**Property tests.** The delta from the cached full match pass equals the delta from a real `dryRun` with that answer selected (fidelity: cache == authoritative, resolves §10-D/§10-M); the cache is read exactly once per board build (read-once); recompute is pure and IO-free (derive-pure); a coupled component's deltas recompute as a unit (§4.3); `Widen`'s `spawnedKeys` are the exact new escaping edges (§4.5).

**Witness.** A two-cell peer estate with a shared-anchor coupling; assert the pre-computed `Pin` delta on edge X equals the authoritative `dryRun` delta with X pinned, and that toggling X recomputes Y's delta (coupled, not independent).

### 9.3 Slice 3 — the decision workbench

**Goal.** `GoBoard.ItemBody.Decisions`; the `View.Table` decision surface; the cycle-answer `step` arm selecting pre-computed deltas (§4.2); the `Widen` fixpoint surfacing `spawnedKeys` (§4.5). No execution change.

**Property tests.** `step` stays total and pure with the new cycle-answer arm (the `NavigatorTests` totality loop, extended); a toggle selects the pre-computed delta and never runs IO; a coupled toggle re-renders the whole component consistently (never a stale sibling delta, §4.3); the four answer rows all carry real deltas at equal fidelity; DROP is absent from the answer set and appears only as the named refusal.

**Witness.** A two-cell estate with two candidate reconcile columns on one escaping edge; assert the decision table renders both with distinct exact deltas, the toggle selects between them, and the headless twin (`--json`) carries the same decision table.

### 9.4 Slice 4 — per-act consent

**Goal.** The eight-arm `Act` DU (incl. `DeleteScope`); per-act `WriteApproval (mode × ActFingerprint)`; the population/effect fingerprint split (§5.2); the act-set-equality aggregate refusal reporting the full unblessed set (§5.1), evaluated on every `Execute` (§5.3); the board mirror; the one-lever bless / declare-all-safe `step` arms with fingerprint-snapshot capture (§5.4).

**Property tests.** Plan-act-set ≠ blessed-act-set refuses `transfer.writeSignoff.actUnblessed` with the full unblessed set (surjection: every arm covered, resolves §10-F); a `Match` blessing re-opens when a source business-key edit changes the matched identity at constant PK-range/count (resolves §10-I); a `Match` blessing re-opens when a sink duplicate breaks sink-uniqueness (resolves §10-O); a `Resolve` re-toggle from column-A to column-B invalidates the downstream `Rekey` blessing at constant population (resolves §10-J); declare-all captures a fingerprint set, and a new act on re-run re-opens rather than being auto-covered (resolves §10-T); consent evaluates on Incremental emission, not only WipeAndLoad (resolves §10-G emission).

**Witness.** Extend `PeerWitnessDockerTests` style: a live transfer whose only creative acts are minted keys + phase-2 re-points on an Incremental emission refuses `transfer.writeSignoff.actUnblessed` when those acts are unblessed, and proceeds only when each is blessed by fingerprint; a subsequent source edit re-opens exactly the affected act and no other.

---

## 10. Stress-resolution ledger

Every high- and medium-severity stress finding, the design decision that resolves it, and the section that carries the resolution. `H` = high, `M` = medium.

| # | Sev | Finding (short) | Resolution | Section |
|---|---|---|---|---|
| A | H | DROP archetype maps to no DU case; engine refuses at exit 9 — board shows what run won't do | DROP is not an answer; the answer set is the four real `SupportingRelationship` cases; "accept as drop" is a named refusal only, never a gate-clearing proceed | §2, §4.1, §6, §8.1 |
| B | H | Fingerprint for Mint/Rekey binds to rows that don't exist pre-write | Effect fingerprint for creative acts binds to pre-write source-selection signature + planned count + resolved strategy identity, computed identically by board and engine | §5.2 |
| C | H | Toggle "pure Model→View" but `dryRun` is IO — async in the reducer | Deltas pre-computed at board build over the `EvidenceCache`; the toggle is a pure lookup; no IO in `step` | §4.2, §6 |
| D | H | Provisional toggle is a second forecast derivation, breaking two-traversal single source | Provisional tier removed; one authoritative derivation over the `EvidenceCache` feeds board, deltas, and fingerprints | §4.2, §4.4, §7 |
| E | H | (placeholder "test/test/test") | No content; a stray placeholder, no resolution required | — |
| F | H | Surjection: only 2 of the act arms have refusal slots; Mint/Rekey unenforced | Rewrite to one act-set-equality aggregate over all eight arms, each deriving its act set from the plan | §5.1, §9.4 |
| G | H | `signoffRefusal` gated on WipeAndLoad; Incremental re-points/mints unblessed | Per-act consent decoupled from the WipeAndLoad guard; rekey/mint/match/drop consent evaluates on every `Execute` | §5.3 |
| H | H | `DeleteScope` absent from the Act DU — a destructive act the manifest can't bless | `DeleteScope` admitted as a first-class eighth Act arm, enumerated, fingerprinted, previewed by its scope predicate | §2, §5.2 |
| I | H | PK+count fingerprint invariant under a business-key change that re-points a match | Effect fingerprint hashes matched business-key values + resolved target identities; a business-key edit re-opens the act | §5.2 |
| J | H | (also #18/#19 medium) Resolve column-A→B re-toggle leaves downstream Rekey blessing valid | Downstream acts fold the resolution's strategy/column into their effect fingerprint; a re-toggle invalidates them | §5.2, §9.4 |
| K | H | Wipe not in force-OPEN predicate; a wipe hides in a one-gesture SettledClosed | Wipe (and every destructive/creative act) forces OPEN; SettledClosed restricted to entirely-Safe act sets; precedence stated | §3.1 |
| L | H | Caching contracts (schema) is the wrong thing; rows re-read each toggle | Cache the row substrate (`EvidenceCache`), not schema; recompute matches purely; the toggle is a lookup | §4.2 |
| M | H | Comparison table not at run fidelity — only the selected row's delta is real / sample can't yield exact count | All four archetype deltas pre-computed exactly from the full cached match pass; sample-hit stays a separate strength column | §4.1, §4.2 |
| N | H | Widen mutates the decision SET and can grow the manifest — modeled as a value toggle | Decision set modeled as a fixpoint; `spawnedKeys`/`resolvedKeys` surface the keyset diff; Widen blessable only with its cascade shown | §4.5 |
| O | M | Sink-side staleness unmodeled — a new sink duplicate silently displaces | Effect fingerprint includes sink-side uniqueness evidence; a broken sink-unique premise re-opens the Match | §5.2 |
| P | M | Cached evidence (sample) can't yield an exact ForecastDelta | Delta is an exact count from the full cached rowset; the TOP-200 sample is only the strength heuristic, in a distinct column | §4.1, §4.2 |
| Q | M | "Hold others fixed" vs "recompute whole set" in tension for coupled edges | Recompute unit is the weakly-connected component; a component recomputes atomically; independent components hold fixed | §4.3 |
| R | M | Provisional mixes fresh rows with stale schema | No provisional tier; cache and authoritative pass share connections/contracts; mid-session drift is a hard re-open at the next board build | §4.4, §5.5 |
| S | M | Consent gate composition — fold vs rewrite; first-fault vs full-set | Scoped honestly as a rewrite to an act-set-equality aggregate; the full unblessed set is reported, board and engine identical | §5.1 |
| T | M | Headless DROP contradicts a config-expressed drop (A44) | Rule restated as absence-of-decision refuses; a configured reconcile/pin/widen/static-lookup proceeds; "accept as drop" is not an expressible proceed on either surface | §6 |
| U | M | Provisional-vs-authoritative binding unspecified | Blessings bind only to authoritative-pass fingerprints; the re-open contract on a fresh board build is explicit | §5.5 |
| V | M | Declare-all snapshot vs predicate — a new safe act auto-blessed | Declare-all and the roll-up capture the exact fingerprint set at gesture time; a new act on re-run re-opens | §5.4 |

Every high- and medium-severity finding is resolved by a concrete design decision in a named section, and each resolution is exercised by a property test or a docker witness in the slice that carries it (§9). The one placeholder finding (E) has no content.

---

## 11. What this instrument is

The operator transacts thousands of interlinked rows because they have **seen** each coupled unit in relational context, **chosen** each escaping reference over authoritative consequence, and **personally blessed** each act the machine will perform — and the machine has proven, by act identity and effect fingerprint, that it will perform exactly, and only, what was blessed.

confidence = comprehension × control × consent × fidelity. None at zero. Each a seam, not an assertion. Built in four witnessed slices on the seams that already exist.
