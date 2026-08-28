---
name: drop-check
description: Use when the developer says "remove the rule that Total must be positive", "stop enforcing that status list", "drop the check so this load can go through" — removing a CHECK constraint. Always publishes clean (a drop validates nothing), but the rule stops being enforced and re-adding it later re-validates every row.
---

# Drop a check

> **Default (provisional — prove before you classify).** Ships as a single schema change, applied
> in place — one `ALTER TABLE ... DROP CONSTRAINT`, no data read or written, and the publish never
> blocks. A dev lead or an experienced developer reviews it: the business rule stops being enforced
> at the data layer, and rows that violate it can be written from that moment on.

> **SHIP terminal: ONE RELEASE, in place.** Proven live on this branch (database `PG_inv_x1`,
> sqlpackage 170.5.76): removing `CK_Order_Total_NonNegative` from the CREATE generated the single
> statement `ALTER TABLE [dbo].[Order] DROP CONSTRAINT [CK_Order_Total_NonNegative];` and the
> Strict publish returned `Successfully published database.` (The add that preceded it landed
> trusted — `is_not_trusted = 0` — on the same engine; `../add-check/SKILL.md`.)
>
> **Proven precedent:** `../../../sample-prs/drop-check.md` — the worked instance of the
> ten-section template (`../../author-pr/SKILL.md`) for this op.

## OutSystems phrasing
"remove that rule", "stop enforcing it at the database", "drop the check so the load can run".

## SSDT meaning
Remove the `CONSTRAINT CK_... CHECK (...)` from the CREATE. SSDT emits
`ALTER TABLE ... DROP CONSTRAINT [CK_...]`. Data is untouched; the table just stops refusing
rows that break the predicate.

## The named trap
"Drop it so the load can go through" is usually a request for `../toggle-trust/SKILL.md`
(disable, load, re-enable with re-validation), not for a permanent drop — hear the difference
before scoping. And the drop is a one-way door on cheap re-adding: while the check is gone,
violating rows can accumulate, and the re-add re-validates every existing row
(`../../_index/constraint-is-a-claim/SKILL.md`) — each violation blocks it with the check's
`Msg 547` until reconciled.

## How it flips (the specifics only)
- **permanent removal — the rule is genuinely retired** → ships in place as a single schema
  change; a dev lead or an experienced developer reviews it (the data layer stops enforcing a
  business rule).
- **temporary removal to let a load through** → not this op: route to `../toggle-trust/SKILL.md`
  (`NOCHECK` / re-enable `WITH CHECK`), which keeps the constraint's identity and re-validates on
  the way back.
- **re-adding later** → that re-add is `../add-check/SKILL.md` over whatever rows accumulated —
  prove it against the data of that day, not this one.

## Prove it
Strict publishes clean; the delta is a single `DROP CONSTRAINT`; nothing blocks (a drop
validates nothing). Probe `sys.check_constraints` before and after. The real proof burden is
intent: confirm with the developer that the rule is retired for good, and record who owns the
consequence. See `../../prove-on-dacpac/SKILL.md`.

## The verdict (to the developer)
"You asked to stop enforcing this rule. Removing the check always publishes clean — a drop has
nothing to validate — and no existing row changes. From the moment it lands, the database
accepts rows the rule would have refused, and if the rule ever comes back, it is re-checked
against every row written in between. If the real need is a one-time load, disabling and
re-enabling the check keeps the rule and re-validates afterward — say which one you mean."

## The reasoning (in conversation)
A check is a claim the database keeps enforcing on every write; dropping it moves the rule from
the data layer into hope. The publish cannot show that cost — it shows one clean statement. The
mistake to avoid is treating "the load needs it off" as a reason to delete the rule instead of
suspending it.

## On the record

The pull request is an instance of the ten-section template in `../../author-pr/SKILL.md`; the
worked instance is `../../../sample-prs/drop-check.md`. SHIP terminal: **ONE RELEASE, in
place.** The fragment this operation contributes:

**Review & release**
- A dev lead or an experienced developer must review this: a business rule stops being enforced
  at the data layer; no data is touched.
- Ships as a single schema change, applied in place — one `ALTER TABLE ... DROP CONSTRAINT`.
  No data is read or written, and the publish never blocks.
- Added scrutiny: none at deploy time. The consequence accrues after deploy, as unvalidated
  writes.

**Verification** — run in each environment after deployment
```sql
-- expect 0 rows: the check no longer exists
SELECT name FROM sys.check_constraints WHERE name = 'CK_<Table>_<Rule>';
```

**Rollback**
Re-adding the check reverses the drop:
`ALTER TABLE <table> ADD CONSTRAINT CK_<Table>_<Rule> CHECK (<predicate>);`. The re-add
re-validates every existing row, so it lands clean only while no violating row was written in
the gap; a violation blocks it until reconciled (`../add-check/SKILL.md`). The drop itself
loses no data.

**Not verified**
- What gets written while the rule is gone — no deploy-time check exists for future writes;
  whoever owns the rule owns watching for violations until it returns.
- Application-side duplicates of the rule — whether application validation still enforces it is
  not confirmed here.
- Other environments — proven on a disposable copy of Dev only. Run the verification query in
  each environment before promotion.
