---
name: retype-implicit
description: Use when the developer says "change to a bigger number", "INT to BIGINT", "make this VARCHAR into NVARCHAR", "store it as a wider type" — a widening/implicit type change where every value already fits. Lossless.
---

# Retype implicit (widening conversion)

> **Default (provisional — the data decides).** A true widening is data-preserving — every value
> already fits the bigger type, so there is nothing for the engine to refuse. Ships as a single
> schema change, applied in place: no data is read or written, and any team member can review it,
> since the running application is unaffected. Prove the direction is widening — not
> value-reshaping — before classifying it.

## OutSystems phrasing
"change to a bigger number", "INT to BIGINT", "make this VARCHAR into NVARCHAR".

## SSDT meaning
Change the column's data type in the widening direction (`INT`→`BIGINT`,
`VARCHAR(n)`→`NVARCHAR(n)`, `DECIMAL(10,2)`→`DECIMAL(18,2)`). These are lossless — SSDT emits a
single `ALTER COLUMN`. Edit the CREATE; never write `ALTER`.

## The named trap
None material for a true widening — every value converts. The one edge: `VARCHAR`→`NVARCHAR`
**doubles storage**, which can tip an indexed column over the index-key byte limit (see
`../widen/SKILL.md`). If the "retype" is actually value-reshaping (Text→Date, DATETIME→DATE),
it is NOT this op — route to `../retype-explicit/SKILL.md`.

## How it flips (the specifics only)
- implicit/widening, all values convert → ships as a single schema change, applied in place; no
  data is read or written. Any team member can review it — the change is data-preserving and the
  running application is unaffected.
- VARCHAR→NVARCHAR on an indexed column → storage doubles → the index-key byte-limit edge (see
  `../widen/SKILL.md`)
- direction is actually narrowing/reshaping → **wrong op** → `../retype-explicit/SKILL.md`
- CDC-enabled / >1M rows → added scrutiny (see `../../_index/cdc/SKILL.md`): a change-data-capture
  instance is frozen to the table's current columns and needs handling, and at production row
  counts the `ALTER COLUMN` rewrite may block writes or run long — schedule a window.

## Prove it
Strict publishes clean; the delta is one `ALTER COLUMN` to the wider type — not blocked, and no
rebuild of unrelated objects. If the delta ever does more than the single ALTER, stop and check
whether the conversion is actually value-reshaping. For the publish loop, see
`../../prove-on-dacpac/SKILL.md`.

## The verdict (to the developer)
"You asked to go INT→BIGINT (or store it as a wider type). That's a widening: every existing value
already fits the bigger type, so SSDT just widens the column in place with a single ALTER — nothing
to refuse, and no data to move. The one thing I confirm is the direction: a true widening is free,
but if this is really a value-reshaping change like Text→Date, not every value converts and it
becomes a different, staged job. For a VARCHAR→NVARCHAR I also check the column isn't inside an
index whose key would blow the byte limit once storage doubles. Assuming it's a genuine widening,
this is a clean one-liner."

## The reasoning (in conversation)
Direction is everything. A widening retype is free because every existing value already fits the
wider type, so nothing has to be proven row by row — only the explicit, value-reshaping sibling
needs that. The failure to avoid is treating "retype" as a single thing: before promising anything,
ask whether this is a widening (free) or a value-reshaping cast where each row has to be proven to
convert.

## On the record
The fragment this operation contributes to the pull request (`../../author-pr/SKILL.md`). The base
finding holds for any genuine widening; add the added-scrutiny lines only when the table is
CDC-tracked or large.

**Review & release**
- Genuine widening: `Any team member can review this: the change is data-preserving — every value
  already fits the wider type — and the running application is unaffected.` · `Ships as a single
  schema change, applied in place. No data is read or written.`
- Added scrutiny, when it applies: `Added scrutiny: this table feeds a change-data-capture stream,
  so the capture instance is frozen to the current columns and needs handling.` · `Added scrutiny:
  at production row counts the ALTER COLUMN rewrite may block writes or run long — schedule a
  window.`

**Verification** — run in each environment after deployment:
```sql
-- expect the wider type (e.g. bigint, or nvarchar(n)): the column ends at the widened definition
SELECT c.name, t.name AS type_name, c.max_length, c.precision, c.scale
FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID('<t>') AND c.name = '<Col>';
```

**Rollback** — narrowing the column back to its original type reverses the change; it is lossless
only while no value wider than the original type has been written. The reverse is the narrowing
direction and carries its truncation risk.

**Not verified**
- Application impact — a widened column can change how strongly-typed application code handles it
  (an Int32 mapping now backing a BIGINT column, VARCHAR vs NVARCHAR handling); application-side
  type handling is not confirmed here.
- Other environments — the type change is proven on the disposable copy only; run the verification
  query in each environment before promotion.
- Production scale / timing — whether the `ALTER COLUMN` is metadata-only or a size-of-data rewrite
  at production row counts is not shown by the small copy.
- The index-key byte limit — for VARCHAR→NVARCHAR, that the widened column does not push a
  non-clustered index key past the byte limit is proven only against this copy's indexes (see
  `../widen/SKILL.md`).
- Reversibility — only the forward widening is exercised; narrowing back is the lossy direction.
