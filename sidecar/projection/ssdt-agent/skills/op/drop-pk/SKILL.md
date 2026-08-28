---
name: drop-pk
description: Use when the developer says "remove the primary key", "this table shouldn't have an Identifier", "we're changing the key" — removing a PRIMARY KEY. Two faces, proven apart. Referenced by any FOREIGN KEY, the model refuses to build (SQL71516) before the engine is reached. Unreferenced, it publishes clean as one DROP CONSTRAINT and the table silently becomes a heap.
---

# Drop a primary key

> **Default (provisional — prove before you classify).** Two faces with different gates. When any
> foreign key references the key, the project does not build — `SQL71516` at build time, before
> any publish — so the change cannot ship at all until the dependents are handled. When nothing
> references it, the drop ships as a single schema change, applied in place, and the table becomes
> a heap. A dev lead must review either face: the table's identity guarantee and its clustered
> organization are both removed.

> **SHIP terminal: ONE RELEASE, in place — or REFUSED AT BUILD.** Both proven live on this branch
> (database `PG_inv_x1`, sqlpackage 170.5.76). Referenced face: with
> `FK_OrderLine_Order_OrderId` in the model, removing `PK_Order_Id` fails the build —
> `Build error SQL71516: The referenced table '[dbo].[Order]' contains no primary or candidate
> keys that match the referencing column list in the foreign key.` Unreferenced face: removing
> `PK_Category_Id` generated the single statement
> `ALTER TABLE [dbo].[Category] DROP CONSTRAINT [PK_Category_Id];` and the Strict publish
> returned `Successfully published database.` — three rows intact, no clustered index left.
>
> **Proven precedent:** `../../../sample-prs/drop-pk.md` — the worked instance of the ten-section
> template (`../../author-pr/SKILL.md`) for this op.

## OutSystems phrasing
"remove the primary key", "this entity shouldn't have an Identifier", "we're changing what the
key is". In OutSystems every entity carries an `Id` Identifier, so a genuine PK removal is rare —
listen for what is actually wanted.

## SSDT meaning
Remove the `CONSTRAINT PK_... PRIMARY KEY CLUSTERED (...)` from the CREATE. When it builds,
SSDT emits `ALTER TABLE ... DROP CONSTRAINT [PK_...]` — which also drops the clustered index
the key carried, leaving a heap.

## The named trap
Two, one per face. The referenced face refuses **before the engine**: the model is the first
gate, and `SQL71516` is a build error, not a publish block — no delta, no copy, no Msg. The fix
is never to delete the foreign keys casually to make the build pass; each dependent drop is its
own `../drop-fk/SKILL.md` with its own review, and the whole becomes a multi-step program
(`../../_index/multi-phase/SKILL.md`). The unreferenced face is the opposite trap: a green,
one-statement publish that silently removes the table's physical organization — a heap with no
uniqueness on `Id`, duplicate keys now writable, and lookups scanning.

## How it flips (the specifics only)
- **any FK references the key** → REFUSED AT BUILD (`SQL71516`). No shipping shape exists until
  the request is re-scoped: drop the dependents first (each its own op and review), or the real
  intent is a key change.
- **nothing references the key** → ships in place as a single schema change; a dev lead must
  review it (identity and clustering both removed; the table is a heap after).
- **the real request is "change the key"** → not this op alone: that is drop-then-`../define-pk/SKILL.md`,
  and on a populated table the new key's build re-validates uniqueness over every row — scope it
  as that program.

## Prove it
Build first — the referenced face is decided by the compiler, so `dotnet build` / MSBuild is the
probe (`SQL71516` names the foreign key and file). If it builds, Strict publishes clean; the
delta is a single `DROP CONSTRAINT`; probe `sys.indexes` after to show the heap
(`type_desc = 'HEAP'`). See `../../prove-on-dacpac/SKILL.md`.

## The verdict (to the developer)
"You asked to remove this entity's key. Two things decide what happens. If any other entity
references it, the project refuses to even build until those references are dealt with — each
of those is its own change with its own review. If nothing references it, the removal publishes
clean as one statement, and the table is left with no identity rule and no physical ordering:
duplicates become writable and lookups get slower. Before we ship anything, say what the end
state should be — most requests like this are really a key change, which we scope as remove-key
plus define-key together."

## The reasoning (in conversation)
The primary key is two guarantees in one — identity (no duplicate keys) and organization (the
clustered index). Removing it never loses a row, which is why the engine lets the unreferenced
face through in one green statement; everything it actually costs shows up later, in writes the
table now accepts and reads it now scans for. And when a reference exists, the refusal comes
from the model itself — earlier than any publish, which is the model doing exactly what a
declarative schema is for.

## On the record

The pull request is an instance of the ten-section template in `../../author-pr/SKILL.md`; the
worked instance is `../../../sample-prs/drop-pk.md`. SHIP terminal: **ONE RELEASE, in place**
(unreferenced) or **REFUSED AT BUILD** (referenced). The fragment this operation contributes:

**Review & release**
- A dev lead must review this: the table's identity guarantee and clustered organization are
  removed; no data is touched.
- Unreferenced: ships as a single schema change, applied in place — one
  `ALTER TABLE ... DROP CONSTRAINT`; the table is a heap afterward. Referenced: does not ship —
  the build refuses with `SQL71516` until each dependent foreign key is handled as its own
  change.
- Added scrutiny: the table holds more than a million rows (dropping the clustered index on a
  large table can run long and the resulting heap changes read behavior at scale); or this is
  the first time the operation has been performed on this estate.

**Verification** — run in each environment after deployment
```sql
-- expect 0 rows: the primary key no longer exists
SELECT name FROM sys.key_constraints
WHERE parent_object_id = OBJECT_ID('<schema>.<Table>') AND type = 'PK';

-- expect type_desc = HEAP: the clustered index went with the key
SELECT type_desc FROM sys.indexes
WHERE object_id = OBJECT_ID('<schema>.<Table>') AND index_id IN (0, 1);
```

**Rollback**
Re-adding the key reverses the drop: `ALTER TABLE <table> ADD CONSTRAINT PK_<Table>_<Column>
PRIMARY KEY CLUSTERED (<Column>);`. The build validates the key over every existing row —
a duplicate or NULL key value written while the key was absent blocks it until reconciled
(`../define-pk/SKILL.md`), and rebuilding the clustered index on a large table takes a window.
The drop itself loses no data.

**Not verified**
- Write behavior in the gap — nothing prevents duplicate key values while the key is absent;
  whoever owns the table owns watching for them until a key returns.
- Read performance — the heap's behavior at production scale is not shown by a small copy.
- Other environments — proven on a disposable copy of Dev only. Run both verification queries
  in each environment before promotion.
