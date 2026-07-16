---
name: compat-view
description: Use when the developer says "I renamed Customer to Account but the old reports still ask for Customer", "keep the old entity name working after the rename", "don't break the integrations that read the old table", "the SSIS package still expects the old name" — a bridge that keeps an old entity name readable after a rename/split. SSDT destination = a CREATE VIEW bearing the OLD name selecting from the renamed table, enumerated and temporary.
---

# Backward-compatibility view (SELECT * / forgotten-it-is-temporary trap) — recipe AUTHORED HERE

> **AUTHORED-HERE NOTICE.** Handbook file 14 references §17.8 "backward-compatibility view" as the companion to a rename/split, but the template body is empty. The recipe below is authored here to fill the gap; treat it as the working contract and fold it back when file 14 is completed.

> **Default (provisional — the data decides).** The view itself ships as a single declarative
> schema change — a `CREATE VIEW`, applied in place, reading and writing no data. It is one release
> in a staged rename/split program: the view lands with the rename to keep the old name readable,
> and a later release drops it once every external consumer has migrated. A dev lead or an
> experienced developer should review it, because the dependency scope reaches outside the dacpac to
> consumers the SSDT model cannot see. Provisional until the rename delta and the view's transparency
> are proven.

## OutSystems phrasing
"I renamed Customer to Account but the old reports still ask for Customer", "keep the old entity name working after the rename", "don't break the integrations that read the old table".

## SSDT meaning
After a rename or split changes the real table's name/shape, create a **view bearing the OLD name** that SELECTs from the new table, mapping the old column list to the new one. External consumers keep reading `dbo.Customer` (now a view) while the real data lives in `dbo.Account` (the table). The view is the bridge that lets old and new names coexist. Never write ALTER.

## Why it exists
A rename is only safe inside the dacpac if the refactorlog records it, so SSDT emits `sp_rename` and not a DROP+CREATE that would lose the table's data (see `../../_index/identity-and-refactorlog/SKILL.md`). The refactorlog protects only consumers **inside** the SSDT model. Anything outside it — SSIS, Power BI, hand-written procedures, the SSAS feed — still asks for the old name and breaks the moment the table is renamed. The compat view restores the old name as a readable surface.

## The named trap
Making the compat view a `SELECT *` (re-triggers the *SELECT \* View* trap and defeats the purpose — enumerate and alias the old column names; see `../create-view/SKILL.md`), and forgetting it is **temporary** — a compat view is debt with a sunset date. Leaving it forever recreates the name ambiguity the rename resolved.

## The recipe (authored here)
1. **Inside the model**, perform the rename with the refactorlog entry so internal consumers and the dacpac see `sp_rename` (no data loss). Prove the delta is `sp_rename`, NOT DROP+CREATE.
2. **Create a view bearing the OLD name**: `CREATE VIEW dbo.Customer AS SELECT AccountId AS Id, AccountName AS Name, … FROM dbo.Account;` — enumerate every old column, aliasing new names back to old so the external contract is byte-for-byte the same shape.
3. **Mark it temporary**: comment the view with the consumers it serves and the sunset trigger ("drop when SSIS package X and report Y have migrated to dbo.Account").
4. **Sunset (a later PR)**: once every external consumer has moved, drop the compat view as a clean subtractive change.

## How it flips (the specifics only)
- **no external consumers** of the old name (all inside the SSDT model) → the compat view is not needed; the refactorlog alone keeps every consumer working. No view ships.
- **external consumers exist** → the compat view ships alongside the rename, and a sunset PR is scheduled. The view is a single declarative addition; the rename ships in whatever shape its own change requires. A dev lead or an experienced developer should review — the dependency scope reaches outside the dacpac.

## Prove it
(a) the rename delta is `sp_rename` not DROP+CREATE (prove the refactorlog is honored — see `../../_index/identity-and-refactorlog/SKILL.md`); (b) `SELECT` from the compat view returns the SAME shape and SAME row hashes as the pre-rename table — hash the old table before, hash the compat view after, prove **equal** (the bridge is transparent). See `prove-on-dacpac` / `talk-to-local-sql`. Exercise with the `Customer` rename scenario; `dbo.vOrderSummary` is the enumerated-view target (VIE-02).

## The verdict (to the developer)
You renamed the entity, and that rename is safe: SSDT does an `sp_rename`, not a drop-and-recreate, so no data is lost — proven on a disposable copy. The catch is the reports and integrations that still ask for the old name. They live outside SSDT's model, so the rename would break them the moment it lands. To keep them working, there's now a view named `Customer` that reads from the renamed table and returns the exact same shape — proven by matching row hashes. It's marked temporary: once those consumers move to the new name, a later PR drops it.

One thing worth pinning down: which consumers still read the old name? That list is what the view's sunset comment should carry, and it's how you'll know when the later PR can safely drop the bridge.

## The reasoning (in conversation)
The refactorlog's reach stops at the model boundary. So a rename's real dependency scope is everything outside the model — the SSIS packages, the Power BI reports, the hand-written procedures that still ask for the old name. The refactorlog protects the consumers SSDT can see and does nothing for the ones it can't. The compat view covers that gap, and it is debt with a sunset date, not a permanent fixture: leaving it forever brings back the name ambiguity the rename resolved. The failure it avoids is renaming a table out from under an SSIS or Power BI consumer that still asks for the old name. For the full why — identity versus name — see `../../_index/identity-and-refactorlog/SKILL.md`.

## On the record
The fragment this change contributes to the pull request (`../../author-pr/SKILL.md`):

**Review & release**
- A dev lead or an experienced developer should review this: external consumers outside the SSDT
  model read the old name, and those consumers must migrate to the new name to keep working.
- Ships across releases: the compat view lands with the rename so the old name stays readable, and a
  later release drops it once every external consumer has moved.
- Added scrutiny: the dependency scope reaches outside the dacpac — SSIS, Power BI, hand-written
  procedures, and the SSAS feed read the old name and are not in the model.

**Verification** — run in each environment after deployment
```sql
-- expect 1 row: the old name now resolves to a view, not a table
SELECT name, type_desc FROM sys.objects WHERE name = 'Customer' AND type = 'V';

-- expect equal hashes: the compat view returns the same content as the renamed table
SELECT
  CONVERT(CHAR(64), HASHBYTES('SHA2_256',
    CAST((SELECT * FROM dbo.Customer ORDER BY Id FOR XML RAW) AS VARBINARY(MAX))), 2) AS compat_hash,
  CONVERT(CHAR(64), HASHBYTES('SHA2_256',
    CAST((SELECT AccountId AS Id, AccountName AS Name FROM dbo.Account ORDER BY AccountId
          FOR XML RAW) AS VARBINARY(MAX))), 2) AS base_hash;
```

**Rollback**
`DROP VIEW dbo.Customer;` — lossless, the view stores no data of its own. Backing it out re-exposes
the old-name consumers to the rename; the rename's own rollback is separate.

**Not verified**
- Application impact. The SSIS packages, Power BI reports, procedures, and SSAS feed that read the
  old name are outside the dacpac; that each resolves correctly against the view is not verified here
  — the consumers' owners confirm it.
- Write paths. The compat view is read-only; any consumer that writes through the old name
  (INSERT/UPDATE on dbo.Customer) fails against a plain view. Only the readable contract is covered.
- Sunset completion. Whether every external consumer has migrated — the trigger to drop the view —
  cannot be known from the disposable copy.
- Other environments. Test, UAT, and Prod may hold consumers this copy cannot see; run the
  verification query before promotion.
