---
name: move-attribute
description: Use when the developer says "move the Region attribute from Customer to Account", "this field is on the wrong entity", "relocate DiscountRate to the parent" — a single attribute moving from one entity to another, with data. SSDT destination = a multi-phase add-column-to-destination / copy / repoint / drop-from-source program.
---

# Move an attribute between entities (relationship-ambiguity / cross-table-rename trap)

> **Default (provisional — the data decides; prove before you classify).** Ships across multiple
> releases (multiple pull requests): add the column to the destination and copy the values, repoint
> readers, then drop the column from the source — the two tables coexist while readers migrate. A dev
> lead must review this: existing data is moved between tables and the source column is dropped. Prove
> the relationship is 1:1 before copying anything, so no moved value is ambiguous.

## OutSystems phrasing
"move the Region attribute from Customer to Account", "this field is on the wrong entity", "relocate DiscountRate to the parent".

## SSDT meaning
A one-column split. ADD the column to the destination CREATE, copy the values keyed by whatever relationship links the two entities, repoint readers, then drop the column from the source CREATE. SSDT ADDs the column declaratively and (if nullable) publishes clean; the copy runs post-deploy; it **blocks** the source-column drop under `BlockOnPossibleDataLoss` until the values are proven to have moved. Never write ALTER.

> **The application-side cutover is part of this change.** The Integration Studio / Service Studio
> republish order, the two sequencing rules (the app reads the new shape only after the schema
> release; the old shape drops only after the app stops writing it), and what the pull request
> names under Not verified are owned by `../../_index/multi-phase/SKILL.md` — plan no phase
> without it.

## The named trap
**Relationship ambiguity.** The copy needs a join key, and if the relationship is not 1:1 the value is ambiguous — which Customer's Region wins for an Account with many Customers? Same collision shape as `../merge-tables/SKILL.md`. Second trap: treating "move" as a rename and letting SSDT DROP+CREATE. A **cross-table move has NO refactorlog identity mapping**, so a rename with no refactorlog entry drops the column and loses its data; it must be copy-then-drop, never a rename. See `../../_index/identity-and-refactorlog/SKILL.md`. The coexistence why is `../../_index/multi-phase/SKILL.md`; do not re-derive either.

## How it flips (the specifics only)
- source column **empty / all-NULL** → no data to move. Add the column to the destination as a single
  schema change applied in place; drop it from the source as its own release — where the source table
  holds rows, even an all-NULL column drop is blocked under `BlockOnPossibleDataLoss`, so that drop
  ships with a pre-deployment step that clears the guard. One pull request each.
- source populated, **proven 1:1** → ships across multiple releases (multiple pull requests): the two
  tables coexist while readers migrate; a dev lead must review it, because existing data is moved
  between tables and the source column is dropped.
- relationship **not 1:1** → STOP: the value is ambiguous — a design decision (which Region wins?),
  not a change in how it ships.
- **+ CDC on the source** → added scrutiny: a table that feeds a change-data-capture stream has its
  capture instance frozen to its current columns and needs handling — see `../../_index/cdc/SKILL.md`.
- **+ >1M rows** → added scrutiny: at production row counts the copy may block writes or run long —
  schedule a window. **+ first time on this estate** → added scrutiny: this move has not been
  performed here before.

## Prove it
Prove the join is 1:1 (a row-count check) BEFORE copying anything, then hash the moving values source-vs-destination and prove them **equal** before the source-column drop. Strict blocks the drop until the hashes match. See `prove-on-dacpac` / `talk-to-local-sql`. On the sample, move `Customer.Region` to `Account` via the seeded 1:1 `Customer.AccountId` (STR-03).

## The verdict (to the developer)
You asked to move Region from Customer to Account. That's a column moving between two tables, not a
rename — the values are copied across and the old column dropped, never renamed (a cross-table rename
has no refactorlog entry and would lose the data). On a disposable copy of Dev I proved the
relationship is 1:1 — each Customer maps to one Account — so no Region value is ambiguous; the copy
landed with the source and destination hashes matching, and SSDT keeps the source-column drop blocked
until that match is proven. It ships across more than one release (multiple PRs) so the running app
keeps working while the column moves.

## The reasoning (in conversation)
The word you use — "move" — doesn't decide how this ships; the relationship between the two tables
does, and only the data tells you whether it's 1:1. A move crosses tables, and a cross-table column
has no identity mapping in the refactorlog, so it has to be copy-then-drop, never a rename — a rename
with no refactorlog entry would drop the column and lose its data. The full why — why the two tables
coexist through the move — is `../../_index/multi-phase/SKILL.md` and
`../../_index/identity-and-refactorlog/SKILL.md`.

## On the record
The fragment this op contributes to the pull request (`../../author-pr/SKILL.md`).

**Review & release**
- A dev lead must review this: existing data is moved between tables — the values are copied to the
  destination and the source column is dropped.
- Ships across multiple releases (multiple pull requests): add the column to the destination and copy
  the values keyed by the relationship, repoint every reader, then drop the source column — the two
  tables coexist while readers migrate, and the copy cannot be expressed as a table definition.
- Added scrutiny: none for a small, clean, 1:1 move; a CDC-tracked source freezes its capture instance
  to its current columns and needs handling; at >1M rows the copy scans the table and may block writes
  or run long — schedule a window; a first-time move on this estate carries added scrutiny (see
  `../../_index/cdc/SKILL.md`).

**Verification** — run in each environment after deployment
```sql
-- expect 0 rows: the relationship is 1:1, so no moved value is ambiguous (a returned row is a parent
-- with more than one child — stop, the move is unsafe as stated)
SELECT <parentkey>, COUNT(*) AS children
FROM <source>
GROUP BY <parentkey>
HAVING COUNT(*) > 1;

-- expect equal hashes: the moving values are the same content on source and destination
-- (run after the copy, before the source-column drop — the gate Strict enforces)
SELECT
  CONVERT(CHAR(64), HASHBYTES('SHA2_256',
    CAST((SELECT <parentkey>, <moving column> FROM <source>
          ORDER BY <parentkey> FOR XML RAW) AS VARBINARY(MAX))), 2) AS source_hash,
  CONVERT(CHAR(64), HASHBYTES('SHA2_256',
    CAST((SELECT <parentkey>, <the same column, now on the destination> FROM <destination>
          WHERE <moving column> IS NOT NULL ORDER BY <parentkey> FOR XML RAW)
         AS VARBINARY(MAX))), 2) AS destination_hash;
```

**Rollback**
Before the source-column drop, backing out is lossless: drop the destination column and repoint readers
back to the source column, which still holds its values. The drop is not auto-reversible — once the
source column is gone the values live only on the destination; restoring the source column means
re-adding it and copying back from the destination (the values were proven equal to the source
originals before the drop). Keep the source column's data recoverable — a backup, or the coexisting
source table — until the drop is confirmed durable.

**Not verified**
- Application impact: any read or write path still pointing at the source column breaks once it is
  dropped; that every reader has been repointed to the destination is not confirmed here — the app
  owner confirms it.
- Other environments: the relationship was proven 1:1 on a disposable copy of Dev only; Test, UAT, and
  Prod may hold a one-to-many parent this copy does not — run the 1:1 check before the copy in each
  environment.
- Production scale / timing: the copy and drop are exercised at seed scale only; blocking and duration
  at >1M rows are not shown by the small copy.
- Reversibility: only the forward move is proven; re-adding the source column and copying back from the
  destination is not exercised here.
