---
name: tightening-class
description: Cross-cutting KNOWLEDGE shared by make-mandatory, narrow, and delete-attribute (and the populated-veto face of add-check / add-unique / define-PK). Owns the proven, DATA-BLIND BlockOnPossibleDataLoss guard — `IF EXISTS (SELECT TOP 1 1 FROM <t>) RAISERROR(...,16,127)` placed ABOVE the ALTER — that vetoes on TABLE-HAS-ROWS, not on whether the column satisfies the new rule. Per-op skills POINT here instead of re-deriving the guard. Not a capability skill; the publish loop that PROVES this lives in prove-on-dacpac, the probes in talk-to-local-sql.
---

# The tightening class — the guard is TABLE-HAS-ROWS, not column-satisfies-rule

> **This is the flagship abstraction of the whole tree, and it corrects a wrong recipe.** The
> old advice — "backfill the NULLs, then the declarative NOT NULL lands clean under Strict =
> Mechanism 3" — was **disproven empirically on the proving ground**. Do not repeat it. What
> replaces it is below, and it is shared verbatim by every op in this class so no per-op skill
> has to re-derive it.

You are helping an **OutSystems-native developer** land a safe schema change. When they *tighten*
a rule on an existing column — make it mandatory, shorten it, drop it — the same instinct applies,
because SSDT guards all of them the same conservative way. Learn the class once; stop
re-discovering the same veto op by op.

## The members of the class

A **tightening** change is one that removes capacity or removes a column from a populated table.
The members that share this exact guard:

- **make-mandatory** (`NULL` → `NOT NULL`) — the spine proof.
- **narrow** (`NVARCHAR(50)` → `NVARCHAR(10)`, reduced precision) — *Ambitious Narrowing*.
- **delete-attribute** (`ALTER TABLE ... DROP COLUMN`) — the values are irrecoverable.

Plus the **populated-veto face** of the constraint ops — **add-check**, **add-unique**,
**define-PK** — insofar as they refuse on a populated table. Those ops *also* veto on an actual
data **violation** (a dup, an orphan, a failing predicate); that violation face is a *different*
concern owned by `../constraint-is-a-claim/SKILL.md`. Read the distinction at the bottom of this
file — it is deliberate and load-bearing.

## The guard, verbatim (VERIFIED on the proving ground)

For a `NULL → NOT NULL` change (and structurally identical for a narrow), `sqlpackage` generates:

```sql
IF EXISTS (SELECT TOP 1 1 FROM [dbo].[Customer])
    RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127);
-- ... and BELOW it, the actual:
ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR(256) NOT NULL;
```

Read what this is: the guard fires on `IF EXISTS (SELECT TOP 1 1 FROM <table>)` — **the table
merely having a row** — and it is placed **before** the `ALTER`. **It never inspects the column at
all.** It does not count NULLs. It does not measure `MAX(LEN)`. It is **data-blind**: row-presence,
not rule-satisfaction.

## The WHY (specialize this per op; do not restate the whole thing there)

SSDT computes the **entire deploy script once, up front, from the pre-publish model state**, and
is **conservative by design**. It cannot know that a pre-deploy backfill — which runs *at deploy
time, after the script is already generated* — will have emptied the NULLs, or that every value
already fits the narrower type. So it refuses the moment the table holds any row. **The gate
cannot know your intent, so it assumes the worst.**

The empirical proof (the showcase finding): a pre-deploy backfill cleared **every** NULL
(`SELECT COUNT(*) WHERE Email IS NULL` returned **0**), and Strict **STILL vetoed**, leaving the
column nullable. The narrow twin confirmed it: `MAX(LEN)` fitting the new size did **not** clear
the veto either. **Zero violations is necessary but NOT sufficient** on a populated table.

## The ladder (empty = clean; populated = a conscious call)

- **EMPTY table** → **Mechanism 1 Pure Declarative, single-phase, Tier 1.** The `IF EXISTS` is
  false, the `RAISERROR` never fires, the tightening `ALTER` lands. *(Confirm the table is
  genuinely empty first.)* This is the **only** clean single-phase leg.
- **POPULATED table (violations present OR zero violations — it makes no difference)** → the
  change **cannot pass the prod-strict gate by backfill/reconcile alone.** It requires a
  **conscious, documented decision taken AFTER a verified probe** (prove the violation count is 0
  first — necessary, not sufficient), then ONE of:
  - **(a) Targeted gate-relaxation** — operationally **Mechanism 4 / Script-Only with a named
    relax.** Having *proven* zero violations remain, deliberately disable `BlockOnPossibleDataLoss`
    for **this one targeted change** (a scoped publish-profile override or a script-only path) so
    the tightening `ALTER` proceeds against the now-clean column. The proof packet must carry
    **both** the zero-violation probe **AND** the explicit record of the relaxation decision.
  - **(b) Restructure as Mechanism 5 — Multi-Phase, multi-PR** — stage it so the engine never has
    to relax its guard (add the tightened column in a new structure and migrate, or sequence the
    tightening across releases where the model state the engine diffs no longer trips the
    table-has-rows guard).
  Tier 2 baseline; **+1** for CDC-enabled / >1M rows / first-time (see `../cdc/SKILL.md`).

## How the per-op specifics differ (they still point here)

The **guard mechanics above are identical** for every member — that is the whole point of lifting
them. What each op still owns in its own SKILL:

- **make-mandatory** — the probe is `COUNT(*) WHERE col IS NULL`; the trap is trusting a clean NULL
  probe as a green light.
- **narrow** — the probe is `MAX(LEN(col))` + `COUNT(*) WHERE LEN(col) > <new>`; `MAX(LEN)` fitting
  decides gate-relaxation vs. multi-phase for the *remedy*, but never buys a clean single-phase on
  a populated table.
- **delete-attribute** — the values are irrecoverable; danger is Tier 3–4 even when the drop is
  mechanically one statement; the 4-phase deprecation is its multi-phase shape (see
  `../multi-phase/SKILL.md`).

## Prove it (pointer, not a re-scaffold)

For the publish loop that PROVES this — build the dacpac, Strict-publish, read the delta, watch
the veto, then prove the chosen remedy lands the tightening — see `../../prove-on-dacpac/SKILL.md`.
For the probes that PREDICT the veto (`COUNT(*) WHERE col IS NULL`, `MAX(LEN)`), see
`../../talk-to-local-sql/SKILL.md`. The probe predicts; the Strict publish proves; **the guard is
row-presence regardless of what the probe returns.**

## NOT the same as constraint-is-a-claim (keep them separate)

`../constraint-is-a-claim/SKILL.md` vetoes on an actual data **violation** — a value that breaks
the rule (a duplicate, an orphan, a failing predicate). **This** class vetoes **data-blind** on
row **presence** — the guard never looks at a value. Collapsing the two would re-lose the exact
distinction the proving ground exists to teach. When a constraint op refuses on a *populated but
clean* table, that is this class; when it refuses on *dirty data*, that is the claim.

## Handbook

Cite by **filename** (offset +3): handbook **16** (= §19) for the Optimistic NOT NULL / Ambitious
Narrowing anti-patterns, and **10-SSDT-Deployment-Safety.md** for the `BlockOnPossibleDataLoss`
gate semantics.
