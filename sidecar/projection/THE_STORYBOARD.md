# THE STORYBOARD — the surface, scene by scene, across every verb

**The missing middle.** `THE_VOICE.md` is the *register* (how the instrument speaks, under twelve
rules); `THE_VOICE_INTEGRATION.md` is the *build* (how the copy becomes structural). This document is
what sits between them: the **shot list** — the major stages of every run, the streams of information
that enter and exit each frame, the subjects that take the stage, and the positive / negative / edge
outcomes each can land on — storyboarded the way a producer blocks a scene, so that *only then* the
diction can be inventoried against real surfaces.

Grounded in two canonical surfaces, cited throughout:
- **`THE_USE_CASE_ONTOLOGY.md`** — the amino-acid alphabet (§2, the *cast*), the protein catalog (§3,
  the *scene sequences*), the master matrix (§4, the *streams per subject*), the laws (§5, the *counts
  and spectrums*).
- **`THE_VOICE.md`** — the twelve-rule register (§1), the verdict set (§3), the moves (§4), the gates
  (§5), the proofs (§6), the scale discipline (§12), Watch (§13), setup (§14).

Copy here is **illustrative**, per `THE_VOICE.md`'s anchor discipline — read for the stream and the
register, not the exact string. Every line obeys the twelve rules (no pronouns; legible statement,
formal substantiation beneath; verdicts asserted; the true verb; evidential grounding).

---

## 0 — How to read this

A film is **acts** (the arc), **a cast** (who can appear), **streams** (what the camera captures), and
**a call sheet per scene** (who is on stage that day). This storyboard is the same four things:

1. **The spine (§1)** — the nine acts every run moves through. The universal arc.
2. **The six streams (§2)** — the taxonomy of *what kind of information* can flow in any act: counts ·
   spectrums · forecasts · signals · interventions · intentions.
3. **The shot list (§3)** — the heart. Per act: the moment, who enters and exits, the streams in
   frame, the intervention (if the act can stop), and the **positive / negative / edge** outcomes —
   each given equal design care.
4. **The call sheets (§4)** — the nine verbs (proteins P-1…P-9) cast against the acts: which subjects
   take the stage, the felt arc, the one line each leads with.

Then, and only then:

5. **The diction inventory (§5)** — the vocabulary, bound to each stream-type and each outcome
   register, under the twelve rules. Where the voice becomes specific.

Two appendices ground it: **§7** the build-readiness map (each surface live vs latent, from the
current-state debrief) and **§8** the worked proof — **P-6 migrate, frame by frame** at per-string
fidelity.

**The quality claim, stated up front.** Quality is not the polish of the happy path. It is that the
**negative and the edge get the identical care as the positive** — the refusal is as calm and as
concrete as the success; the no-op is a designed line, not an empty screen; the failure names the next
move instead of a stack trace; and the surface is *the same size* whether three things changed or
three thousand (`THE_VOICE.md` §12). The storyboard exists to make that symmetry checkable: every act
below carries all three outcomes, or it is not finished.

---

## 1 — The spine: the nine acts

Every protein in the ontology is the same rhythm —
`Snapshot → Diff → Gate → realize → Measure → Verify → Record` (ontology §3; `THE_VOICE.md` §7) —
bracketed by an establishing shot (setup) and a closing shot (the timeline). Stage names are **what
they do for the operator**, never the engine verb (`THE_VOICE.md` §13).

| Act | Operator-facing name | Engine verb | The moment (felt sense) |
|---|---|---|---|
| **0** | **Arrival** | (config / pre-flight) | *what is set, what is not, whether the target is reachable* — the establishing shot |
| **1** | **Reading** | Snapshot | *what the model contains* (and *what is deployed*) — orientation |
| **2** | **What changed** | Diff | *the difference, big rocks first* — recognition |
| **3** | **The one decision** | Gate | *a stop, to ask* — consent (the intervention act) |
| **4** | **Making it real** | realize (the moves) | *the changes, applied live* — motion (Watch) |
| **5** | **The touch** | Measure | *the exact extent of the change* — minimality, proven |
| **6** | **Verification** | Verify | *the result, confirmed against the model* — fidelity, proven |
| **7** | **Recorded** | Record | *the run, saved* — continuity |
| **8** | **Where it stands** | (timeline + ladder) | *the run takes its place; the distance to cutover* — the arc |

Three things to hold:

- **Acts are elided, never reordered.** A pure read (`diff`) plays acts 0–2 and stops. An idempotent
  redeploy (P-5) plays every act, but acts 3–5 are *near-silent by design* (nothing to consent,
  nothing to touch). Drift (P-8) replaces act 4 with a decision (accept / remediate / escalate). The
  arc is a partial order (ontology §3 cross-protein constraints), and the surface honors it.
- **Act 3 is the only act that stops.** Everything else streams; the gate *waits*. That asymmetry is
  the whole safety story, so it gets its own act even when it is empty.
- **Acts 5–6 are the proofs.** They are where the engine's deepest claim about itself — that it is
  faithful — is stated as a grounded finding, the formal proof one level beneath (`THE_VOICE.md` §6).
  They are the soul of the surface, not a footer.

**The two axes the surface navigates (the concern-movement field).** Beneath the nine acts run two
orthogonal axes the operator moves along — named in `WAVE_6_MORPHOLOGY.md` §2 as the field
`∂κ/∂emission × ∂κ/∂episode`:
- **The emission axis** — *how a change distributes across the estate within one run*, integrated by
  the `ChangeManifest`. Acts 1–6 trace it: this run's breadth, table by table, channel by channel.
- **The episode axis** — *how the estate changes across runs over time*, integrated by the provenance
  / `LifecycleStore` (the FTC `reconstructLatest = genesis ⊕ Σδ`). Acts 7–8 trace it: the history, the
  streak, the distance to cutover.

Depth (statement → substantiation) is the third axis — *into* any one cell. The field is **mostly dark
today** (the morphology's finding): the data exists, the calm rendering does not. Lighting it is the
build (§7).

---

## 2 — The six streams (the taxonomy of what flows)

Everything the surface can show in any act is one of six kinds. Naming them is what lets the diction be
inventoried precisely (§5): each stream-type has its own rendering law.

### 2.1 Counts — scalar magnitudes
*What.* `N tables`, `N rows`, `‖δ‖` (total moves), CDC capture count, `N green checks`, `N unmatched`,
`N runs`, churn-vs-net (`ChangeManifest.pathLength` vs net displacement; ontology §4.4).
*Rendering law (`THE_VOICE.md` §12).* Humane numerals (`2,140`, not `2140`); named units (`4,210
rows`); **big rocks first**, impact-ranked never alphabetical; **cap-and-name** the tail (`and 1,847
more`, searchable, never a scroll). The number scales; the sentence does not.
Counts come in two kinds that must never be conflated (`WAVE_6_ALGEBRA.md` §4–5): **net** — the
distance moved (`net-displacement`) — and **churn** — the work done (`pathLength`). Adding a column
then backfilling it is net 1, churn 2; a busy-but-stationary sprint (add then remove) is net 0,
churn 2. The `ChangeManifest` carries both; **Accumulate** (§4) surfaces churn beside net so honest
effort reads, not just net change. The norm `‖δ‖` is additive across channels (`‖δ‖ = Σ_c ‖π_c δ‖`)
and, on the data plane, *is* the CDC capture count — so Act 5's count is the physical norm, not an
estimate.

### 2.2 Spectrums — a graded position on a named scale
*What.* Faithfulness *class* (`faithful` / `lossy-with-warning` / `refuse-unless-declared`; ontology
§4.1). Faithfulness *ladder* (`L1 → L2 → L3`; ontology §5.3) — rendered as **readiness** (`7 of 10
green`). The cutover streak. A drift's severity. A reshape's instant-vs-rewrite band.
*Rendering law (`THE_VOICE.md` §6, §8).* **Never the L1/L2/L3 notation on the statement.** A spectrum
is shown as a *distance* (`3 green checks remain`) and as **a single lever** (`the one outstanding
item: 3 accounts to map`), never a list of ten. The notation (`schema ✓✓✓ · identity ✓✓▲`) lives in
the substantiation.

### 2.3 Forecasts — a predictive claim made *before* the act runs
*What.* The estimate (`~8s remaining`). The pre-flight reads: `4 rows currently exceed that length`
(data-compat, ontology §4.6); `the change is instant` vs `the existing data has been validated` (widen
vs narrow, ontology §4.2). The validate-user-map count. A would-throw, translated before it throws
(`THE_VOICE.md` §14). The DacFx plan preview (the ALTER-lens; ontology §4.5).
*Rendering law (`THE_VOICE.md` §4, §13, §14).* State the **consequence as meaning, before it
happens**, with the real temporal-causal order (rule 9): `The existing data has been validated against
the new limit, and so the change applies without loss.` The estimate **degrades honestly** — when none
can be computed, none is shown; never a progress bar that misstates.

### 2.4 Signals — a binary or small-state indicator
*What.* The verdict (the closed set of 7; `THE_VOICE.md` §3). Verified / failed. Provably unchanged
(idempotent). Drift present / absent. Connection reachable / not. `The database matches the model`.
*Rendering law (`THE_VOICE.md` §3).* One of seven verdict lines, one glyph (`✓ ▲ ✕`), level tone — the
most consequential statement made most calmly. Define by what *is* (`no removals`), never by what is
not (`nothing destroyed`). Idempotence stated as a grounded finding (`The database is provably
unchanged.`), never as an antithesis.

### 2.5 Interventions — the act where the instrument stops and names a lever
*What.* The gates (`THE_VOICE.md` §5; ontology §4.6): declared-loss · validate-user-map ·
declare-every-drop · intent-filter · data-compat · provenance-completeness · drift · round-trip verification — plus
the pre-flight gates at arrival (connection · permission · CDC-tracking).
*Rendering law (`THE_VOICE.md` §5).* State the consequence **as meaning**; name the **one** lever;
hand over a **plain active imperative** (Approve · Map · Confirm · Trim · Record · Accept · Remediate ·
Halt); then *wait*. Never a wall of error, never `Proceed (Y/n)`, never `Override`.

### 2.6 Intentions — the register the act is producing
*What.* The felt arc (`THE_VOICE.md` §7): reassure · orient · warn · ask-consent · hand-off · prove ·
motivate. The composed shape the operator moves through.
*Rendering law (`THE_VOICE.md` §1).* Calm by default; one register, two velocities; end on the move;
authoritative and humble. The intention is carried by tone and by what is surfaced, not by an explicit
label.

> **The mapping that makes this useful:** every cell in the shot list (§3) is a small set of these six
> streams, in a known register, with a known rendering law. So "design the surface for act N of verb
> V" becomes "render *these* counts, *this* spectrum, *this* forecast, under *this* intention" — and
> the diction (§5) is the lookup from stream-type to words.

---

## 3 — The shot list (per act: the storyboard proper)

Each act is a card with the same fields: **the moment · camera intent · who enters/exits · streams in
frame · the intervention · positive / negative / edge · diction**. The three outcomes are the heart —
each gets a verdict line and a statement.

---

### ACT 0 — Arrival *(setup / pre-flight)*

- **Moment.** Before anything runs: what is configured, what is not, whether the target is reachable.
- **Camera intent.** *Orient, never scold.* A thing not configured is **a choice to make, not a
  failure** (`THE_VOICE.md` §14).
- **Who enters.** No moves yet. The cross-cutting `Gate` (connection · permission · CDC-tracking
  pre-flights, ontology §4.6) and the config surface.
- **Streams in frame.** *Signals* (connection reachable / not; history on / off). *Forecasts* (a
  required thing missing → the clear statement). *Counts* (none).
- **Intervention.** The pre-flight gates — connection, permission — refuse *first*, before any write
  (ontology §4.6 / P-GATE).
- **Outcomes.**
  - **Positive** — `✓` everything required is set. Often *unannounced* (a clean arrival is not stated;
    the run proceeds). On request: `History: on (./runs) · Connection: not set · Output: ./out`.
  - **Negative** — required and missing → a clear statement: `A connection to UAT is required for
    this. Pass --conn <ref> (or set PROJECTION_CONN).` Set-but-invalid → concrete and located: `line
    12, "threshold" must be a number, not "ten". Correct it and rerun.` The variable name and stack
    live in the substantiation.
  - **Edge** — optional and unset → a *recommendation*, not red: `Run history is not being retained.
    To keep a record of runs over time, set PROJECTION_LEDGER_DIR.` A would-throw, caught and
    translated, never a raw exception.
- **Diction.** *Streams 2.3 (forecast) + 2.4 (signal).* The instrument never faults the operator for
  not knowing its switches (`THE_VOICE.md` §14).

---

### ACT 1 — Reading *(Snapshot)*

- **Moment.** What the model contains — read into a comparable value. For in-place and drift, also what
  is deployed.
- **Camera intent.** *Quiet orientation.* The establishing wide shot of the estate.
- **Who enters.** `Snapshot` (ontology §2.2 / §4.3). For in-place and drift, a *second* Snapshot (the
  server) enters — the only act that reads two subjects.
- **Streams in frame.** *Counts* (`N tables`, `N columns`, rows profiled). *Signals* (read complete).
  In Watch, the first live stage: `Reading the model ✓ 1.2s`.
- **Intervention.** None (read-only).
- **Outcomes.**
  - **Positive** — `✓` `Model read complete — 300 tables.` (Often folded into the next act's statement.)
  - **Negative** — read failed: `The model failed to load: line 12 is missing a name. Correct it and
    rerun.` / `UAT is unreachable. Check the connection and retry.` (`THE_VOICE.md` §10.)
  - **Edge** — read completes but the profile is empty (no data evidence): proceed, and let downstream
    acts state plainly what could not be checked (honest without exception, `THE_VOICE.md` §1). Partial
    read → name what is missing.
- **Diction.** *Streams 2.1 (count) + 2.4 (signal).* Concrete names from the start (`Country`, never
  `OS_KIND_Country`; `THE_VOICE.md` §2.1, §5).

---

### ACT 2 — What changed *(Diff)*

- **Moment.** The difference (`B ⊖ A`), surfaced big-rocks-first.
- **Camera intent.** *Recognition without overwhelm.* The few that matter, surfaced; the rest rolled
  up honestly.
- **Who enters.** The schema moves take the stage as named subjects: **Add · Remove · Rename ·
  Reshape** (ontology §4.2); on the data plane, **Insert · Update · Delete · Unchanged** (§4.3). Each
  rendered statement-first (`THE_VOICE.md` §4). `Tolerate` enters quietly — the cosmetic differences
  named and excluded.
- **Streams in frame.** *Counts* (`‖δ‖`, per-channel move counts, `N drops`, `N narrowings`).
  *Spectrums* (each change's faithfulness class — safe / validated / approval-required). *Forecasts*
  (`Email column widened from 50 to 100 characters. The change is instant.` / `Status column
  narrowed; the existing data has been validated … and so the change applies without loss.`). *Signals*
  (the verdict line).
- **Intervention.** None *here* — a detected drop / narrowing routes forward to Act 3. The diff names
  it; the gate asks it.
- **Outcomes.**
  - **Positive** — `✓ Safe to apply: fully reversible, with no data loss.` `14 changes · all additive
    · review before applying.` Big rocks named, cosmetic rolled up: `2,124 additive changes · 380
    cosmetic, excluded by named tolerances.`
  - **Negative** — a destructive move is present: `▲ Paused. Approval required before removal.` `Drops
    the IX_Order_Stale index. No data is lost.` (Routes to Act 3.) Not an error — a recognition that
    routes to consent.
  - **Edge** — **no differences**: `✓ No differences found.` (The diff is empty over every declared
    facet; ontology §5.0 P-CMP.) **At scale**: `2,140 changes across 300 tables · 4 drops · review
    before applying.` — same one-line verdict; cluster by table beneath (`Sales · 412 changes ▸`);
    find, don't scroll (`THE_VOICE.md` §12).
- **Diction.** *Streams 2.1 + 2.2 + 2.3.* The move lexicon (`THE_VOICE.md` §4); the scale discipline
  (§12). Never `δ`, `norm`, `OS_KIND_*` on the statement (§2.2).

---

### ACT 3 — The one decision *(Gate — the intervention act)*

- **Moment.** A stop, before any write. The single act that waits.
- **Camera intent.** *Consent, not alarm.* A question a person can answer — never a wall of error.
- **Who enters.** `Declare(-loss)` / `Gate` (ontology §2.2, §4.6). Exactly **one lever** takes the
  stage at a time, even when several gates could fire (`THE_VOICE.md` §5, §8 — never a list of ten).
- **Streams in frame.** *Interventions* (the gate statement + the active imperative). *Forecasts* (the
  consequence as meaning: `4 rows currently exceed that length`). *Counts* (the precise scope: `3 of
  142 users`).
- **Intervention.** This *is* the intervention. The eight operator-facing gates (`THE_VOICE.md` §5):
  declared-loss · validate-user-map · declare-every-drop · intent-filter · data-compat ·
  provenance-completeness · drift · round-trip verification. Each: *consequence as meaning · one lever · imperative
  · wait.*
- **Outcomes.**
  - **Positive** — **the act is empty**, a designed outcome, not a missing screen: nothing to consent
    → it elides silently to Act 4. (Most idempotent and additive runs.)
  - **Negative** — a gate fires: `▲ The IX_Order_Stale index will be dropped. No data is lost, but the drop is
    not auto-reversible.` → `Approve the removal, or halt.` The verb is always plain, active,
    imperative (Approve · Map · Confirm · Trim · Record · Accept · Remediate · Halt).
  - **Edge** — *multiple* gates could fire → the **most honest, least overwhelming** single lever is
    named, the rest held beneath (`THE_VOICE.md` §8). A drift gate names a *three-way* lever (accept /
    remediate / escalate) — still one question, three plain answers.
- **Diction.** *Stream 2.5 (intervention).* The imperative lexicon (`THE_VOICE.md` §5); never `Declare
  loss`, never `Override`, never `Proceed (Y/N)`.

---

### ACT 4 — Making it real *(realize — Watch)*

- **Moment.** The changes, applied live. Stages fill in, in plain words, with a calm estimate.
- **Camera intent.** *Motion that can be trusted.* Progress that never misstates; a no-op that reads
  as designed.
- **Who enters.** The realizing moves: **Rename → Reshape → Add** (schema, in order), then **Move /
  Insert / Update / Unchanged / Delete** (data, FK-topological); `Reidentify` when crossing substrates
  (ontology §3 ordering; §4.4). `Publish` hands the artifact to its terminus.
- **Streams in frame.** *Forecasts* (the estimate, `~8s remaining`; `142 of 300 tables`). *Counts*
  (rows moved, statements run, batches). *Signals* (per-stage `✓ / ⣷ / ○`). Live stage list
  (`THE_VOICE.md` §13).
- **Intervention.** None mid-stream by design — the gate already fired (Act 3). The exception is a
  *mid-run failure*, which routes to a candid error (below).
- **Outcomes.**
  - **Positive** — stages complete with honest timings:
    `Reading the model ✓ 1.2s · Checking the data ⣷ 142 of 300 · ~8s remaining`. Each finished stage
    *names what follows* (`THE_VOICE.md` §13 — nothing terminates at "done").
  - **Negative** — a stage fails mid-run (connection drops, permission denied, mid-statement abort):
    `✕ Stopped before any change was completed. The cause is shown below.` `ALTER permission is denied
    on dbo.Order. Grant ALTER, then retry.` And the safety statement: the sink is left **clean or at a
    deterministic resume point** (ontology §4.6 P-EXE), stated plainly: `No partial write remains;
    safe to retry.`
  - **Edge** — **the no-op**: nothing to do → a single calm line, not an empty progress bar (`Nothing
    to apply. The database is provably unchanged.`). **Estimate uncomputable** → none shown, never a
    bar that misstates. **Resume** → `Resuming from the last committed point — rows 1–4,000 already
    loaded.`
- **Diction.** *Streams 2.3 (forecast/estimate) + 2.1 (count) + 2.4 (signal).* Stage names are
  operator-shaped (`Building the changes`, never `emit`); errors per `THE_VOICE.md` §10.

---

### ACT 5 — The touch *(Measure — minimality proven)*

- **Moment.** The exact extent of the change — the norm `‖δ‖`, read off the CDC ruler.
- **Camera intent.** *Quiet rigor.* The minimality of the touch, stated plainly, proven beneath.
- **Who enters.** `Measure` (ontology §2.2, §5.6). The minimality proof (`THE_VOICE.md` §6).
- **Streams in frame.** *Counts* (CDC capture count = `|true delta|`; per-channel `‖π_c δ‖`; churn vs
  net). *Spectrums* (isometric vs norm-inflating — minimal touch vs fresh-reload fallback). *Signals*
  (the confirmed-idempotent state: `CDC = 0`).
- **Intervention.** None.
- **Outcomes.**
  - **Positive** — `✓ 312 rows changed — exactly those that differed, and no others.`
    beneath: `‖δ‖ = 312 = CDC capture count` (`THE_VOICE.md` §6 minimality).
  - **Negative** — the honest fallback: a fresh wipe-and-load moved `2×|table|` rows. **Named as the
    fallback, not hidden**: `The full table was reloaded (the safe path); more rows were touched than
    strictly differed.` (ontology §5.6 — norm-inflating is *why* it is the fallback.)
  - **Edge** — **CDC = 0**: the silence *is* the proof — `Confirmed idempotent: zero rows captured,
    zero schema changes issued.` (`THE_VOICE.md` §6 CDC-silence.) **Unmeasurable** (CDC not tracked):
    stated plainly — `Applied. This table is not CDC-tracked, so the row count cannot be proven; the
    expected count is shown.`
- **Diction.** *Streams 2.1 (count) + 2.2 (spectrum) + 2.4 (signal).* `‖δ‖` and `capture count` live in
  the substantiation; the statement is `rows changed` / `rows touched` (`THE_VOICE.md` §2.1).

---

### ACT 6 — Verification *(Verify — fidelity proven)*

- **Moment.** The result, confirmed against the model — the read-back asserts `Ingest ∘ Project = id`
  modulo named tolerances.
- **Camera intent.** *Earned confidence.* The engine's deepest claim about itself, stated as a grounded
  finding, confirmable beneath (`THE_VOICE.md` §6).
- **Who enters.** `Verify` / the canary (ontology §2.2, §4.6, §5.7). `Tolerate` (the named cosmetic
  residual).
- **Streams in frame.** *Signals* (`the database matches the model` / `one difference`). *Spectrums*
  (the ladder → readiness). *Counts* (residual size; named tolerances).
- **Intervention.** A *failed* round-trip verification fires the round-trip verification gate (`THE_VOICE.md` §5): the failure
  blocks the commit.
- **Outcomes.**
  - **Positive** — `✓ Verified. The deployed database now matches the model.`
    beneath: `residual ∅ · Ingest ∘ Project = id` (`THE_VOICE.md` §6 commuting square).
  - **Negative** — `✕ The round-trip returned one difference. It is shown below and must be resolved
    before shipping.` (The one difference shown concretely; it blocks the commit.)
  - **Edge** — **applied but unverifiable**: `The change was applied, but the read-back is unavailable,
    so the result is unverified. Re-verify when the endpoint is reachable.` (`THE_VOICE.md` §10.)
    **Equal-modulo-tolerance**: `Verified, with 3 differences excluded by named tolerances; each is
    listed.` (intent filter; ontology §5.4).
- **Diction.** *Streams 2.4 (signal) + 2.2 (spectrum).* The proof sentences (`THE_VOICE.md` §6); the
  notation in the substantiation only.

---

### ACT 7 — Recorded *(Record — continuity)*

- **Moment.** The run, saved — the episode written durably; the next sprint's diff now has a valid
  prior.
- **Camera intent.** *Closure that opens forward.* The run becomes part of a live history, not a dead
  log.
- **Who enters.** `Record` / `Accumulate` (ontology §2.2, §4.4). The episode + the refactorlog append.
- **Streams in frame.** *Counts* (`run 11`, `14 changes`). *Signals* (`saved`, `verified`).
- **Intervention.** At eject only: the provenance-completeness gate (`THE_VOICE.md` §5) — *the history
  has one gap.*
- **Outcomes.**
  - **Positive** — `This run recorded to the history.` → episode line:
    `run 11 · 2 min ago · 14 changes · ✓ verified` (`THE_VOICE.md` §13).
  - **Negative** — recording failed / the chain is inconsistent: surfaced, not swallowed — a
    recorded-but-broken episode would corrupt the next diff (ontology §3 "Record last"), so a failure
    here is loud and plain.
  - **Edge** — **the provenance gap** (at eject): `The history has one gap: run 7 was never recorded.`
    → `Record it, or freeze with the gap noted.` **Genesis** (first run): no prior to thread — stated
    as a beginning, not an absence.
- **Diction.** *Streams 2.1 (count) + 2.4 (signal).* `Episode` / `LifecycleStore` never on the surface
  (`THE_VOICE.md` §2.1 → `this run` / `the history` / `the record`).

---

### ACT 8 — Where it stands *(timeline + ladder)*

- **Moment.** The run takes its place; the distance to cutover — the schema's whole life as one calm
  shape.
- **Camera intent.** *Motivate by distance.* The streak read as *distance to cutover* (`THE_VOICE.md`
  §8).
- **Who enters.** The timeline (runs as dots) and the ladder (the single lever).
- **Streams in frame.** *Spectrums* (readiness `7 of 10`; the ladder per-axis). *Counts* (`11 runs`,
  `3 green checks remain`). *Signals* (a failed run, named never hidden).
- **Intervention.** None — but the ladder always names the one lever (which routes to a future Act 3).
- **Outcomes.**
  - **Positive** — `Cutover ███████░░░ 7 of 10 green · 3 green checks remain.` The marker tied to a
    real run and time: `▸ run 11, just now`.
  - **Negative** — a failed run is **named, never hidden**: `run 5 failed`. The ladder names the one
    outstanding item: `The lever: a user-fallback on 3 accounts.`
  - **Edge** — **no history yet** (genesis): `Run history is not being retained. To keep a record of
    runs over time, set PROJECTION_LEDGER_DIR.` (folds back to Act 0's recommendation). **At the
    finish**: `All 10 checks pass — ready to freeze.`
- **Diction.** *Streams 2.2 (spectrum) + 2.1 (count).* Never `L1/L2/L3` / `faithfulness ladder` on the
  statement (`THE_VOICE.md` §2.1 → `readiness` / `what remains before cutover`); walking time is
  `← earlier · later →` (§9).

---

## 4 — The call sheets (the nine verbs, cast against the acts)

Each protein (ontology §3) is the spine with a particular cast. `●` = central this verb; `○` = present
when the sprint's delta contains it; `·` = elided. The **felt arc** and the **lead verdict** are from
`THE_VOICE.md` §7; the casting is the incidence matrix (ontology §4.7).

| Verb (protein) | 0 Arrival | 1 Read | 2 Diff | 3 Gate | 4 Real | 5 Touch | 6 Verify | 7 Record | 8 Stand | Lead verdict · felt arc |
|---|---|---|---|---|---|---|---|---|---|---|
| **P-1/2 · Load → on-prem** | ○ | ● | ● | ○ | ● | ● | ● | ● | ● | `Ready to load Dev to on-prem.` · curious→clear→loaded→settled |
| **P-3 · UAT re-key** | ● | ● | ● | ● map | ● move | ● | ● | ● | ● | `Ready, once 3 users are mapped.` · careful→guided→mapped→confident |
| **P-4 · SSIS publication** | · | ● | ● | ● declare | ● publish | ○ | · | ● | ○ | `The delta for the SSIS team, replayable from genesis.` · responsible→precise→handed off |
| **P-5 · Idempotent redeploy** | ○ | ● | ● *(empty)* | · | ● *(no-op)* | ● `=0` | ● | ○ | ○ | `Nothing to apply. The database is provably unchanged.` · reassured (confirmed idempotent) |
| **P-6 · In-place migrate** | ● | ●● *(both)* | ● | ● confirm | ● | ● | ● | ● | ● | `14 changes, 1 to confirm.` · alert→consenting→verified→trusting |
| **P-7 · Eject / freeze** | · | ● | ● *(genesis→frozen)* | ● provenance | ● publish | · | ● | ● *(chain closed)* | ● *(finish)* | `Frozen. Provenance complete from genesis to freeze, verified.` · deliberate→certain→terminal |
| **P-8 · Drift** | ○ | ●● *(both)* | ● | ● accept/remediate/escalate | *(remediate=P-6)* | ● size | · | ○ | ○ | `The server diverges in 2 places.` · watchful→informed→decisive |
| **P-9 · Round-trip verification** | · | ● | ● *(round-trip)* | ● round-trip | ● ephemeral | ● `=0` | ● | · | · | `Fidelity holds: the round-trip is identical.` · quiet confidence, or one clear divergence |

**The storyboard test (`THE_VOICE.md` §7):** a developer on a *first* run and on a *hundredth* read the
**same lead verdict** — the first as orientation, the hundredth as a glance already past. Every call
sheet above must pass it.

Three castings worth reading closely, because they stress the design:

- **P-5 (redeploy)** is the *near-silent* film: every act plays, but acts 2–4 are designed to land on
  *empty* (`No differences found` → no gate → no-op), and the weight falls on Act 5's `CDC = 0` and the
  grounded `Confirmed idempotent`. The edge outcome *is* the product here.
- **P-6 (migrate)** is the *full* film — the only verb that reads both Snapshots (model + server),
  exercises every plane, and plays all nine acts. It is the worst case for calm, so it is the design
  target for §12 scale discipline.
- **P-9 (canary)** is the film *about the films* — its Act 6 is the engine proving its own Act 6. Its
  negative outcome (one divergence) is the single most important screen in the entire surface, because
  it is the only place the instrument reports on *itself*.

---

## 5 — The diction inventory (vocabulary bound to streams and outcomes)

With the surfaces named, the diction can be inventoried precisely. The voice is **one register** under
**twelve rules** (`THE_VOICE.md` §1 — no pronouns; legible statement with the formal substantiation
beneath; verdicts asserted; the true verb; evidential grounding; no antithesis). This section binds
that register to each stream-type (§2) and each outcome (positive / negative / edge), so a builder
reads *down* from "which stream, which outcome" to the words.

### 5.1 By stream-type (the rendering lexicon)

| Stream | Statement diction (on the surface) | Substantiation (one level beneath) | Banned on the statement |
|---|---|---|---|
| **Counts (2.1)** | `4,210 rows` · `312 rows changed` · `2,124 additive changes` · `and 1,847 more` | `‖δ‖ = 312` · `+4,210` · per-channel breakdown | `2140` (un-grouped) · `‖δ‖` · `norm` |
| **Spectrums (2.2)** | `7 of 10 green` · `3 green checks remain` · `the one outstanding item` · `safe / validated / approval-required` | `schema ✓✓✓ · identity ✓✓▲` · `L1/L2/L3` · faithfulness-class names | `L1/L2/L3` · `faithfulness ladder` · `lossy` |
| **Forecasts (2.3)** | `Email column widened from 50 to 100 characters. The change is instant.` · `The existing data has been validated … and so the change applies without loss.` · `~8s remaining` · (no bar when uncomputable) | `before → after · faithfulness class · data validated before apply` | a progress bar that misstates · `would throw` raw |
| **Signals (2.4)** | the 7 verdicts + glyph (`✓ ▲ ✕`) · `the database matches the model` · `no removals` · `The database is provably unchanged.` | `residual ∅ · Ingest ∘ Project = id` · `exit 9 · gate=…` | `ERROR/FAILED/REFUSED` as a lead · `nothing destroyed` · `and that's real` |
| **Interventions (2.5)** | consequence-as-meaning + one lever + active imperative (`Approve · Map · Confirm · Trim · Record · Accept · Remediate · Halt`) | `gate=declared-loss · --allow-drops` | `Declare loss` · `Override` · `Proceed (Y/N)` |
| **Intentions (2.6)** | carried by tone + what is surfaced: calm, concrete, authoritative, humble | — | warmth-performance · drama · figurative metaphor |

### 5.2 By outcome register (the tone that survives across all verbs)

- **Positive.** A grounded finding, the proof one level beneath. End on the next move, never on "done"
  (`THE_VOICE.md` §4). `Safe to apply: fully reversible, with no data loss.` / `Verified. The database
  matches the model.` The master glances; the newcomer reads the finding; one statement.
- **Negative.** *Candid, never dramatic* (`THE_VOICE.md` §10). The shape is fixed: **what happened
  (concrete) · why (plain) · the next move (imperative)**. A refusal is `Cannot migrate yet: a database index would be dropped.`, never `REFUSED`. A failure names the next move and the safe state (`No partial
  write remains`). The exit code and stack are real — and in the substantiation.
- **Edge.** The hardest design and the clearest tell of quality. The **no-op is a designed line**
  (`Nothing to apply. The database is provably unchanged.`), not an empty screen. The **unset thing is
  a recommendation**, not an error. The **unverifiable** is named with candor (`the read-back is
  unavailable, so the result is unverified`). The **at-scale** opens on the *same calm screen* as the
  3-change run (`THE_VOICE.md` §12). The **failed run in history** is named, never hidden.

### 5.3 The always-rules (the twelve, as a checklist over every string)

Before any line lands, it passes `THE_VOICE.md` §1 + §2.2: no pronouns · direction by imperative ·
legible statement with formal substantiation beneath · verdicts asserted · the true verb · gentle and
direct, never colloquial · neutral reference to the estate · every claim grounded in its evidence ·
ordered by real structure · the exact referent · concrete definite subjects · stative, agentless voice
(report states and events, never actions performed; gerunds-in-progress excepted). And it clears the
banned list (no antithesis tic, no euphemism, no drama, no system-shout, no jargon on the statement, no
figurative terms, no leaked internals). If a line fails one, it is not finished.

### 5.4 What this unlocks for the build

With §3 (streams per act) and §5 (diction per stream) in hand, the per-site Voice declaration
(`THE_VOICE_INTEGRATION.md` decision 2 — declare-at-site, harvest-centrally) has a *complete target*:
each emission site knows which act it speaks in, which streams it carries, and therefore which diction
rows apply — and the `code ⇔ copy` totality test can assert that **every act × stream × outcome cell a
verb reaches carries a line that passes the twelve rules**. The storyboard is the coverage map the
totality test checks against.

---

## 6 — How to read / bless this

- **Building a surface?** Find its act (§3) and its verb's call sheet (§4); read the streams in frame
  and the three outcomes; derive the copy from §5 + `THE_VOICE.md`, under the twelve rules. Do not
  invent a tone.
- **Unsure it is complete?** The completeness test is **act × stream × outcome**: every act a verb
  reaches carries its positive, negative, *and* edge line, in the right streams, passing the twelve
  rules. A blank edge cell is the bug.
- **Blessing this doc.** Three things to react to: (1) the **nine-act spine** (§1) — the right
  decomposition of every run; (2) the **six streams** (§2) — they name everything that flows; (3) the
  **positive/negative/edge symmetry** (§3, §5.2) — the right definition of the quality bar. If those
  hold, the diction (§5) is derivation, not invention.

*The instrument disappears. The storyboard is how it disappears the same way in every scene — the calm
of the success, the candor of the refusal, and the designed quiet of the no-op, all stated in one
exact, humble voice.*

---

## 7 — The build-readiness map (live vs latent)

The storyboard doubles as a build map. The proofs the engine makes about itself are largely **live
today**; the calm operator-facing *surfaces* that render them are largely **latent**. Status is from
the canonical current-state ledger (`DEBRIEF_2026_06_02_ISOMORPHISM_CLIMB_AND_BACKLOG.md`): **LIVE** =
built and tested · **PARTIAL** = the data exists, the surface is unbuilt · **LATENT** = designed, not
built.

**By the two axes (§1).** The **emission axis** (acts 1–6 — this run) is mostly lit: the diff, the
measure, and the verify are real engines. The **episode axis** (acts 7–8 — across runs) is **half-lit**:
the durable `Episode` / `LifecycleStore` landed (the data persists), but the operator-facing timeline
and readiness ladder are unbuilt. **Depth** (statement → substantiation) is unbuilt everywhere — the
`View.Disclosure` substrate exists; the per-surface authored copy does not.

| Surface (act) | Status | Basis (debrief) |
|---|---|---|
| Diff / what changed (2) | **PARTIAL** | `CatalogDiff.between` — schema L2; Reference/Index/Sequence channels landed (C1); `IndexOptionsUnreflected` residual named. |
| Declared-loss · data-compat · drift gates (3) | **LIVE** | `Preflight.*` + `MigrationRun` CDC pre-flight refuse before any write (6.A.13 / 6.B.1). |
| Validate-user-map gate (3) | **LIVE** | Transfer consumes the refactorlog-aware remap; gate halts pre-SQL (6.B.2). |
| Connection · permission pre-flights (0) | **LIVE** | `Preflight.connectionPreflight` / `permissionPreflight` wired (A1 / A2). |
| Provenance-completeness gate (7, eject) | **LATENT** | Episode substrate landed; the eject consumer is unbuilt (PROD-data-gated). |
| Live run / Watch streaming (4) | **LATENT** | `ChangeManifest` records per-stage displacement; no streaming terminal surface yet. |
| Measure / CDC count + silence (5) | **LIVE** | `ChangeManifest` integrates the CDC capture series; CDC-silence witnessed (G10 / 6.A.13). |
| Verify / round-trip (6) | **LIVE** | `MigrationRun` reads B′ back; `CanaryRoundTripTests` — the T16 live square. |
| Record / Episode (7) | **PARTIAL** | Durable `LifecycleStore` + FTC landed (6.H.1–4); no operator-facing record line yet. |
| Timeline + ladder (8) | **PARTIAL** | The matrix/ladder data is generated (D1/D2); the calm visual surface is unbuilt. |
| migrate (P-6) | **LIVE** | `MigrationRun.executeFromLive` — A live → evolve → B′ verified (6.D.1 / B1). |
| transfer (P-3) | **LIVE** | `TransferRun` cross-substrate load + remap + drop exit-9. |
| eject (P-7) · publish (P-4) | **LATENT / PARTIAL** | `EjectRun` signature exists, consumer unbuilt; refactorlog/`ChangeManifest` accumulate, SSIS consumer unwired (C3/C4). |
| Config / setup readback (0) | **PARTIAL** | `Preflight` validates endpoints; an `osm config status` readback is unbuilt. |

**The reading.** The instrument can already *prove* a migration end to end — diff, refuse, apply,
measure the minimal touch, verify B′ against the model. What is unbuilt is almost entirely the **voice
and the surfaces**: the streaming Watch (act 4), the record line (act 7), the timeline/ladder (act 8),
and the depth layer everywhere. The storyboard is the map of that remaining work; `THE_VOICE.md` is its
register.

---

## 8 — The worked proof: P-6 migrate, frame by frame

P-6 is the full film — the only verb that reads both Snapshots and plays all nine acts (§4).
Storyboarded to per-string fidelity, in the locked register, it shows the granularity land and doubles
as the build map. Tag: **[LIVE]** the engine does this and a surface could render it today; **[LATENT]**
the data exists, the surface does not.

**Act 0 — Arrival** · *[LIVE — connection / permission pre-flights]*
- *Streams:* signals (reachable / granted) · forecasts (a missing requirement).
- **+** (silent proceed) — on request: `Source: UAT (reachable) · Target: UAT (reachable) · ALTER: granted.`
- **−** `ALTER permission is denied on dbo.Order. Grant ALTER, then retry.`
- **edge** `Run history is not being retained. To keep a record of runs over time, set PROJECTION_LEDGER_DIR.`

**Act 1 — Reading** · *[LIVE — `MigrationRun.executeFromLive` reads A live; the model is read]*
- *Streams:* two Snapshots (deployed A + model B) · counts · signals.
- **+** `Model read complete — 300 tables. Deployed schema read from UAT.`
- **−** `The model failed to load: line 12 is missing a name. Correct it and rerun.`
- **edge** the deployed schema reads clean, but 12 tables are not CDC-tracked — carried to Act 5.

**Act 2 — What changed** · *[PARTIAL — `CatalogDiff.between`, schema L2; index-options residual named]*
- *Streams:* counts (`‖δ‖`, per-channel; churn vs net) · spectrums (faithfulness class) · forecasts.
- **+** `Safe to apply: 14 changes, all additive and reversible.`
- **−** `Paused. Approval required before removal. The IX_Order_Stale index will be dropped. No data is lost.` (→ Act 3)
- **edge** `No differences found.` (degrades to P-5) · at scale `2,140 changes across 300 tables · 4 drops · review before applying.` + `3 differences excluded by a named tolerance (IndexOptionsUnreflected).`

**Act 3 — The one decision** · *[LIVE — declared-loss + data-compat gates]*
- *Streams:* interventions (one lever) · forecasts (the consequence) · counts (the scope).
- **+** (empty — an additive run elides straight to Act 4).
- **−** `The Status column will be narrowed to 20 characters. 4 rows currently exceed that length.` → `Trim the 4 rows, widen the target, or halt.`
- **edge** two gates eligible → the most-honest single lever is named; the rest wait beneath.

**Act 4 — Making it real** · *[LATENT — `MigrationRun.execute` applies in-place; the streaming Watch surface is unbuilt]*
- *Streams:* forecasts (estimate) · counts (rows / statements) · signals (per-stage).
- **+** `Reading the model ✓ 1.2s · Checking the data ✓ · Building the changes ⣷ 142 of 300 · ~8s remaining` — gerund stage labels per rule 12; this live stream is the latent surface (today the run completes, then reports).
- **−** `Stopped before any change was completed. ALTER permission is denied on dbo.Order. Grant ALTER, then retry.` + `No partial write remains; safe to retry.`
- **edge** no-op → a single calm line; estimate uncomputable → none shown.

**Act 5 — The touch** · *[LIVE — `ChangeManifest` CDC capture; CDC-silence]*
- *Streams:* counts (CDC capture = |Δ|; churn beside net) · spectrums (isometric vs norm-inflating) · signals (silence).
- **+** `312 rows changed — exactly those that differed, and no others.` · substantiation `‖δ‖ = 312 = CDC capture count · churn 312, net 312`.
- **−** (fallback) `The full table was reloaded (the safe path); more rows were touched than strictly differed.` · `churn 12,000, net 312`.
- **edge** `Confirmed idempotent: zero rows captured, zero schema changes issued.` · or not CDC-tracked → `Applied. This table is not CDC-tracked, so the row count cannot be proven; the expected count is shown.`

**Act 6 — Verification** · *[LIVE — `MigrationRun` reads B′ back; the T16 live square; canary]*
- *Streams:* signals (matches / one difference) · spectrums (readiness) · counts (residual).
- **+** `Verified. The deployed database now matches the model.` · substantiation `residual ∅ · Ingest ∘ Project = id · the seeded row survived`.
- **−** `The round-trip returned one difference. It is shown below and must be resolved before shipping.`
- **edge** `The change was applied, but the read-back is unavailable, so the result is unverified. Re-verify when the endpoint is reachable.`

**Act 7 — Recorded** · *[PARTIAL — durable `Episode` / `LifecycleStore` landed; the record line is latent]*
- *Streams:* counts (run, changes) · signals (verified).
- **+** `This run recorded to the history.` → `run 11 · 2 min ago · 14 changes · ✓ verified` — the episode persists today; the operator-facing line is the latent surface.
- **−** chain inconsistent → surfaced loudly (a recorded-but-broken episode would corrupt the next diff).
- **edge** genesis (first run) — stated as a beginning, not an absence.

**Act 8 — Where it stands** · *[PARTIAL — the matrix/ladder data is generated; the calm surface is latent]*
- *Streams:* spectrums (readiness, per-axis) · counts (runs, distance) · signals (a failed run, named).
- **+** `Cutover ███████░░░ 7 of 10 green · 3 green checks remain. ▸ run 11, just now.`
- **−** `run 5 failed.` · `The lever: a user-fallback on 3 accounts.`
- **edge** genesis → folds back to Act 0's recommendation.

**What the full film shows.** Today the engine *proves* P-6 end to end — it reads both states, refuses
the unsafe, applies the minimum touch, measures it against the CDC ruler, and verifies B′ against the
model (acts 0–3, 5, 6 are LIVE). What remains is almost entirely **voice and surface**: the streaming
Watch (act 4), the record line (act 7), and the timeline / ladder (act 8) — the data exists; the calm
rendering, in the twelve-rule register, is the build. P-6's storyboard is therefore both the
granularity proof and the work map: the proofs are real; the words and the live and historical surfaces
are what we build, derived from `THE_VOICE.md`.
