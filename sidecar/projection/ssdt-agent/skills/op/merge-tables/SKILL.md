---
name: merge-tables
description: Use when the developer says "merge CustomerAddress back into Customer", "we don't need two entities, combine them", "fold the lookup into its parent", "collapse these two entities into one" — two entities becoming one, with data moving. SSDT destination = a multi-phase add-absorbing-columns / copy / repoint / drop-absorbed-table program with a cardinality proof.
---

# Merge two entities into one (collision / silent-1:many-drop trap) — recipe AUTHORED HERE

> **AUTHORED-HERE NOTICE.** Handbook file 14 lists §17.7 "merge-entities" in its index but the template body is empty. The multi-phase recipe below is authored here to fill the gap and is the working contract; fold it back into the handbook when file 14 is completed.

> **Default (provisional — the data decides; prove before you classify).** Ships across three
> releases (three pull requests): add the absorbing columns to the survivor and copy the data, cut
> the application over, then drop the absorbed table — the two tables coexist while readers migrate.
> A dev lead must review this: existing data is moved into the survivor and the absorbed table is
> dropped. Prove cardinality (1:1) before copying anything, so a one-to-many absorbed side cannot
> silently drop rows.

## OutSystems phrasing
"merge CustomerAddress back into Customer", "we don't need two entities, combine them", "fold the lookup into its parent".

## SSDT meaning
The inverse of a split. ADD the absorbing columns to the surviving table's CREATE, copy data from the entity being absorbed, repoint every FK and view that referenced the absorbed entity, then drop the absorbed table. SSDT ADDs the columns and (if nullable) publishes clean; it will **not** copy the data, and it **blocks** the final `DROP TABLE` under `BlockOnPossibleDataLoss`. Never write ALTER.

> **The application-side cutover is part of this change.** The Integration Studio / Service Studio
> republish order, the two sequencing rules (the app reads the new shape only after the schema
> release; the old shape drops only after the app stops writing it), and what the pull request
> names under Not verified are owned by `../../_index/multi-phase/SKILL.md` — plan no phase
> without it.

## The named trap
**Collision and cardinality.** A merge silently assumes the absorbed entity is **1:1 or 1:0..1** with the survivor. If it is actually 1:many, a naive copy keeps one row per parent and **silently drops the rest** — and a value-hash will NOT flag it (the hash only compares surviving rows). Treat any merge as suspected 1:many until proven otherwise. Companion trap: a *SELECT \* View* (see `../create-view/SKILL.md`) over the absorbed entity left unenumerated through the merge. The coexistence and subtractive-licensing why is `../../_index/multi-phase/SKILL.md`; do not re-derive it.

## How it flips (the specifics only)
- absorbed entity **empty** → drop it as a clean subtractive change and add the survivor's columns
  in place, in a single release. No data to move.
- absorbed populated, **proven 1:1** with survivor → ships across three releases (the three phases
  below); a dev lead must review it, because existing data is moved into the survivor and the
  absorbed table is dropped.
- absorbed populated, **1:many** (collision) → still staged across releases, but the merge is
  *semantically wrong as stated*: STOP and tell the developer the cardinality — a design decision,
  not a change in how it ships.
- **+ CDC on either table** → added scrutiny: each table that feeds a change-data-capture stream has
  its capture instance frozen to that table's current columns, so both need capture-instance
  handling — see `../../_index/cdc/SKILL.md`.
- **+ >1M rows** → added scrutiny: at production row counts the copy may block writes or run long —
  schedule a window. **+ first time on this estate** → added scrutiny: this merge has not been
  performed here before.

**The three phases (each its own PR):**
- **Phase 1 (additive):** ADD the absorbing columns (nullable) to the survivor CREATE,
  post-deploy-copy from the absorbed entity, dual-write new rows. Prove cardinality here: count
  absorbed rows vs. distinct parents — equal = 1:1, safe; unequal = 1:many, STOP. Strict publishes
  clean. A dev lead or an experienced developer should review this: the running application must
  change to dual-write into the new columns.
- **Phase 2 (cutover):** repoint app reads, FKs, and views from the absorbed entity to the
  survivor's new columns. Leave a backward-compat view named for the absorbed entity if any external
  consumer still references it (`../compat-view/SKILL.md`).
- **Phase 3 (subtractive):** drop the absorbed table. Strict must block the drop under
  `BlockOnPossibleDataLoss` until the Phase-1 hashes prove every value is now in the survivor.

## Prove it
Phase 1 — the **row-count cardinality check** (absorbed rows == distinct parent keys) BEFORE anything else; then hash the absorbed columns vs. the new survivor columns and prove equal. Phase 3 — Strict blocks the `DROP TABLE` until the hashes match. See `prove-on-dacpac` / `talk-to-local-sql`. On the sample, merge `CustomerAddress` (seeded 1:1) into `Customer` (STR-02); a scratch seed adding a 2nd CustomerAddress row for one Customer fires the 1:many refusal (STR-02N).

## The verdict (to the developer)
You asked to merge CustomerAddress into Customer. On a disposable copy of your data I proved it's
1:1 — the row counts match — so the copy carries every row and loses nothing. If it had been
one-to-many, a straight copy would have kept one row per customer and silently dropped the rest;
that's why the count comes first, before anything is copied. The data copy publishes cleanly, and
SSDT refuses the final drop of the old table until the copy is proven complete. It ships as three
PRs: add the columns and copy, cut the app over, then drop the old table.

## The reasoning (in conversation)
A row-count proof and a value-hash are two different proofs, and the count has to come first.
"Combine these two entities" hides an unstated question — how many rows on the absorbed side per
parent — and only the data answers it. Get it wrong and a plain copy silently discards the extra
rows on any one-to-many parent, and the value-hash won't catch that, because it only compares the
rows that survived the copy. The full why — why the two tables coexist through the phases — is
`../../_index/multi-phase/SKILL.md`.

## On the record
The fragment this op contributes to the pull request (`../../author-pr/SKILL.md`).

**Review & release**
- A dev lead must review this: existing data is moved into the survivor's new columns and the
  absorbed table is dropped once the copy is proven complete.
- Ships across three releases (three pull requests): add the absorbing columns and copy from the
  absorbed table, cut the application over (repoint reads, foreign keys, and views), then drop the
  absorbed table — the two tables coexist while readers migrate, and the copy cannot be expressed as
  a table definition.
- Added scrutiny: none for a small, clean, 1:1 merge; a CDC-tracked table on either side freezes its
  capture instance to that table's current columns and needs handling on both; at >1M rows the copy
  scans the table and may block writes or run long — schedule a window; a first-time merge on this
  estate carries added scrutiny (see `../../_index/cdc/SKILL.md`).

**Verification** — run in each environment after deployment
```sql
-- expect absorbed_rows = distinct_parents: the absorbed side is 1:1 with the survivor, so the
-- copy carries every row (unequal = 1:many; stop, the merge is unsafe as stated)
SELECT
  (SELECT COUNT(*)                    FROM <absorbed>) AS absorbed_rows,
  (SELECT COUNT(DISTINCT <parentkey>) FROM <absorbed>) AS distinct_parents;

-- expect equal hashes: the absorbed columns now hold the same content on the survivor
-- (run after the copy, before the Phase-3 drop — the gate Strict enforces on DROP TABLE)
SELECT
  CONVERT(CHAR(64), HASHBYTES('SHA2_256',
    CAST((SELECT <parentkey>, <absorbed columns> FROM <absorbed>
          ORDER BY <parentkey> FOR XML RAW) AS VARBINARY(MAX))), 2) AS absorbed_hash,
  CONVERT(CHAR(64), HASHBYTES('SHA2_256',
    CAST((SELECT <parentkey>, <the same columns, now on the survivor> FROM <survivor>
          WHERE <absorbing_col> IS NOT NULL ORDER BY <parentkey> FOR XML RAW)
         AS VARBINARY(MAX))), 2) AS survivor_hash;
```

**Rollback**
Before the Phase-3 drop, backing out is lossless: drop the added survivor columns and repoint reads,
foreign keys, and views back to the absorbed table, which still holds its data. The Phase-3 drop is
not auto-reversible — once the absorbed table is dropped it is gone, and recovery means recreating it
and copying back from the survivor's columns (the values were proven equal to the absorbed originals
before the drop). Keep the absorbed table's data recoverable — a backup, or the backward-compat view
— until the drop is confirmed durable.

**Not verified**
- Application impact: the running application must dual-write into the new columns during Phase 1 and
  read the survivor after cutover; that every reader and writer has been repointed off the absorbed
  table is not confirmed here — the app owner confirms it.
- Other environments: cardinality (1:1) is proven on a disposable copy of Dev only; Test, UAT, and
  Prod may hold a 1:many parent this copy does not — run the cardinality query before the copy in
  each environment.
- External consumers: a SELECT * view or an outside reference may still read the absorbed table by
  name; the backward-compat view covers the known ones (`../compat-view/SKILL.md`), unknown ones are
  not covered.
- Production scale / timing: the copy and drop are exercised at seed scale only; blocking and
  duration at >1M rows are not shown by the small copy.
- Reversibility: only the forward merge is proven; recreating the dropped absorbed table and copying
  back is not exercised here.
