---
name: split-table
description: Use when the developer says "split Customer into Customer and CustomerAddress", "pull the address fields out into their own entity", "this entity is doing too much, break it up" — one entity becoming two, with data moving. SSDT destination = a multi-phase additive-CREATE / copy / cutover / drop-old-columns program.
---

# Split one entity into two (the one-release trap; per-column renames with no refactorlog entry)

> **Default (provisional — the data decides; prove before you classify).** Ships across three
> releases (three pull requests): CREATE the new table and copy the moving columns, cut the
> application over, then drop the old columns from the source — the old and new shapes coexist while
> readers migrate. A dev lead must review this: existing data is moved into a new table and a
> cross-table relationship is added. Prove the source table is empty first — an empty source
> collapses this to a single additive release any team member can review.

## OutSystems phrasing
"split Customer into Customer and CustomerAddress", "pull the address fields out into their own entity", "break this entity up".

## SSDT meaning
CREATE the new table, copy the moving columns' data keyed by the source PK, add the FK, then (much later) drop the old columns from the source CREATE. SSDT will CREATE the new table and ADD the FK declaratively but will **never move the data** — the copy is a post-deploy script, and the column drop is blocked under `BlockOnPossibleDataLoss` until the copy is proven. Handbook file 14 (=§17.6). Never write ALTER.

> **The application-side cutover is part of this change.** The Integration Studio / Service Studio
> republish order, the two sequencing rules (the app reads the new shape only after the schema
> release; the old shape drops only after the app stops writing it), and what the pull request
> names under Not verified are owned by `../../_index/multi-phase/SKILL.md` — plan no phase
> without it.

## The named trap
Treating it as one PR — dropping the old columns in the same release that creates the new table
breaks any app code still reading them. The related trap is a **rename with no refactorlog entry**
applied per-column, when the developer "renames" columns into the new table instead of copying: SSDT
reads that as drop-and-add and the column's data is lost — see
`../../_index/identity-and-refactorlog/SKILL.md`. The coexistence and subtractive-licensing why is
`../../_index/multi-phase/SKILL.md`; do not re-derive either.

## How it flips (the specifics only)
- new entity, **source table empty** (greenfield split) → collapses to a single additive release:
  just CREATE both tables, no data to move, and any team member can review it. Prove the source is
  empty first.
- source populated, app reads old columns → ships across three releases (three PRs); a dev lead must
  review it, because existing data is moved into a new table and a cross-table relationship is added.
  Phase 1 additive CREATE + FK + copy + dual-write · Phase 2 repoint reads · Phase 3 drop the old
  columns, where `BlockOnPossibleDataLoss` blocks the deployment until the copy is proven.
- **+ >1M rows / first-time** → added scrutiny: the copy is a long-running batched operation; at
  production row counts it may block writes or run long — schedule a window.

## Prove it
Phase 1 — Strict publishes the additive CREATE+FK clean; the post-deploy copy runs; hash the moving
columns source-vs-new-table and prove **equal**. Phase 3 — edit the source CREATE to drop the moved
columns; Strict MUST block on data loss, and only the proven-equal Phase-1 hash licenses the drop
(see `../../_index/multi-phase/SKILL.md` for the totality proof). See `prove-on-dacpac` /
`talk-to-local-sql` for the loop. On the sample, split `Customer` into `Customer` + `CustomerAddress`
(STR-01; the seeded 1:1 CustomerAddress row is the copy target).

## The verdict (to the developer)
Splitting Customer into two entities moves data, so it can't be one release. On a disposable copy of
Dev I proved the additive half publishes clean and copies all N rows — the source and new-table
hashes match — and that the column-drop half is blocked until that copy is proven: SSDT refuses to
drop the old columns while it can't see that the values already arrived. It ships as three PRs, in
order: create the new table and copy, repoint the reads, then drop the old columns.

## The reasoning (in conversation)
Any change that relocates data between shapes is additive, then cutover, then subtractive, and the
proof that licenses the subtractive phase is a before/after hash showing the new home holds every
value. The failure this avoids is a one-release split that either breaks live reads — app code still
on the old columns the moment they're dropped — or loses data, when the copy had a bug nobody caught.
The full why — why the phases are required — is `../../_index/multi-phase/SKILL.md`.

## On the record
The fragment this op contributes to the pull request (`../../author-pr/SKILL.md`).

**Review & release**
- A dev lead must review this: existing data is moved into a new table (CustomerAddress) and a
  cross-table relationship (the foreign key back to Customer) is added.
- Ships across three releases (three pull requests): create the new table and copy the moving
  columns, cut the application over (repoint reads), then drop the old columns from the source — the
  old and new shapes coexist while readers migrate, and the copy cannot be expressed as a table
  definition.
- Added scrutiny: none for a greenfield or small, clean split; at >1M rows the copy is a long-running
  batched operation that may block writes or run long — schedule a window; a first-time split on this
  estate carries added scrutiny.

**Verification** — run in each environment after deployment
```sql
-- expect equal hashes: the moving columns hold the same content in the new table as in the source
-- (run after the copy, before the Phase-3 column drop — the gate Strict enforces under
--  BlockOnPossibleDataLoss)
SELECT
  CONVERT(CHAR(64), HASHBYTES('SHA2_256',
    CAST((SELECT <sourcepk>, <moving columns> FROM <source>
          ORDER BY <sourcepk> FOR XML RAW) AS VARBINARY(MAX))), 2) AS source_hash,
  CONVERT(CHAR(64), HASHBYTES('SHA2_256',
    CAST((SELECT <sourcepk>, <the same columns, now in the new table> FROM <newtable>
          ORDER BY <sourcepk> FOR XML RAW) AS VARBINARY(MAX))), 2) AS newtable_hash;

-- expect 0 rows: every source row has its copy in the new table (the copy carried all N rows)
SELECT s.<sourcepk> FROM <source> s
LEFT JOIN <newtable> n ON n.<sourcefk> = s.<sourcepk> WHERE n.<sourcefk> IS NULL;
```

**Rollback**
Before the Phase-3 column drop, backing out is lossless: the moving columns still live in the source,
so dropping the new table and its foreign key and repointing reads back leaves the source whole. The
Phase-3 drop is not auto-reversible — once the old columns are dropped they are gone from the source,
and recovery means re-adding them and copying back from the new table (whose values were proven
hash-equal to the source before the drop). Keep the moving columns recoverable — the new table, or a
backup — until the drop is confirmed durable.

**Not verified**
- Application impact: the running application must dual-write into the new table during Phase 1 and
  read it after cutover; that every reader and writer has been repointed off the old columns before
  they drop is not confirmed here — the app owner confirms it.
- Other environments: the copy's completeness (source and new-table hashes equal) is proven on a
  disposable copy of Dev only; Test, UAT, and Prod hold their own rows — run the verification queries
  before the drop in each environment.
- Production scale / timing: the copy and column drop are exercised at seed scale only; blocking and
  duration at >1M rows are not shown by the small copy.
- Reversibility: only the forward split is proven; re-adding the dropped columns and copying back from
  the new table is not exercised here.
