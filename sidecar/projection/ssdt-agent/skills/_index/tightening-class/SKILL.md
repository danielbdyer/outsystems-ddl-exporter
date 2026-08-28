---
name: tightening-class
description: Cross-cutting KNOWLEDGE shared by make-mandatory, narrow, and delete-attribute (and the row-presence refusal of add-check / add-unique / define-PK on a populated table). Owns the proven, DATA-BLIND BlockOnPossibleDataLoss guard — `IF EXISTS (SELECT TOP 1 1 FROM <t>) RAISERROR(...,16,127)` placed ABOVE the ALTER — that blocks on TABLE-HAS-ROWS, not on whether the column satisfies the new rule. Per-op skills POINT here instead of re-deriving the guard. Not a capability skill; the publish loop that PROVES this lives in prove-on-dacpac, the probes in talk-to-local-sql.
---

# The tightening class — the guard is TABLE-HAS-ROWS, not column-satisfies-rule

> **The data-loss guard checks whether the table holds rows, not whether the column satisfies the
> new rule.** The older advice — backfill the NULLs, then the declarative NOT NULL lands clean under
> Strict — was **disproven on a disposable copy of Dev**: a cleared NULL count does not clear the
> block while the table holds rows. Every op in this class points here, so the guard is stated once
> and not re-derived op by op.

You are helping an **OutSystems-native developer** land a safe schema change. When they *tighten*
a rule on an existing column — make it mandatory, shorten it, drop it — the same instinct applies,
because SSDT guards all of them the same conservative way. Learn the class once; stop
re-discovering the same block op by op.

## The members of the class

A **tightening** change is one that removes capacity or removes a column from a populated table.
The members that share this exact guard:

- **make-mandatory** (`NULL` → `NOT NULL`) — the case this guard was first proven on.
- **narrow** (`NVARCHAR(50)` → `NVARCHAR(10)`, reduced precision) — the handbook's *Ambitious
  Narrowing* case.
- **delete-attribute** (`ALTER TABLE ... DROP COLUMN`) — the values are irrecoverable.

Plus the **row-presence refusal** of the constraint ops — **add-check**, **add-unique**,
**define-PK** — insofar as they refuse on a populated table. Those ops *also* block on an actual
data **violation** (a duplicate, an orphan, a failing predicate); that violation face is a
*different* concern owned by `../constraint-is-a-claim/SKILL.md`. Read the distinction at the
bottom of this file — it is deliberate and load-bearing.

## The guard, verbatim (verified on a disposable copy of Dev)

For a `NULL → NOT NULL` change (and structurally identical for a narrow), `sqlpackage` generates:

```sql
IF EXISTS (SELECT TOP 1 1 FROM [dbo].[Customer])
    RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127);
-- ... and BELOW it, the actual:
ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR(256) NOT NULL;
```

*(As emitted by sqlpackage 170.4.83.3 the guard reads lowercase — `select top 1 1 from
[dbo].[Customer]` — and carries `WITH NOWAIT`; a blocked publish surfaces it as `Error SQL72014` /
`Msg 50000, Level 16, State 127`. The shape above is normalized for reading; the behavior is
identical.)*

What the guard is: it fires on `IF EXISTS (SELECT TOP 1 1 FROM <table>)` — **the table merely having
a row** — and it is placed **before** the `ALTER`. **It never inspects the column at all.** It does
not count NULLs. It does not measure `MAX(LEN)`. It is **data-blind**: row-presence, not
rule-satisfaction.

## Why the guard is conservative (specialize this per op; do not restate the whole thing there)

SSDT computes the **entire deploy script once, up front, from the pre-publish model state**, and
is **conservative by design**. It cannot know that a pre-deploy backfill — which runs *at deploy
time, after the script is already generated* — will have emptied the NULLs, or that every value
already fits the narrower type. So it refuses the moment the table holds any row. **The gate cannot
know the change's intent, so it assumes the worst.**

The empirical proof: a pre-deploy backfill cleared **every** NULL
(`SELECT COUNT(*) WHERE Email IS NULL` returned **0**), and Strict **still blocked the change**,
leaving the column nullable. The narrow case confirmed it: `MAX(LEN)` fitting the new size did
**not** clear the block either. **Zero violations is necessary but NOT sufficient** on a populated
table.

And the remedy must be **durable at source**: a post-deployment script that still writes violations
into the tightened column fails *after* the `ALTER` lands (`Msg 515` — the publish is not atomic
across the schema transaction and the post-deployment script), so the corrected seed or script is
part of the change set, not an afterthought. Proven live; the captured run is
`../../../self-test/golden/make-mandatory-pr.md`.

## The ladder (empty = clean; populated = a conscious call)

- **EMPTY table** → the `IF EXISTS` is false, the `RAISERROR` never fires, and the tightening
  `ALTER` lands. *(Confirm the table is genuinely empty first.)* This ships as a single schema
  change, applied in place — the **only** clean single-phase leg, and the lightest to review, since
  an empty table puts no data at risk.
- **POPULATED table (violations present OR zero violations — it makes no difference)** → the
  tightening `ALTER` trips the row-presence guard, and this estate's pipeline (Azure DevOps →
  Octopus, dacpac) **cannot relax `BlockOnPossibleDataLoss` for one deploy**
  (`../../../FINDINGS_AND_CHANGES.md` Part 1 — the locked-gate axiom). So it ships as **two
  releases** — the pattern proven live on this branch (F4 narrow, F7 make-mandatory):
  - **Release 1** — a one-time pre-deploy script reconciles the data (backfill the NULLs; shorten
    the over-length values) and runs the tightening `ALTER` itself, **with the model left at the
    old shape**, so DacFx generates no data-loss step and the row-presence guard never fires.
    Idempotent and safe over a partial state (F6).
  - **Release 2** — the model catches up to the new shape. The database is already tightened, so
    DacFx sees model = database and generates nothing.

  Never combine the two (F2 — a model that tightens in the same release the pre-deploy tightens
  still trips the guard AND half-applies). Release 1's own `ALTER` fails `Msg 515` if a NULL or an
  over-length value remains, so the reconcile is part of Release 1, not an afterthought. Name both
  releases in the pull request. **And hold every other publish to that environment between
  Release 1 and Release 2.** The model deliberately lags in that window, so ANY publish that
  carries it — a second developer shipping an unrelated change included — regenerates the old
  shape and reverts the tightening, with every check green. The revert was captured live on a
  disposable copy (the drop face: one lagging-model publish re-created the dropped column,
  backfilled from its default — `../../../sample-prs/compound/extract-to-lookup-program.md`).
  The pull request's *Before promoting* section carries the hold as an imperative, and the
  in-flight ledger row (`../../../estate/in-flight.md`) is the register the hold is checked
  against. The full graph is `../../../THE_DECISION_TREE.md`'s S5 SHIP
  sub-machine; the concrete narrow and make-mandatory shapes are in `FINDINGS_AND_CHANGES.md`
  Part 4.

  **A dev lead must review this: existing data is modified.** Added scrutiny raises that bar — this
  table is large enough that the change may block writes or run long at production row counts, or
  this is the first time the operation has been done on this estate.

## How the per-op specifics differ (they still point here)

The **guard mechanics above are identical** for every member — that is the whole point of lifting
them. What each op still owns in its own SKILL:

- **make-mandatory** — the probe is `COUNT(*) WHERE col IS NULL`; the trap is trusting a clean NULL
  probe as a green light.
- **narrow** — the probe is `MAX(LEN(col))` + `COUNT(*) WHERE LEN(col) > <new>`; `MAX(LEN)` already
  fitting means Release 1's reconcile shortens nothing, but it never buys a clean single-phase on a
  populated table — the row-presence guard still forces the two releases.
- **delete-attribute** — the values are irrecoverable; a principal must review this, since data is
  removed and the removal cannot be undone even when the drop is mechanically one statement; the
  4-phase deprecation is its multi-phase shape (see `../multi-phase/SKILL.md`).

## Prove it (pointer, not a re-scaffold)

For the publish loop that PROVES this — build the dacpac, Strict-publish, read the generated delta,
confirm the deployment is blocked, then prove the chosen remedy lands the tightening — see
`../../prove-on-dacpac/SKILL.md`. For the probes that PREDICT the block
(`COUNT(*) WHERE col IS NULL`, `MAX(LEN)`), see `../../talk-to-local-sql/SKILL.md`. The probe
predicts; the Strict publish proves; **the guard is row-presence regardless of what the probe
returns.**

## NOT the same as constraint-is-a-claim (keep them separate)

`../constraint-is-a-claim/SKILL.md` **blocks on an actual data VIOLATION** — a value that breaks
the rule (a duplicate, an orphan, a failing predicate). **This** class **blocks DATA-BLIND on row
PRESENCE** — the guard never looks at a value. Collapsing the two would re-lose the exact
distinction the disposable-copy runs exist to teach. When a constraint op refuses on a *populated
but clean* table, that is this class; when it refuses on *dirty data*, that is the claim.

And not the same as **Optimistic NOT NULL on a NEW column** (`add-mandatory` / `audit-columns`):
that block is a value-needed refusal the remedy **cures** — an explicit `DEFAULT` on a **new** NOT
NULL column stamps every existing row as the column lands, and a populated table applies clean
(proven: `../../../sample-prs/add-mandatory.md`; contrast `../../../sample-prs/add-default.md`, where a
default on an **existing** column never backfills). This class's row-presence guard clears for no
remedy short of the two-release restructure — this estate cannot relax the gate (Part 1). The
discriminator is one sentence: **if a DEFAULT can fix it, it is not the tightening class.**

## Handbook

Cite by **filename** (offset +3): handbook **16** (= §19) for the Optimistic NOT NULL / Ambitious
Narrowing anti-patterns, and **10-SSDT-Deployment-Safety.md** for the `BlockOnPossibleDataLoss`
gate semantics.
