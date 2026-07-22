---
name: delete-entity
description: Use when the developer says "delete the Entity", "drop the table, we don't need it", "remove the old AuditLog" — removing a whole table. The risk is the irreversible data loss, not the number of releases; BlockOnPossibleDataLoss blocking the publish on a populated table is the safety proof.
---

# Delete entity

> **Default (provisional — the data decides).** Ships as a scripted change in a single release — in
> production the drop is an explicit pre-deployment `DROP`, not the mere absence of the file,
> sequenced after any inbound foreign keys are dropped. A principal must review
> this: data is removed and the removal cannot be undone. The risk is the irreversible loss, not the
> release count — one drop in one release still requires a principal. Prove before you classify.

## OutSystems phrasing
"delete the Entity", "drop the table, we don't need it", "remove the old AuditLog".

## SSDT meaning
Remove the `.sql` file; with `DropObjectsNotInSource=True` SSDT emits `DROP TABLE
[schema].[Name]`. On a populated table `BlockOnPossibleDataLoss=True` **blocks** the publish —
that block is the safety proof, not a failure. In production `DropObjectsNotInSource` is usually
**False**, so the drop needs an explicit pre-deployment `DROP`, not mere absence.

## The named trap
Dropping a table with inbound **foreign keys** — the drop fails until they are dropped first. The
block on a populated table is the row-presence gate — see `../../_index/tightening-class/SKILL.md`
for why it is data-blind; do not re-derive the guard here.

## How it flips (the specifics only)
- table empty, no dependents → mechanically a clean drop, but a principal must still review it if
  the table held business data, because the loss cannot be undone; if it is provably scratch (no
  business data), the review need is lower
- table populated → `BlockOnPossibleDataLoss` blocks the drop (row-presence — see
  `../../_index/tightening-class/SKILL.md`); the drop is the irreversible act, so a principal must
  review it: data is removed and cannot be undone
- inbound foreign keys exist → drop those foreign keys **first** (the script owns the ordering) → a
  small multi-step script
- >1M rows or a first-time drop on this estate → added scrutiny: at production row counts the drop
  may block writes or run long, and the operation has not been performed here before

## Prove it
A Strict publish must **block** on `BlockOnPossibleDataLoss` when rows exist — show that block with
the row count as the safety proof. Then prove the ordered remedy (drop foreign keys → drop) on a
disposable copy of Dev. Run `sys.dm_sql_referencing_entities` against the table to
enumerate what still points at it. For the publish loop, see `../../prove-on-dacpac/SKILL.md`;
probes, `../../talk-to-local-sql/SKILL.md`.

## The verdict (to the developer)
You asked to delete the table. Mechanically this is one drop in one release — but it's the gravest
kind of change, because once it lands the data is gone for good and there is no undo. On a
disposable copy of Dev, SSDT's BlockOnPossibleDataLoss blocked the publish because the table still
holds rows; the block reports the exact row count, and that count is the proof of what would be
lost — the block is the safety net working, not a failure. Before this ships I drop the inbound
foreign keys first; I proved on the copy that this order clears the block cleanly
and the table drops. A principal should review it, because the loss can't be undone. One thing to
settle first: is this data truly needed nowhere — no report, no export, no downstream job still
reading it?

## The reasoning (in conversation)
The block isn't SSDT being difficult — it's the gate staying conservative because it can't know your
intent (see `../../_index/tightening-class/SKILL.md`): it sees rows in the table and refuses to
throw them away without a deliberate decision. And the size of a change tells you nothing about its
risk — one statement in one release can still be the most dangerous thing you ship, because risk is
about what is lost, not how much is written. The failure this avoids: reaching for
`DropObjectsNotInSource` to "make it work" instead of proving the table is truly safe to lose.

## On the record
The fragment this op contributes to the pull request (`../../author-pr/SKILL.md`), record register.

**Review & release**
- A principal must review this: data is removed and the removal cannot be undone. If the table is
  provably scratch (no business data ever), the review need is lower.
- Ships as a scripted change in a single release — in production the drop is an explicit
  pre-deployment `DROP` (not the mere absence of the `.sql` file), sequenced after any inbound
  foreign keys are dropped; that ordering cannot be expressed as a table
  definition.
- Added scrutiny, when it applies: at >1M rows the drop may block writes or run long (schedule a
  window); a first-time drop on this estate.

**Verification** — run in each environment after deployment
```sql
-- expect 0 rows before the drop: nothing still points at the table
SELECT referencing_schema_name, referencing_entity_name
FROM sys.dm_sql_referencing_entities('[schema].[Name]', 'OBJECT');

-- expect NULL after the drop: the table no longer exists
SELECT OBJECT_ID('[schema].[Name]', 'U') AS table_object_id;
```

**Rollback**
A dropped table is not losslessly reversible. The table definition is recreated from source control
(the `.sql` file / `CREATE TABLE`), but the rows are gone — the drop is the irreversible act.
Recovering the data depends on a backup taken before the drop; that backup is not part of this
change and must be arranged deliberately. The row count reported by the BlockOnPossibleDataLoss
block records how many rows would be lost.

**Not verified**
- Application impact — any query, view, procedure, report, export, or job that reads the table will
  fail once it is gone: the object no longer resolves. Whether anything in the running application
  still references it is not confirmed here (@app-owner). `sys.dm_sql_referencing_entities` finds
  in-database references only, not application code or external consumers.
- Other environments — the row count and the dependency list were proven on a disposable copy of Dev
  only; Test, UAT, and Prod may hold rows or references this copy cannot see. Run the pre-drop checks
  before promotion.
- Production scale and timing — at >1M rows the drop may block writes or run long; the small copy
  does not show duration or blocking at production scale.
- Reversibility — only the forward drop is proven, and it is irreversible for the data: no rollback
  restores the rows without a separate backup taken beforehand.
