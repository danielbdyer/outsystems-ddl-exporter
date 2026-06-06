# THE VOICE — the operator-facing copy of the instrument

**An anchor piece, not a spec.** This is how the instrument *speaks* — written across the whole
Phase‑6 ontology (the eleven moves, the ten verbs, the nine workflows, the gates, the proofs, the
timeline) **and the instrument's own life around them: its scale (a handful of changes or thousands),
its lifecycle (a run filling in live, a history accumulating), and its setup (what is configured, what
is not, what would otherwise throw when something required is absent).** Everything the operator can
read in the terminal speaks in this voice, not only the changeset. Some copy maps to surfaces that
exist (`diff`, the gate, the readiness board); most maps to surfaces not yet built. **Where a line is
speculative, the register still holds** — read for the structure and the rules, not the exact string.
When a real surface lands, its copy is *derived from this voice*, not invented fresh.

The register is **authoritative, scientific, mature, and humble**, governed by one principle —
**evidential literalism**: every sentence asserts a proposition grounded in its evidence, about a
concretely‑named subject, ordered by its true structure, with no rhetorical figure standing in for
that precision. §1 makes it operable as eleven rules.

---

## 0 — The one sentence

> **The instrument is calm, exact, and humble: it states what is true, grounds the claim in its
> evidence, names the next move, and keeps the proof one level beneath — in one register that serves
> the newcomer and the master alike, because they are the same reader at two velocities.**

Everything below is that sentence, made specific.

---

## 1 — The register: eleven rules

The voice is a precise measurement report a newcomer can read aloud. Eleven rules make it operable. A
line that breaks one is not finished.

1. **No pronouns.** No first person (*I / we*), no second person (*you / your*). The instrument never
   refers to itself and never addresses the reader. Trust comes from precision and named limits, not
   from warmth or possession.
2. **Direction by imperative.** The next move is a bare imperative — `Grant ALTER, then retry.` —
   carrying direction without "you."
3. **Legible statement, formal substantiation beneath.** Every lead is a complete sentence, readable
   aloud, parseable with no prior context. No symbolic shorthand on top (`= → ∅ ‖·‖`), no telegraphic
   fragments. Notation lives one level beneath, in the substantiation.
4. **Verdicts are findings, asserted.** What the engine proves — safe, reversible, idempotent,
   matched — is stated plainly with authority; the proof beneath earns it. Hedge only on genuine
   interpretation (a configuration recommendation, optional advice).
5. **The true verb.** `Drops · Deletes · Narrows · Rewrites` — the precise operation, never softened
   (*removes, cleans up*) and never dramatized (*destroys*). No euphemism.
6. **Gentle and direct; never colloquial.** Composed, complete, candid; a limit named without
   harshness and without casual idiom.
7. **Neutral reference to the estate.** *the model · the database · 300 tables* — never *your*.
8. **Ground every claim in its evidence.** A factive or evidential predicate carries the basis
   (`Verified` · `provably` · `Confirmed` · `has been validated`). Forbidden: the antithesis that
   simulates rigor instead of stating it — `X, not Y` (*"verified, not assumed"*; *"and that's real"*).
   Assert the positive evidential claim.
9. **Order by real structure.** Tense and connectives carry it: perfect aspect for what is established
   (`has been validated`), an explicit consequence for what follows (`and so the change applies
   without loss`). No ambiguous habitual present.
10. **Name the exact referent.** The precise operation and predicate, concisely (`user remapping`;
    `every identified user mapped across both environments`) — never a loose paraphrase.
11. **Concrete definite subjects.** Every assertion names what it is about; never a headless quantity
    (`No changes` → `The database is provably unchanged`).

**What carries from the prior register, unchanged:**

- **Calm.** The surface is quiet; the depth is complete. The most consequential statement is made in
  the most level tone. Calm is a trust signal.
- **One register, two velocities.** The newcomer is oriented; the master reads the same line as a
  glance. One statement serves both — never a beginner mode, never an expert mode. Power is fluency,
  not a denser screen.
- **The statement on top, the proof beneath.** The lead is a complete plain finding; the formal proof
  (notation, exit codes, norms) is one level beneath, reachable by anyone — the statement made
  rigorous, never the statement itself.
- **End on the move.** Every surface names the next action — the lever, the gate, the unmatched
  record, the command. Nothing terminates at "done"; where nothing remains, the surface states that.
- **Honest without exception.** Safety is never overstated. Every tolerated difference is named; no
  loss, tolerance, or unverified result is hidden. Trust is earned by naming the edges.

---

## 2 — The lexicon

### 2.1 What the surface says instead (internal → operator)

The engine's vocabulary is precise and internal; the operator's vocabulary is precise and plain. The
boundary translates — *always*.

| The engine's word | What the operator reads |
|---|---|
| `Kind` / `Entity` / `OS_KIND_Country` | **the table's name** — `Country` |
| `Attribute` / `OS_ATTR_…` | **the column's name** — `Email` |
| `Reference` / FK | **a relationship** (`Order → Customer`) |
| `SsKey` / Identity | (never shown; resolves to the name) |
| `Reshape` | **widened** / **narrowed** / **the type changed** |
| `Reidentify` / surrogate remap | **re‑keyed** (matched by business key; relationships preserved) |
| `‖δ‖` / norm / CDC capture count | **rows changed** / **rows touched** |
| `CatalogDiff` / delta | **the changes** / **what differs** |
| commuting square / isomorphism / `Ingest∘Project = id` | **the database matches the model** |
| CDC‑silence / idempotent redeploy | **provably unchanged** |
| faithfulness ladder (L1/L2/L3) | **readiness** / **what remains before cutover** |
| tolerance / quotient | **a difference excluded by a named tolerance** |
| Episode / `LifecycleStore` / refactorlog | **this run** / **the history** / **the record** |
| Gate / refusal | **paused** / **approval required** |
| declared‑loss | **approve the removal** |
| eject / freeze | **the terminal freeze** / **hand‑off** |
| canary | **self‑check** |
| drift | **a divergence on the server** |

### 2.2 Words and figures never used on the surface

- **Pronouns:** *I, we, you, your* (rule 1). The instrument neither narrates itself nor addresses the
  reader.
- **The antithesis tic:** *verified, not assumed* · *real, not a missed check* · *and that's real*
  (rule 8). It simulates rigor; state the positive evidential claim instead.
- **Euphemism and drama:** *removes / cleans up* for a drop or delete (rule 5); *destroy(s),
  destroyed, fatal, abort, blast radius* (drama). Use the true verb — `Drops`, `Deletes`, `Narrows`.
- **Figurative terms:** *dig, diggable, the green hush, open a lane, the jewel* — and any metaphor that
  asks the reader to share an image. Disclosure is **Show detail** (§9); the layer beneath is **the
  substantiation**.
- **System‑shout:** `REFUSED`, `ERROR`, `FAILED` as a *lead* (a small verdict tag is admissible; a
  complete sentence is better).
- **Jargon on the statement:** *δ, norm, torsor, quotient, commuting square, isomorphism, surrogate,
  episode* — all real, all in the substantiation, none on the lead. (*Idempotent* is admissible as a
  precise technical term once the plain finding has landed: `The database is provably unchanged. The
  redeploy was idempotent.` Never as the lead alone.)
- **Colloquialism and casual idiom** (rule 6): no *oops, let's, hang on, that was nice.*
- **Leaked internals:** `OS_KIND_*`, `OSUSR_*`, raw `SsKey` roots, file paths where a name belongs,
  exit codes on the statement line.
- **Negation‑as‑headline:** *"nothing destroyed"* → *"no removals"*; define by what *is*.

---

## 3 — The verdict line (the finding: *is it safe? did it work? what is next?*)

The first line of every surface answers the only three questions: **Is it safe? Did it work? What is
the next move?** A small, closed set of verdicts — each a finding the engine proves (rule 4), grounded
inline by its evidence (rule 8). Pick one; never invent a fourth tone.

| Situation | Verdict line | Glyph |
|---|---|---|
| Safe, reversible change | **Safe to apply: fully reversible, with no data loss.** | `✓` |
| Safe, with one item to review | **Ready. One item requires review.** | `▲` |
| A removal awaits consent | **Paused. Approval required before removal.** | `▲` |
| Done and verified | **Verified. The database matches the model.** | `✓` |
| Idempotent no‑op | **Verified. The database is provably unchanged.** | `✓` |
| Real failure | **Stopped before any change was applied. The cause is shown below.** | `✕` |
| No difference at all | **No differences found.** | `✓` |

The verdict line is the sentence the master reads as a heading and the newcomer reads as a finding. It
carries no number it does not need and no word it cannot defend.

---

## 4 — The eleven moves (statement on top, proof beneath)

Each move is rendered **statement‑first**: the complete plain finding everyone reads, the proof one
level beneath. The schema and data legs are the same shape one plane apart, so their copy rhymes.

### Schema plane

**Add** — *a new object, safely.*
- statement: `Added 6 tables, including Country. No existing object is altered.`
- beneath: `new identity minted · nothing else touched`

**Remove** — *a deletion, named exactly.*
- statement: `Drops the IX_Order_Stale index. No data is lost.`
- beneath: `the removal is not auto‑reversible · approval required`
- A removal is never silent and never dramatized. Name the one object, state whether data is affected,
  and route to approval (§5).

**Rename** — *renamed; rows preserved.*
- statement: `Renamed OrderHeader to SalesOrder. No rows are rewritten.`
- beneath: `old → new · ‖rename‖_data = 0 · pages preserved`
- The proof (`‖rename‖_data = 0`) is beneath. The statement is the plain finding: no rows moved. A
  rename that touched data would say so plainly — `This rebuilds the table.` — because then it is not a
  rename.

**Reshape** — *a column changed; instant or validated.*
- statement (widen): `Widened the Email column from 50 to 100 characters. The change is instant; no rows are rewritten.`
- statement (narrow): `Narrows the Status column from 50 to 20 characters. The existing data has been validated against the new limit, and so the change applies without loss.`
- statement (unsafe): `Cannot narrow the Amount column to a smaller type without losing data.`
- beneath: `before → after · faithfulness class · data validated before apply`

### Data plane

**Insert** — *new rows loaded.*
- statement: `Inserted 4,210 rows into Country.`
- beneath: `+4,210 · exactly the rows that were new`

**Update** — *changed rows only.*
- statement: `Updated 312 rows — exactly those that differ.`
- beneath: `312 captured · unchanged rows left untouched`

**Unchanged** — (this is the *silence* — see §6, the idempotent proof). Never a row‑by‑row line; the
absence is the finding.

**Delete** — *rows removed, within scope.*
- statement: `Deleted 18 rows that are absent from the source, within the declared scope.`
- beneath: `−18 · scope <where> · approval required`

### Cross‑plane

**Reidentify** — *the same records, re‑keyed for the new environment; relationships preserved.*
- statement: `Re‑keyed 138 of 142 records to the UAT environment; every relationship is preserved.`
- beneath: `the re‑key is an update keyed on the business key (email) · the FK re‑point is recorded`

**Move** — *rows moved, in dependency order.*
- statement: `Moved 4,210 rows from Dev to UAT, in dependency order.`
- beneath: `the load plan · the two‑phase boundary · ‖move‖ = rows touched`

**Accumulate** — *the changes since the last run.*
- statement: `12 changes since run 9: 2 new tables, 1 rename, the rest cosmetic.`
- beneath: `the evolution chain · churn (work done) beside net (distance moved)`

---

## 5 — The gates (consent, in plain words)

A gate is the instrument **stopping to ask** before it writes. The copy law: *state the consequence as
meaning, name the one lever, hand over the imperative, and wait.* Never a wall of error; always a
question a person can answer.

| Gate | The plain statement | The next move |
|---|---|---|
| **Declared‑loss** (a drop / scoped delete) | `Drops the IX_Order_Stale index. No data is lost, but the drop is not auto‑reversible.` | `Approve the removal, or halt.` |
| **Validate‑user‑map** (re‑key, pre‑write) | `User remapping cannot run until every identified user is mapped across both environments. 3 of 142 remain.` | `Map the 3 users, assign the system user, or halt.` |
| **Declare‑every‑drop** (publication) | `This changelog drops 2 objects the SSIS team depends on.` | `Confirm each, so the change replays cleanly downstream.` |
| **Intent‑filter‑complete** (idempotent) | `14 real changes. 3 differences are excluded by named tolerances; each is listed.` | `Review the 3, or apply.` |
| **Data‑compat** (narrowing) | `Narrows the Status column to 20 characters. 4 rows currently exceed that length.` | `Trim the 4 rows, widen the target, or halt.` |
| **Provenance‑completeness** (eject) | `The history has one gap: run 7 was never recorded.` | `Record it, or freeze with the gap noted.` |
| **Drift** (deployed differs) | `The server holds 2 objects the model does not.` | `Accept them, remediate, or escalate.` |
| **Self‑check** (canary) | `The round‑trip returned one difference.` | `The difference is shown below; it blocks the commit.` |

The verb on the move line is always **plain, active, imperative**: *Approve · Map · Confirm · Trim ·
Record · Accept · Remediate · Halt.* Never *Declare loss* (jargon), never *Override* (reckless), never
*Proceed (Y/N)* (evasive).

---

## 6 — The proofs (fidelity, made plain)

This is the soul. The deepest thing the engine knows about itself — that it is *faithful* — is stated
to everyone as a grounded finding, with the formal proof one level beneath. The newcomer reads the
sentence and the proof stands behind it; the master reads the proof and *confirms it*.

| Proof | The plain statement | The substantiation |
|---|---|---|
| **Commuting square** (it matches) | `Verified. The deployed database now matches the model.` | `residual ∅ · Ingest ∘ Project = id` |
| **CDC‑silence** (real no‑op) | `Confirmed idempotent: zero rows captured, zero schema changes issued.` | `CDC = 0 · zero ALTERs` |
| **Rename fidelity** (rows preserved) | `The rename preserved every row.` | `‖rename‖_data = 0` |
| **The ladder** (readiness) | `Seven of ten checks pass. Three remain before cutover. The one outstanding item: a user‑fallback on 3 accounts.` | `schema ✓✓✓ · data ✓✓✓ · identity ✓✓▲` |
| **The intent filter** (honest diff) | `14 real changes; 3 differences excluded by named tolerances, each listed.` | `each tolerance named · neither emitted nor dropped` |
| **Minimality** (the touch was minimal) | `Changed exactly the 312 rows that differed, and no others.` | `‖δ‖ = 312 = CDC capture count` |

The rule across all of them: **the statement is the proof made legible; the proof is the statement
made rigorous.** No reader is asked to take on faith what they would rather see.

---

## 7 — The nine workflows (the felt arc)

Each protein is the same rhythm — *Snapshot → Diff → Gate → realize → Measure → Verify → Record* —
differing in the change and the gate. The copy gives each a **verdict** (what it leads with) and a
**felt arc** (the composed shape the operator moves through). The arc is the design target; the words
are illustrative.

| Workflow | Leads with | Felt arc |
|---|---|---|
| **P‑1/2 · Load to on‑prem** | `Ready to load Dev to on‑prem.` | curious → clear → loaded → settled |
| **P‑3 · UAT with re‑key** | `Ready, once 3 users are mapped.` | careful → guided → mapped → confident |
| **P‑4 · SSIS publication** | `The delta for the SSIS team, replayable from genesis.` | responsible → precise → handed off |
| **P‑5 · Idempotent redeploy** | `Nothing to apply. The database is provably unchanged.` | reassured (the confirmed‑idempotent state) |
| **P‑6 · In‑place migrate** | `14 changes, 1 to confirm.` | alert → consenting → verified → trusting |
| **P‑7 · Eject / freeze** | `Frozen. Provenance complete from genesis to freeze, verified.` | deliberate → certain → terminal |
| **P‑8 · Drift** | `The server diverges in 2 places.` | watchful → informed → decisive |
| **P‑9 · Self‑check canary** | `Fidelity holds: the round‑trip is identical.` | quiet confidence, or one clear divergence |

The storyboard test for all nine: a developer on a *first* run and on a *hundredth* read the **same
verdict line** — the first as orientation, the hundredth as a glance already past.

---

## 8 — The timeline & the ladder (*readiness, and the one item in the way*)

**The timeline** is the schema's whole life as one calm shape — runs as dots, the present marked, the
streak filling toward cutover. The copy under it is a single plain line:

```
  ●●●●✕●●●●●●●▸          Cutover  ███████░░░  7 of 10
  └ 11 runs · run 5 failed · ▸ run 11, just now · 3 green checks remain
```

- Past runs: dots. A failed run is named, never hidden: `run 5 failed`.
- The present: the marker is tied to a real run and a real time — `▸ run 11, just now` — never a bare
  "here," which states neither which run nor when.
- The gate distance: `3 green checks remain` — the streak read as *distance to cutover*.

**The ladder** answers readiness and names the **one** outstanding item — the most honest, least
overwhelming next step:

```
  Cutover     ███████░░░   7 of 10 green     one item remains ↓
       The lever: a user‑fallback on 3 accounts.
```

Never a list of ten problems. One lever, named, with the next move.

---

## 9 — Affordances & navigation (the small words)

The microcopy of *moving* through the instrument. These are tiny and they are where colloquialism
creeps in — hold the line.

- **Disclosure (a collapsed node):** the affordance reads `Show detail` or a bare `▸ 3 more`. Never
  *dig*, never *open a lane*, never *expand the node*. The triangle does most of the work; the words
  state how much is inside.
- **Depth:** `Show more (→)` / `Less (←)`. One level at a time. Never the whole tree; never "level 3
  of 5."
- **Walking time:** `← earlier · later →` (not "prev/next run," not "−1/+1").
- **The next action** is always a plain imperative naming the *object*: `Map the 3 users` · `Approve
  the index drop` · `Run the migration` · `Trim 4 rows`. Never `Continue?`, never `[Y/n]`, never
  `Press any key`.
- **Filtering / search:** `Show only removals` · `Find a table` (not "filter by axis," not "grep by
  SsKey").

---

## 10 — Refusals & errors (saying *no* with candor)

When the instrument cannot do something, it states the situation plainly and names the next move. The
shape: **what happened (concrete) · why (plain) · the next move (imperative).** Never a stack trace on
the surface; never blame.

| Situation | What the surface states |
|---|---|
| Cannot reach the server | `UAT is unreachable. Check the connection and retry.` |
| Missing permission | `ALTER permission is denied on dbo.Order. Grant ALTER, then retry.` |
| Invalid input / model | `The model failed to load: line 12 is missing a name. Correct it and rerun.` |
| Could not verify | `The change was applied, but the read‑back is unavailable, so the result is unverified. Re‑verify when the endpoint is reachable.` |
| Self‑check failed | `The round‑trip returned one difference. It is shown below and must be resolved before shipping.` |

The exit code, the gate label, the stack — all real, all in the substantiation (`exit 9 ·
gate=connection‑unavailable`), none on the statement. A reader who reads only the first line still
knows the next move.

---

## 11 — Tone calibration (off → on)

The fastest way to internalize the voice: see it correct itself. Left is the failure mode (including
the prior conversational register, now retired); right is the gold standard.

| ✗ Off | ✓ On |
|---|---|
| `3 changes · 3 destroy structure` | `3 changes · 3 drops · review before applying` |
| `· OS_KIND_Country` | `· Country` |
| `‖δ‖ norm  ▲ 3` | `Total changes: 3` |
| `migrate refused — undeclared destructive change · exit 9` | `Cannot migrate yet: this drops a database index. Approve the removal to continue.` |
| `nothing destroyed` | `no removals` |
| `Nothing changed — and that's real.` | `Verified. The database is provably unchanged.` |
| `Your data stayed put.` | `No rows were rewritten.` |
| `I touched exactly the 312 rows that changed.` | `Changed exactly the 312 rows that differed, and no others.` |
| `7/10 L1–L3 axes green; identity axis at L2` | `7 of 10 checks pass. One item remains: 3 accounts to map.` |
| `It ran, but I couldn't read it back to confirm.` | `The change was applied; the read‑back is unavailable, so the result is unverified.` |
| `needs your okay` / `open a lane` | `approval required` / `Show detail` |
| `Operation completed successfully.` | `Verified. The database matches the model.` |

---

## 12 — At scale (a handful of changes, or thousands)

The estate is **300 tables and, on a big sprint, thousands of changes** — and the voice stays calm at
that size. The governing principle: **the surface is a constant size; only the numbers grow.** Calm is
not a function of how much changed.

- **The verdict stays one line.** `2,140 changes across 300 tables · 4 drops · review before
  applying.` The number scales; the sentence does not. Numbers are humane — `2,140`, not `2140`;
  `4,210 rows`, not `4210`.
- **The big rocks first.** Never a list of 2,140 things. Surface the few that matter — the drops, the
  narrowings, the one lever — and roll the rest up, impact‑ranked, never alphabetical: `4 drops · 12
  narrowings to review · 2,124 additive changes · 380 cosmetic, excluded by named tolerances.`
- **Cluster by where it lives.** The detail groups by table / module *before* it groups by change —
  `Sales · 412 changes ▸`, opened on demand. Breadth is disclosed the way depth is.
- **Cap the breadth, name the remainder.** A long list shows the top few and states `and 1,847 more` —
  with a way to reach them (search / filter), searchable, not scrollable. The wall of a thousand lines
  is the failure guarded against at every level.
- **Find, don't scroll.** At scale, navigation is search‑first: `Find a table` · `Show only removals`
  · `Jump to Country`.
- **The proof scales by summary, not enumeration.** `Changed exactly the 4,210 rows that differed
  across 300 tables, and no others.` One sentence, any size.

The test: a 3‑change run and a 3,000‑change run **open on the same calm screen** — same verdict shape,
same one‑line statement. Only the depth goes deeper.

---

## 13 — Lifecycle & the live run (Watch)

The instrument speaks about its own *running*, not only its results. While a run is in flight and
after it lands, every stage and episode has a voice — in scope for the terminal, same as the
changeset.

- **The live run (Watch).** Stages fill in, in plain words, with progress and a calm estimate:
  ```
    Reading the model      ✓  1.2s
    Checking the data      ⣷  142 of 300 tables · ~8s remaining
    Building the changes    ○
    Self‑checking           ○
  ```
  Stage names are what they *do for the operator* — `Reading the model`, `Checking the data`,
  `Building the changes`, `Self‑checking` — never the internal verb (`Snapshot`, `Profile`, `emit`,
  `canary`). The estimate degrades honestly: when none can be computed, none is shown — never a
  progress bar that misstates.
- **The episode (a run, recorded).** Each completed run is a line in the history: `run 11 · 2 min ago
  · 14 changes · ✓ verified`. Recording is stated plainly: `Saved this run to the history.`
- **The history (the record).** The accumulation reads as a sequence, not a log: `11 runs · last 3
  verified · 3 remain to cutover.` The record is live — opening a past run re‑proves it — never a dead
  audit trail.
- **The rhythm names the next move.** Each stage ends naming what follows: a finished diff offers the
  migration; a finished migration offers the verify; a finished verify offers the record. Nothing
  terminates at "done."

---

## 14 — Configuration & setup (the things that must be set)

A thing not configured is **a choice to make, not a failure.** The instrument states what is set, what
is not, and exactly how to set it — in the same calm voice. Config state, missing setup, and the
errors that *would otherwise throw* when something required is absent all speak here, as plain words,
never as a raw exception.

- **Optional and unset → a recommendation** (rule 4b, the interpretation register). `Run history is
  not being retained. To keep a record of runs over time, set PROJECTION_LEDGER_DIR.` Informative, not
  red — the instrument works without it and offers more on opt‑in.
- **Required and missing → a clear statement.** `A connection to UAT is required for this. Pass --conn
  <ref> (or set PROJECTION_CONN).` Names the requirement and how to provide it, without scolding.
- **Set but invalid → concrete and located.** `The configuration has a problem: line 12, "threshold"
  must be a number, not "ten". Correct it and rerun.` The line, the field, the expected shape — never
  "parse error at token 47."
- **A thing that *would* throw → caught and translated.** Anything that would otherwise surface as a
  stack trace (an unset environment variable, a malformed connection string, a missing file) is caught
  at the boundary and stated plainly: *what is missing · why it is required · how to provide it.* The
  variable name, the stack, and the exit code live in the substantiation.
- **Show the current setup on request.** A plain read‑back of state: `History: on (./runs) ·
  Connection: not set · Output: ./out`. The configuration is always legible without guessing.

The principle across all setup copy: **the instrument never faults the operator for not knowing its
switches.** It assumes a first meeting and states what to set.

---

## 15 — How to use this doc

- **Building a surface?** Find its move / verb / gate / proof above and *derive* the copy from the
  example — keep the register, fit the specifics. Do not invent a new tone.
- **Writing a new string?** Run the eleven rules (§1) and the banned list (§2.2) over it before it
  lands. If it fails one, it is not finished.
- **Unsure of a word?** Reach for the lexicon (§2.1). If the operator's word is not there yet, add it
  here first, then use it — this doc is the source of the operator's plain language, the boundary‑side
  sibling of the engine's ubiquitous language.
- **A surface not yet imagined?** The eleven rules still hold. State the finding; precision of the
  string can come when the surface does. That is what an anchor is for.

*The instrument disappears. What remains is the schema's truth and the operator's hand on it — stated
in one exact, humble voice.*
