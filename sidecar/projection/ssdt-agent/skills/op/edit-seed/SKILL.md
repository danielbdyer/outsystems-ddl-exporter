---
name: edit-seed
description: Use when the developer says "add 'Refunded' to the Status list", "change the label on this lookup value", "we have a new order type", "rename the Cancelled option" — adding or amending rows in an existing OutSystems Static Entity. SSDT destination = extend or amend the VALUES block of the idempotent MERGE in the post-deploy seed.
---

# Edit static seed records (unconditional-WHEN-MATCHED trap)

> **Default (provisional — the data decides).** Ships as one release: the seed MERGE in the
> post-deployment script re-runs, inserting the new row or amending the one changed row. The table
> definition is unchanged. Any team member can review this: the change is additive and the running
> application is unaffected. Prove the redeploy is silent and the label change touches exactly one row
> before classifying.

## OutSystems phrasing
"add 'Refunded' to the Status list", "change the label on this lookup value", "we have a new order type".

## SSDT meaning
Extend or amend the `VALUES` block of the MERGE in the post-deploy seed. Adding a value = a new `WHEN NOT MATCHED THEN INSERT` row; changing a label = the **guarded** `WHEN MATCHED THEN UPDATE` path fires for that one row. Never write ALTER.

## The named trap
An **unconditional `WHEN MATCHED`** that rewrites every row on every deploy (the CDC-silence violation) — this is the idempotent-seed concern; see `../../_index/idempotent-seed/SKILL.md`. (Retiring a referenced value is `delete-seed-value` — route there; that op owns deactivate-don't-delete.)

## How it flips (the specifics only)
- add a new value / change a label → ships as one release; any team member can review it — the change
  is additive and the running application is unaffected.
- retire a value the app references → route to `../delete-seed-value/SKILL.md` (deactivate, don't delete).
- **+ CDC-tracked** → the guarded `WHEN MATCHED` is mandatory; added scrutiny, because the table feeds
  a change-data-capture stream and an unguarded rewrite would surface as phantom changes — see
  `../../_index/cdc/SKILL.md`.

## Prove it
Deploy the new/changed seed, then redeploy unchanged and assert **0 rows affected** + identical data-hash. For a label change, additionally prove the guarded MERGE updates **only the one changed row** — snapshot the `WHEN MATCHED` branch rowcount; it must equal 1, not the table size. If CDC-tracked, the no-op redeploy captures 0. See `prove-on-dacpac` / `talk-to-local-sql` for how. On the sample, add a 'Refunded'-shaped value to `dbo.Category` (STA-02).

## The verdict (to the developer)
You asked to add 'Refunded' to the Status list. It's in the seed now, and the redeploy proved it
idempotent — the second deploy added nothing, and the label change touched exactly one row, not the
whole table. That one-row discipline is what keeps a change-data-capture feed from reporting the edit
as a table-wide change. Nothing else moves; it's ready to ship.

## The reasoning (in conversation)
A label change has to touch the one row that changed, not rewrite the whole table. If the table is
CDC-tracked, an unguarded MERGE that rewrites every row on every deploy makes the capture feed report
the entire table as changed — phantom edits that never happened. That is the failure the guarded `WHEN
MATCHED` avoids, and it is why the guard compares each column before updating. The full why:
`../../_index/idempotent-seed/SKILL.md` (the guarded MERGE) and `../../_index/cdc/SKILL.md`
(over-capture).

## On the record
The fragment this op contributes to the pull request (`../../author-pr/SKILL.md`).

**Review & release**
- Any team member can review this: a seed value is added (or a label amended) and the running
  application is unaffected. The change is additive; existing rows keep their identity.
- Ships as one release: the seed MERGE in the post-deployment script re-runs, inserting the new row or
  amending the one changed row. The table definition is unchanged.
- Added scrutiny: if the table feeds a change-data-capture stream, the guarded `WHEN MATCHED` is
  mandatory so the redeploy captures only the changed row, not the whole table. Otherwise none.

**Verification** — run in each environment after deployment
```sql
-- expect exactly 1 row: the added or amended value is present once with its current label
SELECT Id, Code, IsActive FROM dbo.Category WHERE Code = N'Refunded';
```

**Rollback**
Revert the `VALUES` block and redeploy. A label amendment reverts through the same guarded `WHEN
MATCHED`, which sets the row's `Code` back — lossless. A newly added value is removed by a separate
deactivate-don't-delete step (`../delete-seed-value/SKILL.md`): the seed MERGE has no
delete-unmatched-by-source branch, so taking the row out of the VALUES block leaves the inserted row
in place.

**Not verified**
- Application impact. Code paths that switch on the exact set of seed values — a screen bound to the
  list, logic that resolves a value by id — are not exercised on the disposable copy (@app-owner).
- Other environments. Whether Test, UAT, or Prod already hold this key with a different label, or the
  id is already taken, is unknown from the disposable copy of Dev; run the verification query before
  promotion.
