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
- **POPULATED table (violations present OR zero violations — it makes no difference)** → the change
  **cannot pass the prod-strict gate by backfill/reconcile alone.** It requires a **conscious,
  documented decision taken AFTER a verified probe** (prove the violation count is 0 first —
  necessary, not sufficient), then ONE of:
  - **(a) Targeted gate-relaxation.** Having *proven* zero violations remain, deliberately disable
    `BlockOnPossibleDataLoss` for **this one targeted change** (a scoped publish-profile override or
    a script-only path) so the tightening `ALTER` proceeds against the now-clean column. This
    **ships as a scripted change** — the data-loss guard is relaxed for this one change, which
    cannot be expressed as a table definition. The proof packet must carry **both** the
    zero-violation probe **AND** the explicit record of the relaxation decision.
  - **(b) Restructure across releases.** Stage it so the engine never has to relax its guard (add
    the tightened column in a new structure and migrate, or sequence the tightening across releases
    where the model state the engine diffs no longer trips the table-has-rows guard). This **ships
    across multiple releases** so the running application keeps working while the change is in
    flight, each release its own pull request.

  Either way, **a dev lead must review this: existing data is modified.** Added scrutiny raises that
  bar — this table is large enough that the change may block writes or run long at production row
  counts, or this is the first time the operation has been done on this estate.

## How the per-op specifics differ (they still point here)

The **guard mechanics above are identical** for every member — that is the whole point of lifting
them. What each op still owns in its own SKILL:

- **make-mandatory** — the probe is `COUNT(*) WHERE col IS NULL`; the trap is trusting a clean NULL
  probe as a green light.
- **narrow** — the probe is `MAX(LEN(col))` + `COUNT(*) WHERE LEN(col) > <new>`; `MAX(LEN)` fitting
  decides gate-relaxation vs. multi-phase for the *remedy*, but never buys a clean single-phase on
  a populated table.
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

## Handbook

Cite by **filename** (offset +3): handbook **16** (= §19) for the Optimistic NOT NULL / Ambitious
Narrowing anti-patterns, and **10-SSDT-Deployment-Safety.md** for the `BlockOnPossibleDataLoss`
gate semantics.
