# Customer: rename the Email attribute to EmailAddress — a rename without a refactorlog is a drop-and-add, blocked by the data-loss guard; the real rename is a metadata sp_rename

**In OutSystems** — You rename the `Email` Attribute on the `Customer` Entity to `EmailAddress`. In Service Studio the field keeps its data and the platform rewires every reference for you.
**In SSDT** — `[Email] NVARCHAR(250) NOT NULL` becomes `[EmailAddress] NVARCHAR(250) NOT NULL` in `Tables/dbo.Customer.sql`. A column name is an **address, not identity**: **without a refactorlog entry** SSDT has no way to know this is the *same* column under a new name, so it sees "`Email` is gone, `EmailAddress` appeared" — two different columns — and plans to **drop one and add the other**.

## Summary

You rename `Customer.Email` to `EmailAddress`. This looks like a one-word edit and carries the **most expensive trap in the catalog**: the column name is only a label, and SSDT only knows a rename happened if the **refactorlog** carries it. Edit the name alone and the generated deploy is `DROP COLUMN [Email]` + `ADD [EmailAddress]` — which would discard every email address in the table. Against the Twin — a disposable SQL Server database published from this estate and filled with real-shaped synthetic data — under a **production-faithful** publish (`BlockOnPossibleDataLoss = true`), the discovered outcome is that the deploy is **REFUSED**: the data-loss guard fires on the drop of the populated column and the whole change rolls back, leaving `Email` and every value exactly as they were. The block is the safety net; the underlying plan is still a destructive drop-and-add. No work item was provided with the request; attach one before merge so the record is traceable.

**The correct rename is a metadata `EXEC sp_rename 'dbo.Customer.Email', 'EmailAddress', 'COLUMN'`** (or a refactorlog entry that makes SSDT emit exactly that), which renames the column *in place* — proven below to preserve every value byte-for-byte.

## Review & release

- A dev lead or an experienced developer must review this: a name-only edit **does not rename the column**; its deploy plan drops the old column and adds a new empty one, and only the production data-loss guard stands between that plan and losing every email address. That guard is not something to lean on — it means the change cannot ship as written at all.
- It ships one of two ways, both of which preserve the data: (a) as a **scripted `EXEC sp_rename 'dbo.Customer.Email', 'EmailAddress', 'COLUMN'`**, a metadata rename that keeps every value; or (b) as the same name edit **paired with a refactorlog entry**, so SSDT emits that `sp_rename` for you. The bare name edit, reviewed here, is **not** a valid way to ship the rename.
- Every caller of the old column name must move to the new one: views, stored procedures, ORM mappings, reports, ETL, and any integration that reads `Customer.Email`. That reference list is the main release risk — the rename is metadata-cheap but breaks every consumer of the old name.
- Added scrutiny: first time this operation is proven on the Twin; a first attribute rename on this estate carries added scrutiny — read the generated delta before every promotion.

## Changes

| File | Change |
|---|---|
| `Tables/dbo.Customer.sql` | Renames `[Email]` to `[EmailAddress]` (same type `NVARCHAR(250)`, same `NOT NULL`) in the table definition |
| *(required, not yet present)* `<project>.refactorlog` | Must carry the rename so SSDT emits `sp_rename` instead of `DROP` + `ADD` — **or** ship the rename as an authored `EXEC sp_rename` script instead of the bare name edit |

The name edit alone is what makes this a drop-and-add. It must be paired with the refactorlog entry or replaced by the scripted `sp_rename`.

## Data remediation

The name edit does not carry the data. On the disposable copy the corrective, **lossless** rename was proven directly:

```sql
-- the real rename: same column, new name. Every value is preserved.
EXEC sp_rename 'dbo.Customer.Email', 'EmailAddress', 'COLUMN';
```

There is no backfill and nothing to reconcile — `sp_rename` is a metadata operation that touches no rows. If a name-only edit is ever published with `BlockOnPossibleDataLoss` relaxed (or smart-defaults enabled), the plan becomes a real `DROP COLUMN [Email]` and the values are gone; in that case they come back only from a backup. That relaxed path was **not** exercised here — the production-faithful block below is what this Twin proved.

## Deployment evidence — objective proof, production-faithful publish, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, materializes real-shaped data, applies the bare name edit under a **production-faithful** DacFx posture (`BlockOnPossibleDataLoss = true`, `DropObjectsNotInSource = false`, no smart-defaults), and then proves the corrective `EXEC sp_rename` directly. DacFx is the same publish engine `sqlpackage` wraps.

**Test:** `Twin.Tests.Integration.SamplePrRenameTests+SamplePrRenameTests.rename-attribute: a bare column rename (no refactorlog) is a DROP+ADD blocked by the data-loss guard; EXEC sp_rename preserves every value`

```
Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2, Duration: 49 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact 1 (the trap) — the bare rename is a drop-and-add, and it is REFUSED.** `Customer` held **25 rows**, every one with a non-NULL `Email`. The name edit (no refactorlog) published under the production-faithful posture was **refused**: SSDT planned to drop `[Email]` and add `[EmailAddress]`, and the data-loss guard terminated the deploy. Verbatim from the run:

```
baseline: Customer rows=25, [Email] exists=1, [EmailAddress] exists=0, rows with Email NOT NULL=25, Email digest=795751655, Email at MIN(Id)='text-4405'
production publish (BlockOnPossibleDataLoss=true, DropObjectsNotInSource=false) bare rename [Email]->[EmailAddress], NO refactorlog [SSDT DROP COLUMN [Email] + ADD [EmailAddress]]: REFUSED: Could not deploy package.
Warning SQL72015: The column [dbo].[Customer].[Email] is being dropped, data loss could occur.
Warning SQL72015: The column [dbo].[Customer].[EmailAddress] on table [dbo].[Customer] must be added, but the column has no default value and does not allow NULL values. If the table contains data, the ALTER script will not work. To avoid this issue you must either: add a default value to the column, mark it as allowing NULL values, or enable the generation of smart-defaults as a deployment option.
Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 50000, Level 16, State 127, Line 8 Rows were detected. The schema update is terminating because data loss might occur.
Error SQL72045: Script execution error.  The executed script:
IF EXISTS (SELECT TOP 1 1
           FROM   [dbo].[Customer])
    RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127)
        WITH NOWAIT;
```

Reading the facts: the two `SQL72015` warnings are SSDT spelling out its plan — `[Email]` **is being dropped** ("data loss could occur"), and `[EmailAddress]` **must be added** as a fresh empty column. The `RAISERROR` guard (`IF EXISTS(SELECT TOP 1 1 FROM [dbo].[Customer])`) then fired on the populated table and terminated the deploy (`Msg 50000` — "data loss might occur"). The deploy is transactional, so the refusal rolled everything back:

```
  DISCOVERED: [Email] exists after=1 (1 = survived the rolled-back block; 0 = dropped), [EmailAddress] exists after=0 (0 = never landed), Customer rows=25 (was 25), Email digest=795751655 (unchanged=true)
```

`[Email]` is still there, `[EmailAddress]` never landed, all **25 rows** are intact, and the `Email` value digest (`795751655`) is unchanged. The change simply cannot ship as written — the guard blocked a plan that, if unblocked, would have emptied the column.

**Fact 2 (the real rename) — `sp_rename` renames the same column intact.** On the same copy, the metadata rename preserved every value:

```
the CORRECT rename (EXEC sp_rename 'dbo.Customer.Email', 'EmailAddress', 'COLUMN'): [Email] exists=0 (gone), [EmailAddress] exists=1, Customer rows=25, rows with EmailAddress NOT NULL=25, EmailAddress digest=795751655 (Email digest before was 795751655 -> preserved=true), EmailAddress at MIN(Id)='text-4405' (was 'text-4405' -> preserved=true)
```

`sys.columns` now shows `EmailAddress` and no `Email`; all **25 rows** are present and still non-NULL; the value digest is **`795751655`** — identical to `Email`'s digest before the rename — and a concrete probe value (`'text-4405'`, the email of the lowest-`Id` row) came through unchanged. Identical digest before and after is the proof this was a **rename**, not a drop-and-re-add.

## Verification — run in each environment after deployment

```sql
-- expect 1 row, name = EmailAddress: the column exists under the new name only, the old name is gone.
-- Two rows, or a row named Email, means the rename did not land as a rename.
SELECT c.name FROM sys.columns c
WHERE c.object_id = OBJECT_ID('dbo.Customer') AND c.name IN ('Email', 'EmailAddress');
```

Before each promotion the generated delta must read as `EXEC sp_rename ... 'COLUMN'`, **not** `DROP COLUMN [Email]` + `ADD [EmailAddress]` — a build that lost the refactorlog emits the drop-and-add and (with the gate relaxed) loses every value. On a disposable copy, capture the value digest **before** and **after**: they must match for the rename to be lossless.

## Rollback

Reversible without data loss: rename the column back with its own refactorlog entry —

```sql
EXEC sp_rename 'dbo.Customer.EmailAddress', 'Email', 'COLUMN';
```

`sp_rename` preserves the data in both directions. The callers updated for the new name must be reverted with it — that is **not** auto-reversed. Only the forward rename was exercised here.

## Not verified

- **Application impact.** Every consumer of the old column name — views, stored procedures, ORM mappings, reports, ETL, and integrations not in the dacpac — breaks until it moves to `EmailAddress`. That all of them were found and updated is not confirmed here; the application owner owns closing this before promotion. If an external consumer must keep reading `Email`, a computed column carrying the old name can hold it while those consumers migrate.
- **The drop-enabled / smart-defaults path.** Under a posture with `BlockOnPossibleDataLoss` relaxed or `GenerateSmartDefaults` enabled, the same name edit would proceed as a real `DROP COLUMN` (values lost) or add an empty `EmailAddress` and drop `Email`. That path was not run here — only the production-faithful block — but it is the documented worse case; confirm your target's gate settings and read the delta.
- **The refactorlog path.** Shipping the rename as a name edit **plus** a refactorlog entry (so SSDT emits the `sp_rename` itself) is the declarative alternative to the scripted `sp_rename`. This proof used the scripted rename directly; that a refactorlog entry is present and correct in each environment is not confirmed here — confirm it before each promotion.
- **Other environments.** Test, UAT, and Prod may hold `Customer` at different volumes or carry consumers of the old name Dev does not. The disposable copy cannot see them — run the verification query and read the delta before every promotion.
- **Reversibility.** Only the forward rename is exercised on the disposable copy; the backout rename is the same metadata operation but is not separately proven.
