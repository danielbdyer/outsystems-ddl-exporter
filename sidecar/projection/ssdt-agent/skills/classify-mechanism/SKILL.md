---
name: classify-mechanism
description: Use after confirm-intent has named the operation and gathered the four state-variables, to state PROVISIONALLY how a change ships and who must review it, before proving. Walks the handbook decision cascade (file 15 = §18.1, Q1-Q4), adds the standing-risk escalations (CDC / >1M rows / first-time op), keeps how-it-ships and who-reviews as independent findings, and decides WHEN a change can be classified on sight versus when it MUST be proven on a disposable copy of Dev. Emits a provisional pair of findings that prove-on-dacpac confirms or flips.
---

# Classify mechanism

> **Why this matters.** The findings produced here are **provisional on purpose** — classifying
> from the `.sql` text alone is the named failure mode, and the data holds the casting vote. How a
> change ships and who must review it are kept as **two independent findings** because they are
> genuinely independent: a `DROP TABLE` ships as a single scripted change, yet a principal must
> review it because the data is gone irreversibly. Never fold the review level into how simply the
> change ships, and never treat a from-text reading as final past the purely-additive corner — the
> test is "would this same `.sql` edit get the same answer regardless of the rows?" If it would, the
> reading is a guess and the data still has to decide. Surface this to the developer: once a
> developer sees that the shipping shape can *change on data they didn't mention*, they stop
> trusting the schema diff alone — that is the shift from reading SQL to proving it.

You are helping an **OutSystems-native developer** land a safe schema change. This skill turns a
named operation + its data-state into two independent findings — **how** the change ships and
**who must review it, and why** — and, just as importantly, decides whether the answer can be given
on sight or must be proven on a disposable copy of Dev.

**Classification from the `.sql` text alone is a guess. The data decides how the change ships.**
This skill produces *provisional* findings; `prove-on-dacpac` is what confirms them. Never deliver a
provisional finding to the developer as if it were proven.

## How it ships and who reviews are independent — state BOTH, always

- **How it ships** = the release shape. Five shapes, below.
- **Who must review, and why** = the review level, with added-scrutiny escalations. Four levels,
  below.

These are not the same finding. **How simply a change ships is not who must review it.** A
`DROP TABLE` on populated data ships as a single scripted change (one release), but a principal must
review it because the data is gone irreversibly. Keep the two findings separate, or a change that
removes data gets under-reviewed on the strength of how simply it ships.

## The five shipping shapes (the team's release vocabulary)

| Shape | Release bucket | What it is |
|---|---|---|
| Pure declarative | single-phase | Edit the CREATE; SSDT computes a safe in-place delta. No script, no data touched. |
| Declarative + post-deploy | single-PR | Declarative schema change **plus** an idempotent post-deploy script (seed/backfill that runs after the schema lands). |
| Pre-deploy + declarative | single-PR | A pre-deploy script **first** (backfill / dedupe / reconcile) so the declarative change then lands instead of being blocked. |
| Script-only | single-PR (operational) | The change is not expressible in the declarative model (CDC, `NOCHECK`->`WITH CHECK CHECK`, table-swap). Lives in a script. |
| Multi-phase | multi-PR | The change spans **multiple releases** because old and new app code must coexist, or a no-gap CDC rollout forces staging. |

The plain "how it ships" statement each shape surfaces on the record is in `THE_RECORD.md` §5 — the
shape name is internal; the §5 sentence is what a reviewer reads.

**Shape -> release-bucket map** (the team's release vocabulary):

- **SINGLE-PHASE** = pure declarative: no script, one publish.
- **SINGLE-PR** = declarative + post-deploy and/or pre-deploy + declarative, or operational
  script-only: one release, scripts included.
- **MULTI-PR** = multi-phase: staged across releases.

## The review-level decision cascade (handbook file 15 = §18.1, Q1-Q4)

Walk these in order. The **first** question that fires sets the review floor; nothing after it can
lower the reviewer's seniority, only raise it.

- **Q1 — Will data be lost?** (drop table/column with data, narrowing truncation, a rename with no
  refactorlog entry)
  -> **a principal must review this: data is removed and the removal cannot be undone.** Stop here
  for the floor; this is the irreversible band.
- **Q2 — Will existing data change or move?** (backfill, retype, value migration)
  -> **a dev lead must review this: existing data is modified** — and staging across releases
  (MULTI-PR) is on the table.
- **Q3 — Cross-table or external dependencies?** (FK, view, proc, ETL, reports, External Entities)
  -> **a dev lead must review this: a cross-table relationship is added.**
- **Q4 — Can the app keep working unchanged?**
  - **NO** (the running OutSystems app breaks without a code change) -> **a dev lead or an
    experienced developer should review this: the running application must change to keep working**
    — ships as a single release (SINGLE-PR).
  - **YES** (additive, app oblivious) -> **any team member can review this: the change is additive
    and the running application is unaffected** — ships in place (SINGLE-PHASE).

Then apply the escalations.

## The added-scrutiny escalations (apply after the cascade)

Each adds a standing-risk line to the review finding (they stack):

- **CDC-enabled table.** Added scrutiny: this table feeds a change-data-capture stream, so the
  capture instance is frozen to the table's current columns and needs handling — and every
  subsequent schema change carries the same obligation (**CDC Surprise**).
- **> 1,000,000 rows.** Added scrutiny: at production row counts this change may block writes or run
  long — build time, blocking, memory grants; schedule a window.
- **First-time operation** for this team/codebase. Added scrutiny: this operation has not been
  performed on this estate before — no muscle memory, no prior proof.

The most senior reviewer is a principal; once a principal must review (data removed irreversibly),
the added-scrutiny lines only sharpen the warning (a CDC stream, for instance, makes the rollback
harder still).

> **Why this matters.** The added-scrutiny lines exist because some risks are *standing*, not
> visible in the single delta: a CDC-enabled table carries an obligation on **every future change**
> (the capture instance must be managed), scale turns a metadata edit into a blocking build, and a
> first-time op has no prior proof to lean on. The review level of a change is not only what *this*
> edit does to the data — it is what the table's *context* makes every edit cost. Surface this: a
> developer who hears "the base operation is trivial, but this table feeds a CDC stream, so every
> change to it carries the capture obligation" stops being ambushed months later when a new column
> silently vanishes from the feed.

## The four state-variable flips

The **same operation** changes how it ships as the data crosses a threshold. This is the
classify-by-proving core. For any operation, the flip table is:

| State of the data | How it ships |
|---|---|
| table **empty** | pure declarative, in place (single-phase) |
| populated, **data does NOT violate** the new rule | still in place (but **prove it** — see below) |
| populated, **data violates** the new rule (NULLs / orphans / over-length / dups) | pre-deploy + declarative (backfill/reconcile first), single-PR — or declarative + post-deploy if the fix is a post-deploy seed |
| + **CDC-enabled, no-gap required** | multi-phase, multi-PR; added scrutiny for the CDC stream |
| + **> 1M rows / first-time op** | added scrutiny (the shape may also stage across releases) |
| change **not expressible declaratively** (CDC enable, FK NOCHECK reconcile, IDENTITY swap) | script-only |
| **old + new app code must coexist** | multi-phase, multi-PR |

The per-operation `skills/operations/*.md` entry carries the **specific** flip table and which seed
scenario exercises it. Read it.

## WHEN to classify on sight vs WHEN you MUST prove

This is the judgment call this skill exists to make.

**You may classify on sight (no proving needed) only when ALL of these hold:**

- The operation is **purely additive** and the app is oblivious (Q4 = YES), AND
- The change touches **no existing data** (a new nullable column, a brand-new table, a new index
  on a new column), AND
- The table is **not CDC-enabled** and **not** above the scale/first-time thresholds.

That is the additive, in-place, any-reviewer corner, and only that corner. Examples:
`add-attribute-optional` on a non-CDC table; `create-entity`; a post-deploy `MERGE` that adds a
genuinely new lookup value.

**You MUST prove (hand to `prove-on-dacpac`) whenever ANY of these is true** — which is
*everything past the additive corner*:

- The operation could touch, move, or refuse existing data: **make-mandatory, narrow, retype,
  create-FK, add-unique, add-check, define-PK, delete-anything, rename-anything.**
- **Any** of the four state-variables is `unknown` (and for the data-violation variable it is
  *almost always* unknown — the developer's recollection is not proof).
- A **named trap** is in play (a rename with no refactorlog entry, Optimistic NOT NULL, Ambitious
  Narrowing, Forgotten FK Check, CDC Surprise). The delta is the only place to catch these reliably.
- The table is **CDC-enabled** or above the **scale/first-time** thresholds.

The discriminator that makes this real: **make-mandatory on a table with NULLs vs without NULLs is
identical `.sql` text but a different shipping shape.** If both get the same answer from the text,
the text alone has classified nothing — that reading is a guess. Prove it.

> **Why this matters.** Note the *honest correction* the disposable copy forces here: for
> `make-mandatory` on a **populated** table, even *without* NULLs the change does **not** pass the
> prod-strict gate cleanly — SSDT's guard is **table-has-rows, not column-has-NULLs** (see
> `prove-on-dacpac`). So the on-sight corner is genuinely only the purely-additive case; the moment
> rows exist *and* a constraint tightens, proving is required. The "answer-on-sight" zone is much
> smaller than intuition suggests — when in doubt, the cost of one publish is far less than the cost
> of a wrong classification. Surface this to the developer so they understand *why* something that
> "looks obvious" is being proven.

## What you emit (the provisional findings)

A structured handoff to `prove-on-dacpac`:

- **operation** (from the change-order).
- **provisional how it ships** — the release shape in plain words (in place / post-deploy script /
  pre-deploy script first / scripted / staged across releases), and its release bucket.
- **provisional who must review, and why** — the review level in plain words, with each
  added-scrutiny line (CDC / large table / first-time) named and why.
- **cascade trace**: which of Q1-Q4 fired and set the floor.
- **which state-variable flips the answer**: the one that, if it crosses, changes how the change
  ships — and the exact thing the disposable copy must check (e.g. "COUNT of NULL Email; the Strict
  gate must refuse if > 0").
- **on-sight vs must-prove**: the decision and the reason. If on-sight, why proving is unnecessary;
  if must-prove, the proof to demand.
- **named trap, if suspected**, so the delta gets read for it.

Label the whole handoff **PROVISIONAL**. It becomes a confirmed finding only after
`prove-on-dacpac` returns.

## Worked examples

- **"Make Customer.Email required."** Operation `make-mandatory`. Q4 = NO (the app may write NULLs
  today) -> a dev lead or an experienced developer should review this: the running application must
  change to keep working. Data-violation variable unknown -> **must prove**. Provisional shipping
  shape: a single in-place schema change *if* the table is truly empty; but on a **populated** table
  it does **not** land clean even with zero NULLs — SSDT's guard is table-has-rows, not
  column-has-NULLs — so SSDT refuses under the Strict (prod) gate, and shipping needs a conscious
  call: relax the data-loss guard for this one column after proving zero blanks remain, or stage it
  across two releases. Proof to demand: `COUNT(*) WHERE Email IS NULL`; then prove SSDT STILL refuses
  after the backfill clears the blanks.
- **"Add an optional Notes field to Customer"** (non-CDC, small). Purely additive, the app is
  oblivious, no existing data touched -> **classify on sight**: ships as a single in-place schema
  change with no data read or written, and any team member can review it. It still goes through
  `prove-on-dacpac` for a clean-publish confirmation, but no flip is possible. (If the same table
  fed a CDC stream, added scrutiny applies — the capture instance is frozen to the table's current
  columns and must be recreated, so a trivial base op hides a non-trivial CDC obligation. Prove it.)
- **"Drop the AuditLog table"** (populated, maybe CDC). Q1 = YES (data is removed) -> a principal
  must review this: data is removed and the removal cannot be undone; the floor is set immediately.
  A CDC stream on the table does not raise the review level further but hardens the warning — the
  rollback is harder still. It ships as a single scripted change: how simply it ships is not who
  must review it. Must prove: SSDT's refusal under `BlockOnPossibleDataLoss` is the safety proof;
  sequencing is disable-CDC-first, drop-FKs-first.
- **"Add a FK from Order.CustomerId to Customer"** (orphans unknown). Q3 = YES (a cross-table
  relationship is added) -> a dev lead must review this. Must prove. Provisional shipping shape: a
  single in-place schema change *if* there are no orphans; flips to a scripted change (`NOCHECK` ->
  reconcile -> `WITH CHECK CHECK`) *if* orphans exist. **Forgotten FK Check** suspected.

## Connector points

- The two findings — how it ships and who must review — are the **Review & release** section of the
  pull request (`THE_RECORD.md` §5). `change-author`'s review packet carries them to `reviewer`, and
  `author-pr` renders them in the record register. See `CONNECTORS.md`.
- `.claude/skills/`-shaped and Copilot-mappable (format verify-first); see `CONNECTORS.md`.
