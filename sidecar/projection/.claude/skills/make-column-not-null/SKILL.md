---
name: make-column-not-null
description: >
  Use when an OutSystems-native developer wants to make an EXISTING attribute
  mandatory -- i.e. flip "Is Mandatory = No" to "Is Mandatory = Yes" on an
  attribute that already has data. Triggers on the words the developer actually
  types: "make required", "make mandatory", "set Is Mandatory to Yes",
  "NOT NULL", "this attribute can't be empty anymore", "require a value",
  "stop allowing blank". Proves the change is SAFE TO SHIP by running it on a
  throwaway local SQL Server loaded with real-shaped data and returning
  {what breaks, the fix, the proof}. Do NOT use for a brand-new attribute
  (that is add-attribute) or for a new entity.
---

# make-column-not-null -- prove it is safe to ship

You are making an attribute that USED to allow empty values into one that
requires a value. In Service Studio you would set **Is Mandatory = Yes** and
publish, and the platform sorted out the existing rows for you. SSDT will not.
The danger is not the one-line schema edit -- it is the rows that are *already
empty*.

This skill proves safety by running your change on a throwaway copy of the
schema with real-shaped data, letting SQL Server itself say what breaks, then
proving the fixed version survives.

## What you said you want (OutSystems -> SSDT)

| Your OutSystems intent | The SSDT / SQL meaning |
|---|---|
| Entity attribute, Is Mandatory: No -> Yes | `ALTER TABLE [Entity] ALTER COLUMN [Attr] <type> NOT NULL` |
| "the existing records are fine" (the assumption) | SQL Server **rejects** the change if ANY existing row is empty (NULL) |
| Publish | Multi-phase: backfill the empties **first**, then apply the constraint |

This is a populated-table change. The schema edit and the data fix cannot be
reasoned about separately -- see `handbook/14-Multi-Phase-Pattern-Templates.md`
§17.2 (NULL -> NOT NULL on a Populated Table).

## The one trap this catches

**The Optimistic NOT NULL** -- `handbook/16-Anti-Patterns-Gallery.md` §19.2.
The build succeeds (SSDT does not know your data). Then the deploy fails:
*"Cannot insert the value NULL into column '<Attr>'"*, because real rows still
hold NULL. Worse, if `GenerateSmartDefaults` is on, SSDT silently backfills
empty strings and you ship corrupted data with no error at all. Service Studio
hid this by inventing a value for you; here you must choose the value and prove
the empties are gone.

## Inputs to collect

- **Entity -> table** (e.g. Customer -> `dbo.Customer`)
- **Attribute -> column** and its SQL type (e.g. Email -> `Email NVARCHAR(200)`)
- **The fill value for existing empties.** Ask in OutSystems terms:
  *"What should records that are currently empty become?"* Either a literal
  default (`'unknown@placeholder.com'`) or a value derived from other attributes.
  This is §17.2 Option A vs Option B.

## The SSDT change

Two phases, **same release is acceptable** because the pre-deployment backfill
runs before the constraint is applied (§17.2). For a very large table the
backfill cost can push this into two releases -- that volume call cannot be
proven by this kit on real-SHAPED (not real-VOLUME) data; see "What this cannot
prove" in `change-kit/README.md`.

Phase 1 -- pre-deployment backfill (fill the empties first):
```sql
UPDATE [dbo].[Customer]
SET [Email] = 'unknown@placeholder.com'   -- the fill value the dev chose
WHERE [Email] IS NULL;
```
Phase 2 -- the declarative definition change (the table's .sql file):
```sql
[Email] NVARCHAR(200) NOT NULL,   -- was NVARCHAR(200) NULL
```
SSDT then generates `ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email]
NVARCHAR(200) NOT NULL`.

## Prove it

Build a scenario folder with three files and hand it to the one prove-loop. Copy
the shipped magic-moment scenario and edit it to your table, or write your own:

```
change-kit/scenarios/notnull/
  00-seed.sql          -- BEFORE schema + real-shaped rows, incl. some empties
  10-change-naive.sql  -- ALTER COLUMN ... NOT NULL   (expected to BREAK)
  20-change-fixed.sql  -- UPDATE backfill ; ALTER COLUMN ... NOT NULL (succeeds)
```
The seed MUST be drop-create idempotent (`DROP TABLE IF EXISTS ...; CREATE ...`)
so the loop can reset between the naive and fixed runs.

Then, from the project root (`sidecar/projection`):
```bash
bash change-kit/prove-safe.sh change-kit/scenarios/notnull
```
The loop spins up SQL Server via `scripts/warm-sql.sh`, creates a fresh per-run
database, seeds it, applies the naive change (SQL Server raises the real error),
resets, applies the fixed change, snapshots before/after with a value checksum,
prints one verdict, and drops the database.

If `change-kit/prove-safe.sh` is not installed in your checkout yet, run the trap
detector by hand against a seeded copy:
```bash
eval "$(scripts/warm-sql.sh start)"   # exports PROJECTION_MSSQL_CONN_STR
# count the offenders the naive change would choke on:
#   SELECT COUNT(*) AS Empties FROM dbo.Customer WHERE Email IS NULL;
```

## Read the verdict

- **BLOCKED** -- the fixed change failed too. The verdict prints the SQL Server
  error. Most often the backfill missed a path to NULL; fix `20-change-fixed.sql`
  and re-run.
- **SAFE** -- the naive change broke with *Cannot insert the value NULL*, the
  backfill filled the empties, the NOT NULL change then succeeded, and the
  checksum proves every row survived. Ship it. Report the real numbers to
  whoever asked: *"N records had an empty <Attr>; here is what they became, and
  here is proof the required version applies cleanly with no row lost."*

Rollback (`handbook/11-Multi-Phase-Evolution.md`): revert the definition back to
NULL; the backfilled values remain (usually fine -- note it).

## After it ships (the bridge)

This attribute lives in a table OutSystems reads as an **External Entity**. SSDT
must deploy first, then **both** refreshes happen, in order
(`handbook/17-The-OutSystems-External-Entities-Workflow.md`):

1. **Integration Studio** -> open the extension -> right-click the External
   Entity -> **Refresh** -> set **Is Mandatory = Yes** to match the new NOT NULL
   -> **Save** -> **Publish** the extension.
2. **Service Studio** -> refresh the extension reference -> publish the app.

Forgetting either refresh is the single most common post-deploy confusion for
this persona; both are required and SSDT always leads.

## Optional accelerant

For a large or unfamiliar table, the F# Projection engine (this repo) can profile
the attribute's empty-rate and suggest a fill value before you run the loop. It
is entirely optional and never required -- the generic loop above proves safety
on its own.
