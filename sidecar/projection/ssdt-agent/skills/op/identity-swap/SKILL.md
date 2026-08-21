---
name: identity-swap
description: Use when the developer says "turn on Auto Number for the Id", "make the Id auto-increment", "stop auto-numbering, I want to set Ids myself", "switch this entity to a database-generated key" — adding or removing IDENTITY. SSDT destination = a table rebuild (shadow table + IDENTITY_INSERT copy + reseed + FK drop/recreate), NOT a simple alter.
---

# Add / remove IDENTITY (Auto Number) (silent-table-rebuild trap)

> **Default (provisional — prove before you classify).** On a populated table with incoming foreign keys,
> ships across multiple releases: adding or removing IDENTITY cannot be a simple `ALTER` — it is a
> table property fixed at column creation, so SSDT rebuilds the whole table (a shadow table, a
> `SET IDENTITY_INSERT` copy that preserves every key, a reseed, and every incoming foreign key
> dropped and recreated around the rebuild), and that foreign-key bracket keeps the running
> application working while the change is in flight. A dev lead must review this: the whole table is
> rebuilt and cross-table relationships are dropped and recreated, with added scrutiny the first
> time this is done on the estate. Preview the delta and confirm it is a shadow-table rebuild with
> `SET IDENTITY_INSERT` before promising anything — the danger drives the review need, not the
> release count. Prove before you classify.

> **SHIP terminal: a table rebuild — ONE RELEASE with no incoming FKs, ACROSS MULTIPLE RELEASES with
> them.** IDENTITY is a table property fixed at column creation, so SSDT rebuilds the whole table
> (shadow table + `SET IDENTITY_INSERT` copy + reseed; plus drop/recreate of every incoming FK when the
> table has them). The data-loss gate **allows** the rebuild — rows are moved, not dropped. Proven on
> `dbo.Category` (`pg_idsw_before`): no incoming FKs → **one release**, ids 1,2,3 preserved,
> `is_identity = 1`, `IDENT_CURRENT = 3`; the seed's explicit-id insert failed `Msg 544` until bracketed
> with `SET IDENTITY_INSERT`. `../../_index/identity-and-refactorlog/SKILL.md`.
>
> **Proven precedent:** `../../../sample-prs/identity-swap.md` — the worked instance of the ten-section
> pull-request template (`../../author-pr/SKILL.md`) for this op.

## OutSystems phrasing
"turn on Auto Number for this entity's Id", "make the Id auto-increment", "stop auto-numbering, I
want to set Ids myself".

## SSDT meaning
**A column cannot be `ALTER`ed into or out of `IDENTITY`** — it is a table property fixed at column
creation. SSDT implements the change as a **table rebuild**: create a shadow table with the new
IDENTITY property, copy all data with `SET IDENTITY_INSERT ON` to preserve key values, reseed
IDENTITY to `MAX(Id)+1`, recreate every FK that pointed at the table, drop the old table, rename the
shadow into place. Handbook file 14 (=§17.3). Never write ALTER.

> **The application-side cutover is part of this change.** The Integration Studio / Service Studio
> republish order, the two sequencing rules (the app reads the new shape only after the schema
> release; the old shape drops only after the app stops writing it), and what the pull request
> names under Not verified are owned by `../../_index/multi-phase/SKILL.md` — plan no phase
> without it.

## The named trap
**The silent rebuild** — the .sql edit (adding `IDENTITY(1,1)` to the CREATE) looks trivial but
SSDT's generated delta is a full shadow-table swap that drops and recreates the table. If FKs aren't
all recreated or `IDENTITY_INSERT` isn't used, existing keys are re-minted and every FK points at the
wrong rows. This is the most dangerous "one-line edit" in the catalog. Adjacent: a rebuild that loses
the key mapping re-mints keys the way a rename with no refactorlog entry loses a column's data — see
`../../_index/identity-and-refactorlog/SKILL.md`. This shadow-table-rebuild mechanic is owned here;
`../retype-explicit/SKILL.md` cross-references it.

## How it flips (the specifics only)
- table **empty, no FKs** → the rebuild copies nothing; ships as a single schema change (still
  confirm the delta is a rebuild, not a no-op). A dev lead or an experienced developer should review
  it, because the running application's Id handling changes.
- table populated, **no incoming FKs** → ships as one release, a single scripted rebuild with the
  `SET IDENTITY_INSERT` copy and the reseed proven. A dev lead must review it: the whole table is
  rebuilt and every row is copied.
- table populated **WITH incoming FKs** → ships across multiple releases; the FK drop and recreate
  must bracket the rebuild so the running application keeps working. A dev lead must review it, with
  added scrutiny the first time this is done on the estate.
- **+ >1M rows** → added scrutiny: the data copy is the expensive part and may block writes or run
  long; schedule a window.

## Prove it
Preview the Strict delta and CONFIRM it is a **shadow-table rebuild with `SET IDENTITY_INSERT`**, not
a no-op — if SSDT does not show `Starting rebuilding table`, the IDENTITY edit did not register. Publish
onto a populated copy and prove every Id is **unchanged** (the `SET IDENTITY_INSERT` copy preserved
them), `IDENT_CURRENT` sits at or past `MAX(Id)`, and every reference still resolves. See
`prove-on-dacpac` / `talk-to-local-sql`. On the sample, add IDENTITY to `dbo.Category` (explicit-id, NO
IDENTITY — the source; proven `pg_idsw_before`). `Category` has **no incoming foreign keys**, so no FK
drop/recreate brackets the rebuild and it ships **one release**; the data-loss gate **allows** the
rebuild (rows are moved under `SET IDENTITY_INSERT`, not dropped). The trap the sample proves is
**`Msg 544`**: the seed inserts explicit `Category` ids, which fails once the column is IDENTITY —
`Cannot insert explicit value for identity column … when IDENTITY_INSERT is set to OFF` — until the
MERGE is bracketed with `SET IDENTITY_INSERT dbo.Category ON … OFF`; that seed fix ships in the same
change set. To exercise the *incoming-FK* leg (drop/recreate FKs around the rebuild), use a table that
another table references by a **declared** foreign key.

## The verdict (to the developer)
You asked to turn on Auto Number for the Id. That can't be a simple change — SSDT can't alter a column
into IDENTITY, so it rebuilds the whole table behind the scenes: it builds a copy with the new IDENTITY
property, moves every row across with `SET IDENTITY_INSERT` so the keys are preserved, and reseeds the
counter past the highest existing Id. The disposable copy showed the rebuild keeps every existing Id
and leaves every reference resolving — without that `IDENT_INSERT` copy the keys would be re-minted and
references would point at the wrong rows. If other tables reference this one by a declared foreign key,
those come off and go back on around the rebuild and it stages across releases so the running
application keeps working; where nothing references it, it is one release. Two things to confirm: any
code (the seed included) that inserts an explicit Id now fails with `Msg 544` until it is bracketed with
`SET IDENTITY_INSERT` — from now on the database owns the Id — and the delta really is a rebuild, not a
no-op.

## The reasoning (in conversation)
The size of a .sql edit tells you nothing about the size of the deploy. The most dangerous change in
the whole catalog is a one-line edit — adding `IDENTITY(1,1)` to a CREATE — whose generated delta is a
full table swap. The only honest way to know what a change really does is to preview the delta and read
it, then confirm the keys are preserved before promising anything. The failure this avoids: trusting
that a small edit means a small deploy, and shipping a rebuild that silently re-mints every key. Full
reasoning (identity vs. name): `../../_index/identity-and-refactorlog/SKILL.md`.

## On the record
The pull request is an instance of the ten-section template in `../../author-pr/SKILL.md`; the worked
instance for this op is `../../../sample-prs/identity-swap.md`. SHIP terminal: **ACROSS MULTIPLE
RELEASES** (a table rebuild). The fragment this operation contributes:

**Review & release**
- A dev lead must review this: the whole table is rebuilt (every row is copied) and every incoming
  foreign key is dropped and recreated around the rebuild. On an empty table with no incoming foreign
  keys the rebuild copies nothing and a dev lead or experienced developer can review it, because the
  running application's Id handling still changes.
- Ships across multiple releases on a populated table with incoming foreign keys — the foreign keys
  are dropped, the table is rebuilt (a shadow table, a `SET IDENTITY_INSERT` copy that preserves every
  key, a reseed to `MAX(Id)+1`), then the foreign keys are recreated around it; the running
  application keeps working while the change is in flight. Adding or removing IDENTITY cannot be a
  simple `ALTER` — it is a table property fixed at column creation. On a populated table with no
  incoming foreign keys it ships as one release; on an empty table, as a single schema change.
- Added scrutiny: the first time this rebuild is performed on the estate; a table above ~1M rows,
  where the data copy may block writes or run long and needs a scheduled window.

**Verification** — run in each environment after deployment
```sql
-- expect is_identity = 1: the Id column is now database-generated (IDENTITY);
-- for a removal, expect is_identity = 0
SELECT name, is_identity FROM sys.columns
WHERE object_id = OBJECT_ID('dbo.Category') AND name = 'Id';

-- expect current_seed >= max_id: the reseed sits at or past the highest existing key,
-- so the next generated Id cannot collide with an existing row
SELECT IDENT_CURRENT('dbo.Category') AS current_seed, MAX(Id) AS max_id FROM dbo.Category;

-- expect 0 rows: every reference still resolves after the rebuild preserved the ids
-- (dbo.Product.CategoryId into dbo.Category on the sample; for a table WITH a declared
--  incoming FK, run the same left-join for each child)
SELECT p.Id FROM dbo.Product p LEFT JOIN dbo.Category c ON p.CategoryId = c.Id WHERE c.Id IS NULL;
```

**Rollback**
Backing this out is itself a table rebuild in the other direction — removing (or re-adding) the
IDENTITY property with the same shadow-table copy under `SET IDENTITY_INSERT` to preserve every key,
and the same drop-and-recreate of every incoming foreign key around it. It is not a single
`DROP CONSTRAINT` and it is not auto-reversible; it must be previewed and proven the same way. The
forward rebuild preserves every key value, so there is no data-value change to undo — only the
physical rebuild to repeat.

**Not verified**
- Application impact — after Auto Number is on, the database owns the Id: any insert that supplies an
  explicit Id fails unless it wraps the insert in `SET IDENTITY_INSERT`; for a removal, the mirror —
  the application must now supply the Id itself. Application-side Id handling is not confirmed here
  (app owner).
- Other environments — the rebuild and key preservation were proven on a disposable copy of Dev only;
  Test, UAT, and Prod hold row counts and incoming foreign-key data this copy cannot see. Run the
  verification query before promotion.
- Production scale and timing — the data copy is the expensive part of the rebuild; at production row
  counts it may block writes or run long, which the small copy does not exercise. Schedule a window.
- Reversibility — only the forward rebuild is proven; the inverse rebuild that removes or re-adds
  IDENTITY is not exercised here.
