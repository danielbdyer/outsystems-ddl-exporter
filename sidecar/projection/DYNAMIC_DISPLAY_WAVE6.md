# DYNAMIC_DISPLAY_WAVE6 — walkability across the change-over-time use cases

**Status:** ideation pass (2026-06-05), grounded in three reads — the Wave 6
**ontology + algebra** (the move alphabet + the torsor; `WAVE_6_ONTOLOGY.md`,
`WAVE_6_ALGEBRA.md`), the **use-case ontology** (the 9 proteins + the 10-axis
master matrix + the laws; `THE_USE_CASE_ONTOLOGY.md` + `.obligations.md`), and
the **live change-over-time IR** (`Comparison.fs`, `ChangeManifest.fs`,
`Episode.fs`, `LifecycleStore`). It extends `DYNAMIC_DISPLAY.md` (the locked
Glance / Watch / Explore design) by finding the lenses, drill-lifecycles, and
primitives the *full* use-case space implies.

The load-bearing finding: **the engine is 57/57 HELD** — every protein already
has a real, operator-reachable CLI path, and the surfaces a display binds to
already exist (`ReadSide.cdcCaptureCount`, `LifecycleStore` + the FTC,
`ChangeManifest` with per-channel ‖δ‖ + `ToleranceResidual` + `AppliedTransforms`
+ the CDC series, `GateLabel` + `classify`). **The gap is the walkable display,
not the engine.**

And the display gap is **narrower than it looks — the capability outran its
adoption.** The gap-map pass found: a whole `Comparison → View` projection is
**built, law-tested, and unwired** (`Comparison.fs:36-88` renders both
`CatalogDiff` and `PhysicalSchemaDiff` to walkable `View.Panel`s — **zero callers**
in `Program.fs`); `View.Trail` was added for `explain`, which then renders prose
**by hand** instead; **only `readiness` reaches `View` today**; there is no
`diff` verb and no `inspect` verb at all; and every comparison/provenance verb
(`canary` / `migrate` / `transfer` / `verify-data` / `drift` / `eject` /
`policy-diff`) bypasses the `View` path for `printfn` prose or a flat string. So
the harvest is substantially **adoption** — wire the built surfaces, re-point the
prose verbs — *before* it is green-field. `DYNAMIC_DISPLAY.md` §6 row 5 (the
changeset) is the first concrete leg; §6 below sequences the rest, cheapest-first.

> Discipline carried throughout (the noun/verb rule, `WAVE_6_ALGEBRA.md:357-367`):
> a TUI reads the **concrete carriers** (`CatalogDiff`, `Lifecycle`,
> `ChangeManifest`, the CDC log) — never an abstract `Torsor` / `Move` object,
> which has no code home. Every lens below is a projection of a real type onto the
> `View` ADT.

---

## §1 The deeper frame — three navigation axes, not two

`DYNAMIC_DISPLAY.md` framed two axes (time × depth) at three tempos (Glance /
Watch / Explore). The Wave 6 corpus sharpens this into **three orthogonal
navigation axes**, and the tempo is how fast the operator moves along them:

1. **The inspection grid — emission × episode.** The "concern-movement field"
   (`WAVE_6_ALGEBRA.md:291-314`) literally names the navigator's two spatial
   axes: `∂κ/∂emission` = the **manifest** (across artifacts at one fixed
   episode — SSDT vs seeds vs refactorlog vs profile), `∂κ/∂episode` = the
   **provenance** (the same concern across time), and the mixed partial `∂²` =
   the **change-manifest series** (what each episode-edge touched). The depth
   axis from `DYNAMIC_DISPLAY.md` is the *emission* leg made granular
   (verdict → module → kind → attribute → decision → evidence within one
   episode); the *episode* leg is the timeline.

2. **The timeline is a lattice, not a line.** Runs are points on one line;
   `EpisodicLifecycle` is points across **environments** (Dev→Qa→Uat, the
   cutover rotation) × **versions** (the schema-plane ordinal) — the master
   matrix's axis 6, the "environment-lattice cell" (`THE_USE_CASE_ONTOLOGY.md
   :428-429`). The Glance strip generalizes to a lattice the operator walks on
   both axes (← / → versions, a row per environment).

3. **Comparison regime — the diff basis (a third entry-axis).** The same
   walkable changeset has **three operator entry-modes** depending on what it
   diffs against (matrix axis 5, `THE_USE_CASE_ONTOLOGY.md:426-427`, `:972-983`):
   - **prior-snapshot** → *history* (the sprint-by-sprint timeline walk);
   - **current target-reality** → *reconcile-live* (drift; the intent filter is
     **mandatory** because a read-back is noisy);
   - **two authored models** → *preview* (design-time "what would this migration
     cost," pure displacement, no substrate noise).
   `diff` / `drift` / `migrate --preview` are the **same changeset surface under
   three bases** — not three renderers.

**The frame, in one line:** the display is a navigator that, at a chosen
*tempo*, inspects a point in the *emission × episode* grid, under a chosen
*comparison regime*.

---

## §2 The move-typed changeset — seven channels, each its own lens

`DYNAMIC_DISPLAY.md` proposed a "unified walkable changeset." The ontology
sharpens it: **the changeset is not a flat list — it is seven channels**, and
each move has its *own* natural inspection lens. Today `Comparison.renderCatalogDiff`
(`Comparison.fs:36`) renders only **count-level summaries** (+added −removed
~renamed ≠changed). The harvest is to make each row *walkable into its move's
lens*:

| Move | Realized by | The drill lens the operator wants | Anchor |
|---|---|---|---|
| **Add** | CREATE ∥ INSERT | the newly-minted `SsKey` + the artifact it lands in; the **CDC capture = 1** proof exactly one row entered | `WAVE_6_ONTOLOGY.md:201,405` |
| **Remove** | refuse / DROP ∥ DELETE | the **stop-and-confirm blast radius** — what would be destroyed, the page deallocation, the DELETE scope boundary, the refusal reason. The most inspection-hungry move. | `:202,408,470` |
| **Rename** | refactorlog (`sp_rename`) | the **identity-survival trail** `old → new` (`SsKey` constant) **and the cross-plane proof `‖rename‖_data = 0`** (zero CDC captures = pages preserved). *A rename that lights the CDC counter is a DROP+ADD in disguise* — the headline rename alert. | `:203`, `WAVE_6_ALGEBRA.md:212` |
| **Reshape** | `ALTER` (DacFx) / the `SchemaMigrationEmitter` **preview lens** | the **before→after facet diff** (type/length/nullability), faithfulness-classed: green (metadata-only) / amber (lossy rewrite) / red (refuse). Drill: metadata-only widen vs full-table rewrite? | `:204` |
| **Reidentify** | `Transfer` reconcile | the **surrogate remap table** `(source → sink surrogate)` + the **FK re-point trail** (relationships survived, keyed on business identity not raw surrogate) | `:205,409` |
| **Move** | `Transfer` + data scripts | the **ordering plan / dependency DAG** (what loads before what), the two-phase FK boundary, the running **CDC count vs |true delta|** | `:206` |
| **Accumulate** | `Lifecycle` / refactorlog-vs-prior | the **evolution chain** walkable episode-by-episode + the **CDC capture log as history-and-proof**; `pathLength` vs net = **churn** | `:207`, `ALGEBRA:330` |

Three badges ride every row, each derived from the type (not decoration):

- **Channel filter (clean by construction).** T14 (`WAVE_6_ALGEBRA.md:132-154`)
  proves the moves **partition** the diff — every element is exactly one move,
  none twice, none dropped (P-CH). So *filter-by-move is provably
  non-overlapping*; the changeset groups into orthogonal lanes with no
  double-counting.

- **Reversibility badge (green / amber / red).** The abstract groupoid is fully
  invertible (`Add⁻¹=Remove`, `Rename⁻¹=reverse-Rename`, …, `WAVE_6_ALGEBRA.md:88`),
  but **emission is partial** (`:90-96`). So each row carries a reversibility
  class from `Comparison.Apply` (`Some` = replayable torsor, `None` = lossy
  quotient — `Comparison.fs:25-27`) + the faithfulness ladder: **green** (Add /
  Rename / widen — preview & undo), **amber** (narrowing — warn), **red**
  (Remove / Delete — refuse, show blast radius, demand consent). *Offer undo
  exactly where emission supports it, never where it would silently destroy.*

- **Intent badge (intended / tolerated).** The intent filter `observe(A,B) =
  (B⊖A) ⊕ tolerate(A,B)` (`WAVE_6_ALGEBRA.md:230-240`) means every difference is
  operator-**intended** (will emit) or substrate-**tolerated** (DacFx
  auto-names, empty-string↔NULL, ANSI padding, collation). The toleration bucket
  is **visible and itemized, never a hidden filter** — "silence on neither" is
  the rule a fidelity display exists to honor.

**The asymmetry the changeset must respect:** *schema = preview-then-apply; data
= apply-then-observe* (`WAVE_6_ALGEBRA.md:370-393`). A schema δ is a value you
walk and dry-run; data movement is substrate-fused — its observable form is the
**realized CDC capture series, after the MERGE fires**. So the changeset
(previewable) and the live data gauge (observable) are two distinct surfaces.

---

## §3 The universal drill lifecycle — the walkable "act," not just "inspect"

`DYNAMIC_DISPLAY.md`'s Explore mode is *inspection*. The proteins reveal the
next surface: a **guided lifecycle** every mutating workflow instantiates
(`THE_USE_CASE_ONTOLOGY.md:376-389`):

```
Snapshot → Diff → Gate(refuse-first) → realize moves in dependency order
         → Measure(‖δ‖) → Verify(read-back) → Record(episode)
```

This *is* "preview → approve → execute → verify," and it is native to the chain
— not a UI invention. Two operator-facing inflection points recur in every
protein and become two new display primitives:

- **The Gate — the stop-and-confirm.** Before any write, the operator faces
  refuse / declare-loss / proceed. There is already a `GateLabel` DU +
  `classify : code → (exit, label)` to bind to. A **`View.Gate`** primitive
  renders the **blast radius** (the annihilated `SsKey`s for a Remove; the
  narrowing for a lossy Reshape; the unmapped users for a re-key), the named
  declared-loss decision, and the exit-code the gate will return. This is the
  Remove move's lens (§2) made a *decision surface*.

- **The Measure / Verify checkpoint — the proof badge.** After realization, the
  operator confirms `CDC = |true delta|` (minimality; `=0` on an idempotent
  redeploy) and the read-back is structurally equal. The whole faithfulness
  question reduces to **one equation, T16** (`WAVE_6_ALGEBRA.md:158-188`):
  `run(emit(B⊖A), realize(A)) = realize(B)` modulo residual = (erasure ⊎
  tolerance). A **proof badge** on any timeline edge is exactly this: the two
  legs (model vs substrate) met, residual surfaced.

The drill lifecycle is the *act* extension of Explore: the operator doesn't only
navigate a finished run — they walk a pending change through its gates to a
recorded episode, inspecting at each checkpoint.

---

## §4 The new lenses + primitives (discriminating predicate + ≥2 consumers each)

Each is an enriched primitive supporting the outcome without completing it — the
layer's posture. Listed with its discriminating predicate (the input on which a
naive version diverges) and its consumers.

1. **`View.Changeset` — the move-typed walkable changeset.** *Predicate:* a
   renamed element appears in the **Rename** lane carrying its `old→new` trail
   and `‖rename‖_data` — a naive "flat diff list" shows it as a Remove+Add pair
   in two lanes. *Consumers:* `diff <ref> <ref>`, Explore's between-runs view,
   `drift`, `migrate --preview` (the three comparison regimes, §1).

2. **`View.Gate` — the stop-and-confirm.** *Predicate:* a Remove with no
   declared loss renders **red with its blast radius and a non-zero exit code**;
   a declared Remove renders amber-cleared — a naive "print a warning" shows the
   same text for both. *Consumers:* `migrate`, `eject`, `drift --remediate`,
   the P-3 user-map gate.

3. **The reversibility badge + the intent badge** (Theme tokens + `View`
   row-state). *Predicate:* the badge is derived from `Comparison.Apply` +
   faithfulness, so a `PhysicalSchema` divergence (Apply=None) can **never**
   render as undoable, and a tolerated substrate-noise row renders distinctly
   from an intended one. *Consumers:* `View.Changeset` (every row), the Gate
   (the consent decision), Explore's evidence layer.

4. **`View.Lattice` — the 2-D episode timeline.** *Predicate:* `toJson` carries
   `(environment, version, ‖δ‖, canary)` per cell, so the present-marker and the
   R6 streak land at the right *(env, version)* cell — a naive 1-D strip
   collapses Dev and Uat onto one line. *Consumers:* Glance (the lattice
   header), Explore (←/→ versions, ↑/↓ environments), `readiness` per
   environment.

5. **The churn lens.** `ChangeManifest.pathLength` (total moves) vs
   `EpisodicLifecycle.netSchemaDiff |> norm` (net displacement) — their
   difference is churn (`ChangeManifest.fs:92-101`). *Predicate:* an
   add-then-remove across two edges contributes **2 to pathLength, 0 to net** —
   a naive "sum the diffs" double-counts or cancels silently. *Consumers:* the
   timeline trend (a churn sparkline), the eject summary ("47 moves, netted
   12"), `diff` over a range.

6. **The proof badge (T16 per edge).** *Predicate:* a green-with-tolerance edge
   renders green **with its `ToleranceResidual` itemized as a doorway** — a
   naive "green check" hides the equivalence the edge was accepted under.
   *Consumers:* the timeline (each edge), the canary verdict, `migrate`'s
   verify step.

7. **The surrogate-remap table + the user-map review.** *Predicate:* a re-keyed
   row renders as **one `Update` keyed on business identity** showing
   `(source-surrogate → sink-surrogate)` — a naive view shows a delete+insert
   pair. *Consumers:* `transfer` / `migrate --execute` (Reidentify), the P-3
   `validate-user-map` gate (the propose→review→override→accept drill, the
   richest decision surface in the catalog, `THE_USE_CASE_ONTOLOGY.md:194-219`).

8. **The matrix navigator (a meta-lens).** The master matrix is a navigable
   space: 10 pivot axes, 9-field cells, and **pre-built pivot tables** — the
   protein×move incidence grid (● central / ○ conditional / — absent) and the
   gate×failure matrix are directly renderable as heatmaps
   (`THE_USE_CASE_ONTOLOGY.md:789-811`, `:753-771`). *Predicate:* selecting a
   cell shows its nine fields (physical realization · substrate constraint ·
   faithfulness · gate · artifact · measurement · failure-prevented ·
   discriminating-predicate · planes-touched). *Consumers:* an operator
   `capabilities` / `explain-move` surface; a developer's coverage view (which
   cells are HELD vs decaying).

9. **The "null drill" — green silence as a first-class state.** The idempotent
   redeploy (P-5) verifies that **nothing happened** — `isEmpty(between A A)`,
   CDC=0, every substrate difference a named tolerance. *Predicate:* the state
   renders distinctly from "no data" — it asserts *confirmed absence*, with the
   sensitivity check (AC-D3) that silence isn't a dead predicate. *Consumers:*
   the canary verdict, `migrate` of an unchanged model, the Glance "all green."

10. **The CDC norm gauge (Watch, data-side — aspirational).** ‖δ‖ accumulating
    per channel as a MERGE fires. *Honesty caveat (latent vs activated,
    `WAVE_6_ALGEBRA.md:317-353`):* schema-side move-count + the live commuting
    square are **real today**; the **data-side norm (CDC capture series) is
    ⬚ unwitnessed-live** (6.F.3-data). The gauge ships schema-side now; the
    data-side lands when the witness does — *do not render a data gauge the
    engine can't yet prove.*

---

## §5 The protein → display binding (each workflow as a display flow)

Every protein is the universal skeleton (§3) with its own gate, measure, and
drill targets. This is the catalog of *flows* the display serves:

| Protein | The flow (its gate · its measure · its drill target) | Anchor |
|---|---|---|
| **P-1/P-2 load** (Dev/QA → on-prem) | gate: declared-loss · measure: CDC=|δ|, **=0 on redeploy** · drill: episode→channel→object→SQL→CDC | `:158-193` |
| **P-3 UAT re-key** | gate: `validate-user-map` (**halts before any SQL**) · measure: matching-report audit · drill: the user-map table (orphans / by-email / overrides) — propose→review→override→accept | `:194-219` |
| **P-4 SSIS publication** | gate: declare every Remove · measure: `reconstructLatest` reproduces · drill: the rename set + the reshape-preview (the human-readable ALTER lens the SSIS team reads) | `:221-250` |
| **P-5 idempotent redeploy** | gate: intent-filter complete · measure: **CDC=0, zero ALTERs** · drill: the null drill (§4.9) | `:252-272` |
| **P-6 `migrate A B`** | gate: declared-loss + data-compat · measure: CDC=|δ| (not 2×table) · drill: the per-channel plan → execute in order → verify `isSchemaEqual` (the canonical preview→approve→execute→verify) | `:273-301` |
| **P-7 eject / freeze** | gate: **provenance-completeness** (last chance to catch a broken chain) · measure: genesis→terminal FTC · drill: the full-history diff + the churn summary | `:302-333` |
| **P-8 drift** | gate: human-in-the-loop accept/remediate/escalate · measure: drift size per channel · drill: the **model-ahead vs deployed-ahead** classification (the dangerous class) | `:335-354` |
| **P-9 canary** | gate: `isEmpty` else red · measure: CDC-silence · drill: each non-tolerance residual = an unfaithful cell to close | `:356-374` |

---

## §6 The build sequence — cheapest-first, gap-grounded

The gap map (the operator-surface pass) makes the ordering clear: **lead with
adoption (wire what's built), then the new lenses.** The "wire-up" rows are
near-free and immediately upgrade many verbs at once; the "build" rows are the
genuine new primitives. Each row is a thin slice, one per commit, the layer's
rhythm. Ladder anchors are the G-numbers from the fidelity ledger
(`DEBRIEF…:176-198`).

### Wire-ups (the capability is built; adopt it) — do these first

| # | Ships | Today's state (the gap) | Acceptance |
|---|---|---|---|
| **W1** | **the `diff <refA> <refB>` verb** | `Comparison.summary` is `resolveCatalog ×2 → Render → View` — **built and unwired**; no `diff` verb exists | `diff @run-9 @run-10` prints the `Comparison` panel; `--format json` emits `View.toJson`; the three comparison regimes (file/`@run`/`live`) all resolve through `Ref` |
| **W2** | **re-point the prose comparison verbs at `View`** | `canary` / `drift` call `PhysicalSchema.renderDiff` (a **string**); `migrate --preview` prints per-channel prose — yet `Comparison.renderPhysicalDiff` / `renderCatalogDiff` (→`View`) sit unused (G1, G3, G4) | `canary` / `drift` / `migrate --preview` render the `Comparison` `View`; piped, they emit clean json; the prose string-renderers retire |
| **W3** | **`explain` → `View.Trail`** | `explain` builds prose **by hand** with `Theme.*` glyphs, though `View.Trail` (`View.fs:41`) was added for exactly this | `explain` builds a `View`; output preserved; `--format json` falls out — and the navigator (B3) can now reuse the same `View` |

### Builds (the genuine new primitives) — `DYNAMIC_DISPLAY.md` §6 rhythm

| # | Ships | On | Acceptance |
|---|---|---|---|
| **B1** | `View.Timeline` + `Theme.timeline` (the strip) | `RunHistory` / `EpisodicLifecycle` | the Glance strip renders; `toJson` carries verdicts+present+gate (`DYNAMIC_DISPLAY.md` §6 row 1) |
| **B2** | the **move-typed `View.Changeset`** + reversibility/intent badges | W1/W2 + `CatalogDiff.channelCounts` | the changeset groups into the 7 move-lanes (§2); each row badged reversible (green/amber/red) + intended/tolerated; a Rename shows `old→new` + `‖rename‖_data`, not a Remove+Add pair |
| **B3** | the **`Navigator` + `inspect <runId>`** TUI | View + `RunHistory` + W3's `explain` View | `inspect @run` opens on verdict+rollup; `↑↓` depth, `←→` runs; `--format json` = navigator state (`DYNAMIC_DISPLAY.md` §6 row 4) |
| **B4** | **`View.Gate`** — the stop-and-confirm | `Preflight.GateRefusal` / `GateLabel` (`Preflight.fs:383-481`) | a Remove/refusal renders its **blast radius + exit code** as a walkable `View`, not a single `Console.Error.WriteLine` string (G5, G7, G15–G17) |
| **B5** | the **Watch renderer** (live stage tree + ETA) | `LogSink.addSubscriber` (**shipped**) + `Bench` samples | `full-export --pretty` draws a filling stage tree; channel-1 routing per §15 (`DYNAMIC_DISPLAY.md` §6 rows 2–3) |
| **B6** | `View.Lattice` (2-D timeline) + the **churn lens** + the **proof badge** | `LifecycleStore`, `ChangeManifest.pathLength`/net, T16 residual | the timeline walks env×version; "47 moves, netted 12 → 35 churn"; each edge carries a T16 faithfulness badge with `ToleranceResidual` as a doorway (G10, G11) |
| **B7** | the **matrix-as-`View` (G13)** — "where am I on the ladder" | the generated ladder matrix + `Tolerance` `@ladder` tags | an operator asks the running tool its L2/L3 state per axis and gets a walkable `View` (today: a generated `.md` + CI only — the most trust-relevant artifact has no in-terminal home) |
| **B8** | the **surrogate-remap / user-map review** | `Transfer` / P-3 `validate-user-map` | the propose→review→override→accept drill; the user-map is a walkable table (orphans / by-email / overrides) gating before any SQL |
| **B9** | the **matrix navigator** (meta-lens) | the master matrix pivot tables | the protein×move + gate×failure heatmaps; cell→9-field detail pane |

**A note the build saves money on (G18).** `DiagnosticLattice.subsumes` (rollup
/ antichain of diagnostics) and `LineageTree` (branching provenance) are
zero-consumer, law-tested, and on a **cutover+1 deletion clock** — and their
named triggers are *exactly* the operator-triage (B4) and branching-provenance
(B6/B8) display surfaces. Building the display gives them their consumer:
**these wire-ups retire the deletion clock rather than the code.**

---

## §7 Disciplines this harvest carries

1. **Read the concrete carriers, not an abstract torsor** (`WAVE_6_ALGEBRA.md:357-367`).
   Every lens projects a real type (`CatalogDiff`, `ChangeManifest`,
   `EpisodicLifecycle`, the CDC log) onto `View`. No `Move`/`Delta`/`Torsor`
   object — it has no code home.
2. **Latent vs activated — don't promise what the engine can't prove**
   (`WAVE_6_ALGEBRA.md:317-353`). Schema-side norm + the live square are real;
   the data-side CDC norm gauge is aspirational until 6.F.3-data witnesses it.
3. **The cardinal sin: silent erasure is worse than no claim**
   (`THE_USE_CASE_ONTOLOGY.md:876`). Toleration and refusal are always **visible
   and itemized** — the intent badge and the Gate's blast radius exist to honor
   "silence on neither."
4. **Channels partition; filtering is clean by construction** (T14). Group/filter
   by move without double-counting.
5. **View-navigator, color+glyph, calm-by-default, end-with-next-action** —
   inherited from `DYNAMIC_DISPLAY.md` §7. Every new lens obeys them.

---

## §8 Cross-reference index

| This doc | Ground | Code carrier |
|---|---|---|
| §1 the grid / lattice / regime | concern-movement field; matrix axes 4/5/6 | `LifecycleStore`, `EpisodicLifecycle` |
| §2 move-typed changeset | the move alphabet + T14 + intent filter | `Comparison.fs`, `CatalogDiff.channelCounts` |
| §3 the drill lifecycle | the universal skeleton + the laws | `GateLabel`/`classify`, `ReadSide.cdcCaptureCount` |
| §4 lenses/primitives | moves × proteins × algebra | `ChangeManifest.fs`, `Comparison.Apply` |
| §5 protein flows | the 9 proteins | the shipped verbs (`migrate`/`eject`/`drift`/`canary`/…) |

**Companion:** `DYNAMIC_DISPLAY.md` (the locked Glance/Watch/Explore design);
`REPORTING_HORIZON.md` (the reporting roadmap); `WAVE_6_ONTOLOGY.md` /
`WAVE_6_ALGEBRA.md` / `THE_USE_CASE_ONTOLOGY.md` (the change-over-time corpus).
