# Customer: rename ContactPhone and require Email — why one release cannot carry both

**Atoms:** `rename-attribute` (ContactPhone → MobileNumber, with its refactorlog entry) ·
`make-mandatory` (Email `NULL` → `NOT NULL`, table populated). Both reshape `dbo.Customer` —
reshape-coupled, one concern — and the proof below is why they still cannot share a release.

## Verdict
Combined into one release, this change does not ship: the tightening's row-presence guard
blocks the publish, and the whole release rolls back with it — the rename included, although
the rename alone is safe. It ships as a serialized sequence on one table: Release 1 the rename
(in place, identity preserved), then the tightening as its own two-release
(`../make-mandatory.md`). A dev lead approves the sequence, weighing that existing data is
affected by the tightening leg.

## Intent
The developer's stated intent: the attribute is a mobile number and should be named one, and a
customer can no longer be saved without an email.

## What changes
- `dbo.Customer.ContactPhone` → `MobileNumber`, carried by a refactorlog entry
  (`Rename Refactor`, ElementName `[dbo].[Customer].[ContactPhone]`, NewName
  `[MobileNumber]`), so the engine emits `sp_rename` and the data survives.
- The post-deployment seed's six references to `ContactPhone` renamed with it — the seed is
  part of the rename's change set.
- `dbo.Customer.Email`: `NVARCHAR(256) NULL` → `NOT NULL`, as its own later releases.

## Before promoting
- Land Release 1 (the rename) in an environment before any Email release goes up there.
- Hold other publishes to the environment during each release's window; a concurrent publish
  carrying an older model reverts what the newer release changed.
- The Email tightening's own confirmations are in `../make-mandatory.md` — the fill value for
  blank emails is the data owner's call.

## The data
- `dbo.Customer` holds 5 rows; every row carries a ContactPhone value (5 of 5), which is what
  the rename must preserve. The Email column's blanks are the tightening leg's concern.

## How it ships
- Release 1 — the rename, a single schema change applied in place: the delta is
  `EXECUTE sp_rename @objname = N'[dbo].[Customer].[ContactPhone]', @newname = N'MobileNumber',
  @objtype = N'COLUMN';` plus the corrected seed.
- Then the tightening ships as its own two-release, exactly as `../make-mandatory.md` records.
- Never both in one release — proven below, not policy.

## What proving showed
Published to a throwaway copy on this branch (sqlpackage 170.5.76).
- **Tried (the combined release):** one publish carrying both edits. The generated script
  contains BOTH the `sp_rename` and the guarded tightening. The guard fired
  (`Msg 50000, Level 16, State 127` — "Rows were detected") and the publish was refused —
  and the copy afterward shows `ContactPhone` still present, `MobileNumber` absent, Email
  still nullable. Under `IncludeTransactionalScripts=True` the refused release rolled back
  WHOLE: the blocking atom vetoed its innocent sibling.
- **Tried (rename alone, first attempt):** the schema change succeeded — `sp_rename` ran,
  5 of 5 values intact under the new name — and the publish still failed: the post-deployment
  seed still wrote `ContactPhone` and failed with `Msg 207` ("Invalid column name") after the
  rename had committed. The refactorlog renames the schema; it does not rename raw column
  references inside deployment scripts. The seed rename is part of the change set.
- **Did:** rename the seed's references, publish Release 1 again → published. The delta is the
  single `sp_rename`; 5 of 5 values preserved.
- **Realized:** a release is vetoed by its strictest atom, and a rename's change set is wider
  than its CREATE edit — it includes every script that names the old column.

## After deploy — check
```sql
-- expect 5: every value survived the rename
SELECT COUNT(MobileNumber) FROM dbo.Customer;

-- expect 0 / 1: the old name is gone, the new one present
SELECT COL_LENGTH('dbo.Customer','ContactPhone') AS old_col,
       COL_LENGTH('dbo.Customer','MobileNumber') AS new_col;
```

## How to roll this back
The rename reverses losslessly: a second refactorlog entry renaming `MobileNumber` back, with
the seed's references renamed again in the same change set. The tightening leg's rollback is
recorded in `../make-mandatory.md`.

## Not checked / still open
- Application references to the old attribute name — the refactorlog covers the schema, and
  this change set covers the project's scripts; application code that names `ContactPhone` is
  the application owner's to update.
- The Email tightening's open items are its own record's (`../make-mandatory.md`).
- Other environments — proven on a disposable copy of Dev only; run the checks after each
  promotion, in release order.
