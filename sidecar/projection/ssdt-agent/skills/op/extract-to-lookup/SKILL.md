---
name: extract-to-lookup
description: Use when the developer says "turn this text Status column into a proper Status entity", "these string values should be a lookup so we stop typos", "promote the free-text StatusText into a real reference entity" — replacing a free-text column with a Static Entity + FK. SSDT destination = a multi-phase create-lookup / seed / add-FK / backfill / drop-old-column program.
---

# Extract free-text column to a lookup entity (Forgotten-FK-Check / lost-unmapped-values trap)

> **Default (provisional — the data decides; prove before you classify).** Ships across releases
> (multiple pull requests): create the lookup, seed it with the distinct existing values, add the FK
> column, backfill, then drop the old free-text column — the old and new representations coexist
> while readers migrate. A dev lead must review this: existing data is moved into a new shape and a
> cross-table relationship is added. Prove the mapping is total before the drop, so no value silently
> becomes NULL.

## OutSystems phrasing
"turn this text Status column into a proper Status entity", "these string values should be a lookup so we stop typos".

## SSDT meaning
A multi-step transform: **create** the lookup table, **seed** it with the distinct existing values, **add** a new FK column to the source, **backfill** by joining text → lookup key, then **drop** the old free-text column. Data is read, reshaped, and moved; old and new representations coexist during the transition. Never write ALTER.

## The named trap
Doing it in one publish and silently losing the unmapped values — any source text with no seeded lookup row becomes NULL, or blocks the FK from validating. This is the **Forgotten-FK-Check** face (handbook 16 = §19.3) *and* a coexistence move. The staging/coexistence why is `../../_index/multi-phase/SKILL.md`; the seed leg's guarded MERGE + explicit IDs are `../../_index/idempotent-seed/SKILL.md`. Do not re-derive either here.

## How it flips (the specifics only)
- clean distinct values, all mappable → ships across releases (multiple PRs); a dev lead must review
  it, because existing data is moved into a new shape and a cross-table relationship is added.
- **unmapped / dirty source values present** → still staged across releases, but a pre-backfill
  reconcile is required so nothing maps to NULL; if any value is lost, a principal must review it,
  because data is removed and the removal cannot be undone.
- **+ CDC-tracked** / **+ >1M rows** → added scrutiny — see `../../_index/cdc/SKILL.md`.

## Prove it
Prove the mapping is **total** BEFORE dropping the old column:
`SELECT DISTINCT <oldcol> FROM <t> WHERE <oldcol> NOT IN (SELECT Code FROM <lookup>)` must return **zero rows**. Each phase must publish Strict-clean; the drop phase is blocked under Strict until the backfill hash proves the new shape holds every value (see `../../_index/multi-phase/SKILL.md` for the totality proof). See `prove-on-dacpac` / `talk-to-local-sql` for the loop. On the sample, promote `dbo.Order.StatusText` ('Pending'/'Shipped'/'Cancelled') into a Status FK (STA-03); a scratch seed with an unmapped value fires the total-mapping negative.

## The verdict (to the developer)
"You asked to promote that free-text Status column into a proper Status lookup. That moves the
existing values into a new shape behind a new foreign key, so it can't be done in one publish — it
stages across a few releases (several PRs), with the old text column and the new lookup living side
by side until every reader has moved to the FK. On a disposable copy of Dev, before the old column is
dropped, I proved every existing value maps to a seeded lookup row — zero unmapped — so nothing
silently becomes NULL. Because this moves existing data and adds a relationship, a dev lead should
review it. If the current values aren't clean, some may have no home in the lookup yet — do you know
whether every StatusText value is one of the expected set, or should we plan a reconcile pass for the
stragglers before the backfill?"

## The reasoning (in conversation)
When data *moves into a new shape* rather than the schema merely *gaining* shape, the application
can't switch over in one step — the old text column and the new lookup have to coexist while readers
migrate, so the change stages across releases, and the mapping is proven total before the old column
is dropped. The failure this avoids is the one-publish version that silently turns unmapped values
into NULL. The full why — why the staging is required — is `../../_index/multi-phase/SKILL.md`.

## On the record
The fragment this op contributes to the pull request (`../../author-pr/SKILL.md`).

**Review & release**
- A dev lead must review this: existing data is moved into a new shape and a cross-table relationship
  (the lookup foreign key) is added. If any source value has no lookup row and would be lost, a
  principal must review this: data is removed and the removal cannot be undone.
- Ships across releases (multiple pull requests): create the lookup table, seed it with the distinct
  existing values, add the FK column, backfill by joining text → lookup key, then drop the old
  free-text column — the old and new representations coexist while readers migrate, and the seed and
  backfill cannot be expressed as a table definition.
- Added scrutiny: none for a small, clean source; a CDC-tracked table freezes its capture instance to
  the current columns and needs handling; at >1M rows the backfill scans the table and may block
  writes or run long — schedule a window (see `../../_index/cdc/SKILL.md`).

**Verification — run in each environment after deployment**
```sql
-- expect 0 rows: every source value maps to a seeded lookup row (the mapping is total)
SELECT DISTINCT <oldcol> FROM <t> WHERE <oldcol> NOT IN (SELECT Code FROM <lookup>);

-- expect 0 rows: the backfill left no source row without a lookup key
SELECT * FROM <t> WHERE <fkcol> IS NULL;
```

**Rollback**
The final phase drops the old free-text column — a subtractive change. Because the mapping was proven
total before the drop, the original text is reconstructable by joining the new FK column back to the
lookup's Code; re-adding the column and backfilling from that join restores it. The lookup table and
the FK drop cleanly (`ALTER TABLE <t> DROP CONSTRAINT FK_...; DROP TABLE <lookup>;`). The column drop
is not auto-reversed; any value that was reconciled rather than directly mapped is restored from the
reconcile's recorded originals, not from the join.

**Not verified**
- Application impact: application code that reads or writes the old free-text column directly, rather
  than through the new FK, breaks once the column is dropped; that every reader and writer has moved
  to the FK is not confirmed here — the app owner confirms it.
- Other environments: the distinct source values were enumerated on a disposable copy of Dev only;
  Test, UAT, and Prod may hold values that were never seeded into the lookup — run the total-mapping
  query before promotion in each environment.
- Production scale / timing: the seed, backfill, and drop are exercised at seed scale only; blocking
  and duration at >1M rows are not shown by the small copy.
- Reversibility: only the forward transform is proven; re-adding and re-backfilling the dropped
  column from the lookup join is not exercised here.
