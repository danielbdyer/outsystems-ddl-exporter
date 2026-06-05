# THE VOICE — the operator-facing copy of the instrument

**An anchor piece, not a spec.** This is the *felt sense* of how the instrument
speaks, written across the whole Phase‑6 ontology — the eleven moves, the ten
verbs, the nine workflows, the gates, the proofs, the timeline. Some of this
copy maps to surfaces that exist (`diff`, the gate, the readiness board); most
maps to surfaces not yet built, and a few to surfaces we can only guess at until
we get closer. **Where a line is speculative, the intent still holds** — read
for the register and the shape, not the exact string. When a real surface lands,
its copy is *derived from this voice*, not invented fresh.

It is grounded in two things: `THE_INSTRUMENT.md` (the vision — "the newcomer is
the power user; one essence, infinitely diggable") and the Apple‑clear‑diction
discipline (`DYNAMIC_DISPLAY.md` §7.8). This document is where that discipline
becomes *vocabulary*.

---

## 0 — The one sentence

> **The instrument is calm, concrete, and kind; it tells you the truth in plain
> words, hands you the next move, and keeps the proof one step beneath — and it
> speaks to the newcomer and the master with the exact same words, because they
> are the same person at two velocities.**

Everything below is that sentence, made specific.

---

## 1 — The voice, in eight principles

1. **Calm by default.** The surface is quiet; the depth is infinite. We never
   raise our voice — no `ERROR`, no `DESTROYED`, no alarm. The most dangerous
   thing on screen is stated in the most level tone. Calm *is* the trust signal.

2. **One surface, two velocities.** The newcomer is guided into the move; the
   master takes it in a stroke. We write **one** line that serves both — never a
   "beginner message" and never an "expert mode." Power is fluency at the dig,
   not a denser screen.

3. **Plain on top, proof beneath.** The lead line is human ("your data stayed
   put"). The proof is one dig down (`‖rename‖_data = 0 · every page kept`).
   Notation, exit codes, and norms live in the dig, reachable by anyone — they
   are the essence *made rigorous*, never the essence itself.

4. **End on the move.** Every surface names what you can do next — the lever,
   the gate, the orphan to map, the command to run. Nobody is ever told "done"
   and left standing there. If there is genuinely nothing to do, we say *that*,
   plainly.

5. **Concrete over abstract.** Name the thing — `Country`, the `IX_Order_Stale`
   index, *3 accounts* — never the category (`OS_KIND_Country`, "structure",
   "an entity"). A person should recognize what we're talking about on sight.

6. **Kind, never dramatic.** "removes" not "destroys"; "Can't yet" not
   "REFUSED"; "needs your okay" not "BLOCKED." We state consequences without
   theatre. Destruction is shown to *everyone*, calmly, before it happens.

7. **Honest to a fault.** We never overstate safety. We name what we're ignoring
   on purpose ("3 cosmetic differences, ignored — here they are"). We never hide
   a loss, a tolerance, or a thing we couldn't check. Trust is earned by
   admitting the edges.

8. **It's yours.** "*your* rename," "*your* data," "you're 7 of 10." The estate
   belongs to the operator; the instrument is the hand on it, not the authority
   over it. We report; we don't pronounce.

---

## 2 — The lexicon

### 2.1 What we say instead (internal → operator)

The engine's vocabulary is precise and ours; the operator's vocabulary is plain
and theirs. The boundary translates — *always*.

| The engine's word | What the operator hears |
|---|---|
| `Kind` / `Entity` / `OS_KIND_Country` | **the table's name** — `Country` |
| `Attribute` / `OS_ATTR_…` | **the column's name** — `Email` |
| `Reference` / FK | **a link** / **a relationship** (`Order → Customer`) |
| `SsKey` / Identity | (never shown; resolves to the name) |
| `Reshape` | **changed** ("the type changed", "made longer") |
| `Reidentify` / surrogate remap | **re-keyed** ("matched up", "kept the links") |
| `‖δ‖` / norm / CDC capture count | **changes** / **rows changed** / **rows touched** |
| `CatalogDiff` / delta | **what changed** |
| commuting square / isomorphism / `Ingest∘Project = id` | **it matches** ("your database matches your model") |
| CDC‑silence / idempotent redeploy | **nothing changed — and that's real** |
| faithfulness ladder (L1/L2/L3) | **how ready you are** / **what's in the way** |
| tolerance / quotient | **a difference I'm ignoring on purpose** |
| Episode / `LifecycleStore` / refactorlog | **this run** / **the history** / **the trail** |
| Gate / refusal | **paused** / **needs your okay** |
| declared‑loss | **approve what's removed** |
| eject / freeze | **hand it off for good** |
| canary | **self‑check** ("I checked myself") |
| drift | **what's different on the server** |

### 2.2 Words we never use on the surface

- **Drama:** *destroy(s), destroyed, fatal, abort, blast radius* (in the dig,
  "blast radius" is fine as a heading; on the surface it's "what this removes").
- **System‑shout:** `REFUSED`, `ERROR`, `FAILED` as a *lead* (a small verdict
  tag is fine; a sentence is better).
- **Jargon on top:** *δ, norm, torsor, quotient, idempotent, commuting square,
  isomorphism, surrogate, episode.* All real, all in the dig, none on the lead.
- **Mystical / cute:** *needs your nod, open a lane, the jewel,* anything that
  asks the reader to already share our metaphors.
- **Leaked internals:** `OS_KIND_*`, `OSUSR_*`, raw `SsKey` roots, file paths
  where a name belongs, exit codes on the lead line.
- **Negation‑as‑headline:** *"nothing destroyed"* → *"no removals"*; define by
  what *is*, not what isn't.

---

## 3 — The verdict line (the essence: *am I OK?*)

The first line of every surface answers the only three questions anyone ever
has: **Is it safe? Did it work? What do I do next?** There is a small, closed
set of verdicts. Pick one; never invent a fourth tone.

| Situation | Verdict line | Glyph |
|---|---|---|
| Safe, reversible change | **Safe to ship.** | `✓` |
| Safe, but one thing to look at | **Ready — one thing to check.** | `▲` |
| A removal awaits consent | **Paused — needs your okay.** | `▲` |
| Done and verified | **Done — and I checked it matches.** | `✓` |
| Idempotent no‑op | **Nothing to do — and that's real.** | `✓` |
| Real failure | **Stopped — here's what went wrong.** | `✕` |
| No difference at all | **No changes.** | `✓` |

The verdict line is *the* sentence the master reads as a heading and the
newcomer reads as reassurance. It carries no number it doesn't need and no word
it can't defend.

---

## 4 — The eleven moves (essence on top, proof in the dig)

Each move is rendered **essence‑first**: the plain line everyone reads, the proof
one dig beneath. The schema and data legs are the same shape one plane apart, so
their copy rhymes.

### Schema plane

**Add** — *a new thing, safely.*
- essence: `Added Country and 5 other tables.`
- dig: `new identity minted · nothing else touched`

**Remove** — *this deletes something; here's exactly what.*
- essence: `Removes the IX_Order_Stale index. Nothing else is lost.`
- dig: `what this removes · can't be auto‑undone · approve to continue`
- Destruction is never silent and never dramatic. We name the *one* thing, say
  whether data is affected, and hand over the approval.

**Rename** — *renamed, and your data stayed put.*
- essence: `Renamed OrderHeader to SalesOrder. Your data stayed put.`
- dig: `old → new · ‖rename‖_data = 0 · every page kept`
- The jewel (`‖rename‖_data = 0`) lives in the dig. The surface says the human
  truth: *nothing moved.* A rename that touched data would say so plainly —
  "this rebuilds the table" — because then it isn't really a rename.

**Reshape** — *changed a column; here's what and whether it's instant.*
- essence (widen): `Made Email longer (50 → 100). Instant.`
- essence (narrow): `Narrowing Status (50 → 20). I'll check your data first.`
- essence (unsafe): `Can't change Amount to a smaller type without losing data.`
- dig: `before → after · faithfulness class · data‑check before run`

### Data plane

**Insert** — *new rows came in.*
- essence: `Loaded 4,210 rows into Country.`
- dig: `+4,210 · exactly the rows that were new`

**Update** — *some rows changed.*
- essence: `Updated 312 rows — only the ones that actually differ.`
- dig: `312 captured · unchanged rows left alone`

**Unchanged** — (this is the *silence* — see §6, the idempotent proof). Never a
row‑by‑row line; the absence is the message.

**Delete** — *rows were removed.*
- essence: `Removed 18 rows that are no longer in your model.`
- dig: `−18 · scoped to <where> · approve to continue`

### Cross‑plane

**Reidentify** — *same records, re‑keyed for the new environment; all links kept.*
- essence: `Matched 138 of 142 records to UAT and kept every relationship.`
- dig: `re‑key is an update keyed on email · the FK re‑point trail`

**Move** — *moved your rows, in the right order.*
- essence: `Moved 4,210 rows from Dev to UAT, in dependency order.`
- dig: `the load plan · the two‑phase boundary · ‖move‖ = rows touched`

**Accumulate** — *here's what changed since last time.*
- essence: `12 changes since run 9. 2 new tables, 1 rename, the rest cosmetic.`
- dig: `the evolution chain · churn (work done) beside net (distance moved)`

---

## 5 — The gates (consent, in plain words)

A gate is the instrument **stopping to ask** before it writes. The copy law:
*say the danger as meaning, name the one lever, hand over the action, and wait.*
Never a wall of error; always a question a person can answer.

| Gate | The plain ask | The action |
|---|---|---|
| **Declared‑loss** (a drop / scoped delete) | `This removes the IX_Order_Stale index. No data is lost, but it can't be auto‑undone.` | `Approve what's removed, or stop.` |
| **Validate‑user‑map** (re‑key, pre‑write) | `3 of 142 users don't match UAT yet. I won't move a row until they're mapped.` | `Map them, use the system user, or stop.` |
| **Declare‑every‑drop** (publication) | `This changelog drops 2 things your SSIS team depends on.` | `Confirm each, so it replays cleanly downstream.` |
| **Intent‑filter‑complete** (idempotent) | `14 real changes. 3 cosmetic differences I'm ignoring on purpose — here they are.` | `Review the 3, or ship.` |
| **Data‑compat** (narrowing) | `Narrowing Status to 20 chars. 4 rows are longer than that today.` | `Trim them, widen the target, or stop.` |
| **Provenance‑completeness** (eject) | `Before I freeze this, the history has one gap: run 7 was never recorded.` | `Record it, or freeze with the gap noted.` |
| **Drift** (deployed differs) | `The server has 2 things your model doesn't.` | `Accept them, remediate, or escalate.` |
| **Self‑check** (canary) | `My round‑trip didn't come back identical — 1 difference.` | `Here's the difference; this blocks the commit.` |

The verb on the button is always **plain and active**: *Approve · Map · Confirm ·
Trim · Record · Accept · Remediate · Stop.* Never *Declare loss* (jargon), never
*Override* (reckless), never *Proceed (Y/N)* (lazy).

---

## 6 — The proofs (fidelity, made plain)

This is the soul. The deepest thing the engine knows about itself — that it is
*faithful* — is said to everyone as plain confidence, with the formal proof one
dig beneath. The newcomer reads the sentence and trusts; the master digs and
*verifies the trust*.

| Proof | The plain sentence | The dig |
|---|---|---|
| **Commuting square** (it matches) | `Done — and I checked: your database now matches your model.` | `residual ∅ · Ingest ∘ Project = id` |
| **CDC‑silence** (real no‑op) | `Nothing changed — and I made sure that's real, not a missed check.` | `CDC = 0 · zero ALTERs · the green hush` |
| **Rename fidelity** (data stayed) | `Your rename kept every row.` | `‖rename‖_data = 0` |
| **The ladder** (where am I) | `You're 7 of 10 green checks from cutover. The one thing in the way: a user‑fallback on 3 accounts.` | `schema ✓✓✓ · data ✓✓✓ · identity ✓✓▲` |
| **The intent filter** (honest diff) | `14 real changes; 3 cosmetic differences I'm ignoring on purpose — here they are.` | `each tolerance named · neither emitted nor dropped` |
| **Minimality** (the touch was small) | `I touched exactly the 312 rows that changed — nothing more.` | `‖δ‖ = 312 = CDC capture count` |

The rule across all of them: **the essence is the proof made kind; the proof is
the essence made rigorous.** No one is ever asked to take it on faith who would
rather see it.

---

## 7 — The nine workflows (the felt arc)

Each protein is the same rhythm — *Snapshot → Diff → Gate → realize → Measure →
Verify → Record* — differing in the change and the gate. The copy gives each a
**verdict** (what it leads with) and a **felt arc** (the emotional shape the
operator moves through). The arc is the design target; the words are
illustrative.

| Workflow | Leads with | Felt arc |
|---|---|---|
| **P‑1/2 · Load to on‑prem** | `Ready to load Dev → on‑prem.` | curious → clear → loaded → *"that was nice."* |
| **P‑3 · UAT with re‑key** | `Ready, once 3 users are mapped.` | careful → guided → mapped → confident |
| **P‑4 · SSIS publication** | `Here's the delta for your team — replays from genesis.` | responsible → precise → handed off |
| **P‑5 · Idempotent redeploy** | `Nothing to do — and that's real.` | reassured (the green hush) |
| **P‑6 · In‑place migrate** | `14 changes, 1 to confirm.` | alert → consenting → verified → trusting |
| **P‑7 · Eject / freeze** | `Frozen. Provenance complete — genesis to frozen, verified.` | deliberate → certain → *done, for good* |
| **P‑8 · Drift** | `The server differs in 2 spots.` | watchful → informed → decisive |
| **P‑9 · Self‑check canary** | `Fidelity holds — round‑trip is identical.` | quiet confidence (or one clear red) |

The storyboard test for all nine: a developer on her *first* run and her
*hundredth* read the **same verdict line** — the first as reassurance, the
hundredth as a glance she's already past.

---

## 8 — The timeline & the ladder (*where am I?*)

**The timeline** is the schema's whole life as one calm shape — runs as dots, the
present marked, the streak filling toward cutover. The copy under it is a single
plain line:

```
  ●●●●✕●●●●●●●▸          Cutover  ███████░░░  7 of 10
  └ 11 runs · run 5 was red · you're here · 3 green to go
```

- Past runs: dots. A red one is named, never hidden: `run 5 was red`.
- The present: `you're here` (not "cursor", not "HEAD").
- The gate distance: `3 green to go` — the streak read as *distance to the
  finish*, the most motivating possible framing.

**The ladder** answers "where am I, and what's the one thing in the way." It
always names a **single lever** — the most honest, least overwhelming thing we
can do:

```
  Cutover     ███████░░░   7 of 10 green     one thing in the way ↓
       The lever: a user‑fallback on 3 accounts.
```

Never a list of ten problems. The one lever, named, with the next move.

---

## 9 — Affordances & navigation (the small words)

The microcopy of *moving* through the instrument. These are tiny and they are
where cuteness creeps in — hold the line.

- **Disclosure (a collapsed node):** the affordance reads `Show details` or a
  bare `▸ 3 more`. Never "dig", never "open a lane", never "expand the node."
  The triangle does most of the work; the words just say how much is inside.
- **Depth:** `Show more (→)` / `Less (←)`. The operator opens one level at a
  time. We never dump the tree; we never say "level 3 of 5."
- **Walking time:** `← earlier · later →` (not "prev/next run", not "−1/+1").
- **The next action** is always a plain imperative naming the *thing*: `Map the
  3 users` · `Approve the index drop` · `Run the migration` · `Trim 4 rows`.
  Never `Continue?`, never `[Y/n]`, never `Press any key`.
- **Filtering / search:** `Show only removals` · `Find a table` (not "filter by
  axis", not "grep by SsKey").

---

## 10 — Refusals & errors (saying *no* kindly)

When the instrument can't do something, it still speaks plainly and still hands
over a move. The shape: **what happened (concrete) · why (plain) · what you can
do (active).** Never a stack trace on the surface; never blame.

| Situation | What we say |
|---|---|
| Can't reach the server | `Can't reach UAT. Check the connection and try again.` |
| Missing permission | `I don't have permission to alter dbo.Order. Ask for ALTER, then retry.` |
| Invalid input/model | `That model won't load — line 12 is missing a name. Fix it and rerun.` |
| Couldn't verify | `It ran, but I couldn't read it back to confirm. Re‑run the check when the server's reachable.` |
| Self‑check failed | `My round‑trip came back with 1 difference. Here it is — this needs a look before you ship.` |

The exit code, the gate label, the stack — all real, all in the dig (`exit 9 ·
gate=connection‑unavailable`), none on the lead. A person who reads only the
first line still knows exactly what to do.

---

## 11 — Tone calibration (before → after)

The fastest way to internalize the voice: see it correct itself. Left is the
failure mode; right is the gold standard.

| ✗ Off | ✓ On |
|---|---|
| `3 changes · 3 destroy structure — review first` | `3 changes · 3 removals · review before applying` |
| `· OS_KIND_Country` | `· Country` |
| `‖δ‖ norm  ▲ 3` | `Total changes  3` |
| `migrate refused — undeclared destructive change · exit 9` | `Can't migrate yet — this removes a database index. Approve to continue.` |
| `declare the loss to proceed: --declare-loss` | `To continue, approve what's removed.` |
| `nothing destroyed` | `no removals` |
| `CDC silent; idempotent redeploy confirmed` | `Nothing changed — and that's real.` |
| `7/10 L1‑L3 axes green; identity axis at L2` | `7 of 10 checks green. One thing in the way: 3 accounts to map.` |
| `needs your nod` / `open a lane` | `needs your okay` / `Show details` |
| `Operation completed successfully.` | `Done — and I checked it matches.` |

---

## 12 — How to use this doc

- **Building a surface?** Find its move / verb / gate / proof above and *derive*
  the copy from the example string — keep the register, fit the specifics. Don't
  invent a new tone.
- **Writing a new string?** Run the eight principles (§1) and the banned list
  (§2.2) over it before it lands. If it fails one, it isn't done.
- **Unsure of a word?** Reach for the lexicon (§2.1). If the operator's word
  isn't there yet, add it here first, then use it — this doc is the source of
  the ubiquitous *operator* language, the plain‑side sibling of the engine's
  ubiquitous language.
- **A surface we haven't imagined yet?** The principles still hold. Write the
  felt sense; precision can come when the surface does. That's what an anchor is
  for.

*The instrument disappears. What remains is the schema's truth and the
operator's hand on it — and the words are how it feels like theirs.*
