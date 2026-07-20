---
name: define-pk
description: Use when the developer says "set the primary key", "the Identifier", "make this the unique key for the entity", "use a composite key on OrderId + LineNumber" — declaring a PRIMARY KEY inline in the CREATE (new table) or onto a populated table, where the clustered-index build scans every row and is blocked by duplicate or NULL key values.
---

# Define the primary key (the Identifier)

> **Default (provisional — the data decides).** New table: the primary key is part of the CREATE —
> it ships as a single schema change applied in place, and any team member can review it, because
> the change is additive and the running application is unaffected. Existing populated table: the
> primary key builds a clustered index that scans and reorders every row — it still ships as a
> single in-place schema change, but a dev lead or an experienced developer should review it,
> because the build runs over live data. Prove the key is unique and non-NULL before you classify.

## OutSystems phrasing
"the Identifier", "set the primary key", "make this the unique key for the entity", "use a composite
key on OrderId + LineNumber".

## SSDT meaning
`CONSTRAINT [PK_Table] PRIMARY KEY CLUSTERED (...)` inline in the `CREATE`. On a new table it is part
of the create. On an existing populated table, adding the PK **builds a clustered index** (scans and
reorders every row) and **fails if the key column has duplicate or NULL values**.

## The named trap
A PK is a *claim of uniqueness proven at build time* — adding it to a populated table with duplicate
or NULL key values is blocked at the index build. This is the constraint-is-a-claim family (the
populated-table face, where the build is blocked on the actual data) — see
`../../_index/constraint-is-a-claim/SKILL.md`; do not re-derive the claim mechanics here. Separate
trap: confusing an IDENTITY surrogate with a natural key — see `../identity-swap/SKILL.md`.

## How it flips (the specifics only)
- new table: the primary key is inline in the CREATE. Ships as a single schema change applied in
  place; any team member can review it — the change is additive and the running application is
  unaffected.
- existing table, key column already unique and non-NULL: ships as a single in-place schema change,
  but the clustered-index build scans and reorders every row — a dev lead or an experienced
  developer should review it, because the build runs over live data.
- existing table with duplicate or NULL key values: the index build is blocked — `Msg 1505` on
  duplicate keys (naming the duplicate value), or `Msg 8111` for a NULL in a column declared
  nullable — and the error names the offending keys (`../../_index/constraint-is-a-claim/SKILL.md`
  owns the full signature table). Ships as one release with a pre-deployment script that dedupes or
  assigns keys, after which the primary key lands validated —
  or across several releases when old and new application code must coexist while the key is
  introduced. A dev lead must review this: existing data is modified to make the key hold. See
  `../../_index/constraint-is-a-claim/SKILL.md`.
- more than 1M rows: added scrutiny — at production row counts the clustered-index build locks the
  table and runs long; schedule a window.
- CDC-tracked table: added scrutiny — the table feeds a change-data-capture stream and the capture
  instance must be handled; see `../../_index/cdc/SKILL.md`.

## Prove it
Run the op-specific probes FIRST: `SELECT <keycols>, COUNT(*) FROM <table> GROUP BY <keycols> HAVING
COUNT(*) > 1` (the duplicate probe) and a NULL count on each key column. Then a Strict publish: with
duplicates or NULLs the index build is blocked (`Msg 1505` duplicate / `Msg 8111` NULL — show the
offending keys); clean, the delta is the
primary key inline in the CREATE (new table) or a clean clustered-index build (existing table).
Author the dedupe/assign-keys pre-deployment script, then re-run the Strict publish clean. See
`../../prove-on-dacpac/SKILL.md` for the publish loop and `../../talk-to-local-sql/SKILL.md` for
running the probes. Seed: OrderLine (KEY-01) proves the composite `OrderId + LineNumber`.

## The verdict (to the developer)
"Defining the Identifier on a new entity is free — it's part of the create, so it just applies. On
your existing table it builds a clustered index over every row. I checked first: the key is unique
with no NULLs, so it publishes clean. It gets a closer review than a brand-new table only because
the build runs over every existing row — not because anything is wrong with your data."

## The reasoning (in conversation)
A key constraint is a claim about the rows that already exist: it holds only if every row is unique
and non-NULL. So you prove that with the duplicate and NULL probes before you classify how the change
ships and who needs to review it — the same discipline as the rest of the constraint-is-a-claim
family (`../../_index/constraint-is-a-claim/SKILL.md`). The mistake this avoids: assuming the key is
clean and shipping a change that gets blocked at deploy time on dirty keys.

## On the record
What this operation contributes to the pull request (`../../author-pr/SKILL.md`).

**Review & release** — the proven branch selects one pair of findings:
- New table — Any team member can review this: the change is additive and the running application is
  unaffected. Ships as a single schema change, applied in place; the primary key is part of the
  CREATE.
- Existing populated table, key already unique and non-NULL — A dev lead or an experienced developer
  should review this: the clustered-index build runs over every existing row. Ships as a single
  schema change, applied in place; the build scans and reorders every row.
- Existing table with duplicate or NULL keys — A dev lead must review this: existing data is modified
  to make the key hold. Ships as one release with a pre-deployment script that dedupes or assigns
  keys, then the primary key lands validated (or across several releases when old and new code must
  coexist).
- Added scrutiny, when it applies — at production row counts the clustered-index build locks the
  table and runs long, so schedule a window; and if the table feeds a change-data-capture stream,
  the capture instance must be handled (`../../_index/cdc/SKILL.md`).

**Verification** — run in each environment after deployment
```sql
-- expect 0 rows: the key is unique, no value repeats
SELECT <keycols>, COUNT(*) FROM <table> GROUP BY <keycols> HAVING COUNT(*) > 1;

-- expect 0: no key column holds a NULL (run once per key column)
SELECT COUNT(*) FROM <table> WHERE <keycol> IS NULL;

-- expect 1 row: the primary key exists as the clustered index
SELECT name, type_desc FROM sys.indexes
WHERE object_id = OBJECT_ID('<table>') AND is_primary_key = 1;
```

**Rollback.** Dropping the primary key is lossless for the data: `ALTER TABLE <table> DROP CONSTRAINT
PK_<table>;` drops the constraint and its clustered index without changing any row values. If a
pre-deployment script deduped or assigned keys to make the key hold, that remediation is not
auto-reversed — record the original values for a manual restore.

**Not verified**
- Application impact. Any insert path that writes a duplicate or NULL key will now fail with a
  primary-key violation; the application's insert code is not confirmed here.
- Other environments. Test, UAT, and Prod may hold duplicate or NULL keys this copy does not — run
  the duplicate and NULL probes in each environment before promotion.
- Production scale and timing. On a large table the clustered-index build locks the table and runs
  long; a small disposable copy cannot show the duration.
- Reversibility. If keys were deduped or assigned, backing that out is not exercised; the forward
  build is all that was proven.
