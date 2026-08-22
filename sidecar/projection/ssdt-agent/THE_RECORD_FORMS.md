# THE RECORD FORMS — how every word in a PR is written

`THE_RECORD.md` says a PR is a record, not a chat, and bans the worst habits. This says what a
good record *looks like*, positively, so the shape is the only thing an author can produce.
`THE_DECISION_TREE.md` is the steps; this is the words. Read all three before rewriting a skill
file.

The reader is a reviewer on a global team — Pune, Porto, Portland — with strong SQL Server
knowledge but no time and no patience for a story. Write for that reader.

---

## The register in nine rules

1. **The developer is the author; the agent is invisible.** Never write a sentence whose
   subject is the agent. No "I", no "we", no "the agent". The PR is the developer speaking to
   the reviewer.
2. **State the fact, not a pointer to it.** Name the count, the object, the message. Never
   "the precedent", "this operation", "the prior run" — those point at a fact instead of
   stating it. If the fact is worth including, include the fact.
3. **Plain global English.** Short words, short sentences. Explain each SQL Server term once,
   in plain English, the first time it appears. No idioms — an idiom does not translate.
4. **Direct the reviewer with plain imperatives.** `Confirm the changed values in each
   environment.` `Schedule a maintenance window.` `Check with the app owner.` This is the one
   place second person is right — the developer telling the reviewer the next move.
5. **Lead with the conclusion, then explain.** `1 of 5 rows is too long: Product 3,
   "STANDARD-SKU-001", 16 characters.` The headline is the finding; the colon opens the proof.
6. **Name the risk directly.** Not "the step handles values wherever they are" — say "other
   environments may hold more too-long rows than the one here; the step shortens all of them."
7. **No trap-names, no taxonomy.** Never "Ambitious Narrowing", "Optimistic NOT NULL", or any
   curriculum label. Those teach; a record reports. State what the data does.
8. **No trivial negatives.** Do not deny something no one expected ("no new table is added").
   State a negative only when a reader would otherwise assume the opposite.
9. **Bullets over prose; short.** A PR is an outline a reviewer scans, not an essay. Cut every
   sentence that does not carry a fact the reviewer's decision needs.

---

## The one test every sentence passes

**Point at a referent, or cut it.** Every sentence must name at least one thing a reviewer
could check against a real source — the database, the generated difference, the project files:

- a **count** (`1 row`, `16 characters`, `0 rows`),
- an **object** (`dbo.Product`, `Code`, `Order 4`, `FK_Order_Customer_CustomerId`),
- a **type or state** (`NVARCHAR(10)`, `is_not_trusted = 0`),
- a **message**, word for word (`Msg 547 …`), or the developer's own words in quotes,
- a **thing the schema does** (`shortens`, `blocks`, `validates`, `removes`).

A sentence that only characterises, teaches, or reassures points at nothing and is cut. Test:
*name the value a reviewer could check, and where.* If you cannot, the sentence is a story.

*Worked example.* "the data decides how it ships" points at nothing a reviewer can open, and
personifies an abstraction — cut it. The fact underneath is checkable, so state that: **"the
existing rows determine how it ships"** (the rows are the referent), or the imperative **"prove
before you classify"** (the action that settles it). The same test retires any "X decides" phrasing.

---

## The verdict

One line, first. It answers: what does this do, and what must someone confirm before it moves
up? Driven by the risk row in `THE_DECISION_TREE.md` Node 4.

- Form: `<what it does>. <the call to action>. <the one blocker, or nothing>.`
- Example (data-change): `This PR shortens Product.Code to 10 characters and rewrites one
  existing value to do so. Confirm the shortened value is correct in each environment before
  promoting upward. (Blocker: not yet proven on a copy.)`
- No slot for: a role assignment ("a dev lead must review" — every PR is reviewed, so it says
  nothing), an adjective on the change, or more than one blocker.

---

## The sections (what each one holds, in plain register)

The fixed spine is in `THE_DECISION_TREE.md`. Each section, in words:

- **Intent** — `The developer's stated intent for this PBI:` then a paraphrase, with a direct
  quote for the one crucial constraint. Report the intent; do not embellish it.
- **What changes** — `<object>: <from> → <to>`. One line per real change.
- **Before promoting** — the confirmations, per environment, as imperatives. This is the risk
  made concrete: what to run, what to check, who to ask, before it moves up a level.
- **The data** — the counts and the bad rows, named, headline-colon-detail. Nothing else.
- **How it ships** — the non-routine mechanics at the **developer's** level: what happens and what
  to do. Keep it simple — the deploy engine's internals (the generated `WITH NOCHECK ADD` /
  `WITH CHECK CHECK`, the exact statements) are *evidence* and belong in *What proving showed*, not
  here. Surface a mechanic to the developer only when it is genuinely inherent and they must act on it
  (reconcile the orphan first, or the two releases of a data-loss change); anything the pipeline is
  configured to handle once is invisible here (`FINDINGS_AND_CHANGES.md` Part 5, *simple by default*).
  This estate cannot toggle the gate, so a data-loss change ships as a **two-release** pattern, not a
  gate relaxation. The **S5 SHIP sub-machine** in `THE_DECISION_TREE.md` decides the shape; the
  proofs are in `FINDINGS_AND_CHANGES.md`.
- **What proving showed** — the `Tried / Did / Realized` sequence, on this branch, with the
  real messages. Never a prior run. This is the heart: it shows the reviewer the change was
  published to a copy and what the database actually did.
- **After deploy — check** — the queries, each with `-- expect <result>`. No prose around them.
- **How to roll this back** — the reverse steps, and plainly what is *not* undone automatically.
- **Not checked / still open** — the honest limits, one bullet each, and any open fork. Never
  empty: a copy always has limits a live environment does not.

---

## The fork, and the sacred schema

A remedy prepares data or stages across releases (this estate cannot relax the gate). **It never
adds a permanent table, column, or constraint** — that is a separate product decision with its
own PR and its own review. When a fix seems to need new schema, that is the signal to stop and
ask, not to build — pose it as a fork (`skills/ask-the-developer`); the sacred-schema guard the
authoring machine enforces is `THE_DECISION_TREE.md` S5/S6 (a remedy that would add persistent schema
routes to FORK).

A fork the proof surfaces — an orphan to delete or reassign, a value to truncate — is the
developer's to decide. Pose one question: the measured fact, 2–4 options each with its
consequence and cost and a schema line, and a custom slot. Record the answer as one line. **An open
fork does not hold the PR — emit-and-flag:** the record is emitted with the question named in *Not
checked / still open* and the confirmation it forces in *Before promoting*, carrying no invented
schema. It is resolved in review, before promotion, never silently by the agent
(`THE_DECISION_TREE.md` S6).

---

## How the standard holds (positively, not by a ban list)

A banned-word list lags the next clever phrase — it once *licensed* "Ambitious Narrowing" on a
record. The standard holds three ways instead, and a record is admitted only when it shows the
shape:

- **Golden PRs.** Four worked examples — one clean apply, one data-blocked change, one open
  fork, one refusal — each captured from a real run, cut to the bone, annotated to its forms,
  and paired with a *wrong* twin that keeps every fact right and breaks only the register. The
  twin is a permanent test: it proves the checks catch what a word-scan let through.
- **A positive gate.** A check that asserts the shape is *present*: the spine in order, a
  finding-first line under each heading, every "before promoting" bullet carrying a real
  referent, "Not checked" non-empty, the proof carrying real messages. The finding is the
  shape's *absence*.
- **A reader.** One bounded judge scores each PR against its golden — same op, different data —
  and cuts the sentences that characterise instead of denote. It cannot be passed by copying a
  golden's words, because the data differs.

**The rule that keeps it honest:** no standard here lands without the positive check that
proves it is present — not merely the absence of its violation. A rule enforced only by a
banned word is not yet enforced.
