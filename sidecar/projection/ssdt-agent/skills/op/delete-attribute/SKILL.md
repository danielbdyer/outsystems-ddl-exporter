---
name: delete-attribute
description: Use when the developer says "remove the attribute", "delete the LegacyCode field nobody uses", "drop this column", "get rid of this field" — removing a column. The 4-phase deprecation; danger is not release-count.
---

# Delete attribute (4-phase deprecation)

> **Default (provisional — prove before you classify).** An empty column source
> drops in place — it ships as a single schema change, no data read or written. A populated column
> is blocked: SSDT refuses the drop on `BlockOnPossibleDataLoss` because the table holds rows whose
> values would be lost (the row-presence gate; see `../../_index/tightening-class/SKILL.md`). The
> app must have stopped reading the column first (the deprecation), and then the physical drop ships
> as **two releases**. A dev lead reviews at minimum; a principal must review once the column holds
> data whose loss cannot be undone — *danger is not release-count.*

> **SHIP terminal: TWO-RELEASE.** This pipeline (Azure DevOps → Octopus) cannot relax
> `BlockOnPossibleDataLoss`, so a populated drop ships as R1 (a pre-deploy that drops the column's
> default constraint then the column, with the model still declaring it) then R2 (the model drops
> the column as a no-op). Proven live 2026-08-21 — this advances `FINDINGS_AND_CHANGES.md` Part 5,
> where drops were still TO PROVE. Two drop-specific wrinkles: the seed must stop writing the column
> in the same change set (else `Msg 207` at the post-deploy seed), and re-publishing R1 **re-adds**
> the column with its default (every row stamped with the placeholder), so R1 is a single publish
> and R2 must follow at once. Relaxing the gate is not available on this estate.

> **Proven precedent:** `../../../sample-prs/delete-attribute.md` — the worked instance of the
> `../../author-pr/SKILL.md` template for this op. Its *What proving showed* carries the real
> `Msg 50000` block, the `Msg 207` seed-reference failure, the re-add drift, and the two-release land.

## OutSystems phrasing
"remove the attribute", "delete the LegacyCode field, we don't use it".

## SSDT meaning
Remove the column from the `CREATE`. SSDT emits `ALTER TABLE ... DROP COLUMN [Col]`. On a column
that holds data, `BlockOnPossibleDataLoss=True` blocks the publish; the values are irrecoverable
without a backup. Edit the CREATE; never write `ALTER`.

> **The application-side cutover is part of this change.** The Integration Studio / Service Studio
> republish order, the two sequencing rules (the app reads the new shape only after the schema
> release; the old shape drops only after the app stops writing it), and what the pull request
> names under Not verified are owned by `../../_index/multi-phase/SKILL.md` — plan no phase
> without it.

## The named trap
Dropping a column still referenced by a view/proc/computed-column/index (those break or block the
drop); and dropping before the app has genuinely stopped reading it. A populated column is blocked
by the row-presence gate — see `../../_index/tightening-class/SKILL.md`. The coexistence obligation
(the 4-phase deprecation) is the multi-phase concern — see `../../_index/multi-phase/SKILL.md`. Do
not re-derive either here.

## How it flips (the specifics only)
- column empty / provably unused, no dependents → ships as a single schema change applied in place,
  but a dev lead reviews at minimum: a drop is structurally irreversible even with no data to lose
- column holds data → `BlockOnPossibleDataLoss` blocks the drop (row-presence — see
  `../../_index/tightening-class/SKILL.md`); the loss cannot be undone, so a principal must review.
  The physical drop ships as **two releases**: R1 a pre-deploy drops the default constraint then the
  column with the model still declaring it (published once — a re-publish re-adds the column with
  its default); R2 the model drops the column as a no-op. The seed stops writing the column in the
  same change set (else `Msg 207`).
- app still reads/writes the column → coexistence required → the 4-phase deprecation (soft-deprecate
  → stop writes → verify unused → drop; see `../../_index/multi-phase/SKILL.md`) precedes the drop,
  and the running application must change to keep working
- referenced by a view/proc/index → drop those first → ordered multi-step
- >1M rows → added scrutiny: at production row counts the drop may run long — schedule a window

## Prove it
A Strict publish is blocked on `BlockOnPossibleDataLoss` when the column has data — show the blocked
publish and its message. Run `sys.dm_sql_referencing_entities` to prove nothing still references it.
Prove the ordered drop (dependents first) on a disposable copy of Dev. The clean Strict re-run after
the column is provably empty/unused is the proof. For the publish loop, see
`../../prove-on-dacpac/SKILL.md`; probes, `../../talk-to-local-sql/SKILL.md`.

## The verdict (to the developer)
"You asked to drop the attribute. On a disposable copy of Dev, SSDT refused it: the column still
holds values and two views read it, so dropping it now would lose that data for good and break
those views. The safe path is a 4-phase deprecation across releases — stop writing the column,
confirm nothing reads it, then drop it once it's provably dead. Because the values can't be
recovered afterward, a principal signs this one off. Do you know whether any application code still
writes this column, or should it be treated as live and staged through the full deprecation?"

## The reasoning (in conversation)
A drop is a sequence that ends in an irreversible act, not a single edit — the old column and the
new world have to coexist until every reader has stopped, so the change spans releases (see
`../../_index/multi-phase/SKILL.md`). How simply it ships and how dangerous it is are two separate
things: a one-statement `DROP COLUMN` can still be the most dangerous change in front of you,
because once the values are gone they're gone. The failure this avoids is blind-dropping an
"unused" column before `sys.dm_sql_referencing_entities` and a real stop-writes phase have proven
it's truly dead.

## On the record
Assemble the pull request from the `../../author-pr/SKILL.md` template; the worked instance for
this op is `../../../sample-prs/delete-attribute.md`. **SHIP terminal: TWO-RELEASE** on a populated
column (ONE-RELEASE on an empty, unused one).

**Review & release**
- A principal must review this: data is removed and the removal cannot be undone. (An empty,
  provably-unused column loses no data, but a drop is structurally irreversible — a dev lead
  reviews at minimum.)
- Ships as **two releases** after the app has stopped reading the column: R1 a pre-deploy drops the
  column with the model lagging (published once); R2 the model drops the column as a no-op. The
  seed stops writing the column in the same change set. The data-loss guard is not relaxed, because
  this pipeline cannot relax it. An empty, unused column with no dependents ships as a single schema
  change, applied in place.
- Added scrutiny, when the table is large: at production row counts the drop may block writes or
  run long — schedule a window.

**Verification** — run in each environment after deployment:
```sql
-- expect 0 rows: the column no longer exists on the table
SELECT c.name FROM sys.columns c
WHERE c.object_id = OBJECT_ID('dbo.<table>') AND c.name = '<Col>';
```
Before the final drop phase, `sys.dm_sql_referencing_entities` must return nothing for the column —
no view, procedure, computed column, or index still references it.

**Rollback.** Re-adding the column definition restores the structure, but a populated column's
values are gone once dropped and are recoverable only from a backup taken before the drop; an
empty, provably-unused column re-adds losslessly. The drop is not auto-reversed.

**Not verified**
- Application impact — whether application code outside the database still writes or reads the
  column. `sys.dm_sql_referencing_entities` sees SQL objects, not application code; app owner
  confirms the app has stopped.
- Other environments — Test, UAT, and Prod may still have live readers where Dev does not. Run the
  referencing check and the verification query before each promotion.
- Reversibility — only the forward drop is exercised on the disposable copy; the dropped values are
  not recoverable from the schema change, and the pre-drop backup is the sole restore path.
- Production scale / timing — at large row counts the drop may block writes or run long; the small
  disposable copy cannot show this.
