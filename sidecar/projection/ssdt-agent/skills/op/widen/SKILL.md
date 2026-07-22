---
name: widen
description: Use when the developer says "make the field longer", "increase Email to 256", "give Total more precision", "make it a bigger number" — enlarging length/precision. Data-preserving; the one coupling is the index-key byte limit.
---

# Widen length/type

> **Default (provisional — the data decides).** Ships as a single schema change, applied in place —
> no data is read or written. Any team member can review it: the change is additive and the running
> application is unaffected. Prove before you classify — the couplings below can move both findings.

## OutSystems phrasing
"make the field longer", "increase Email to 256", "give Total more precision".

## SSDT meaning
Enlarge length/precision (`NVARCHAR(100)`→`NVARCHAR(200)`, `DECIMAL(10,2)`→`DECIMAL(18,2)`,
`INT`→`BIGINT`). Every existing value still fits, so it is data-preserving. SSDT emits `ALTER
COLUMN` to the wider type. Edit the CREATE; never write `ALTER`.

## The named trap
**Index-key byte limit** — a column inside a non-clustered index key cannot push the key past
1700 bytes (900 in older versions); widening it then fails. And `NVARCHAR` widening **doubles
storage** vs `VARCHAR`, which can tip an index over that limit. This is a single-op coupling (it
recurs only here), so it stays inline — not lifted to an index skill.

## How it flips (the specifics only)
- widen a non-indexed column → ships in place, no data read or written; any team member can review it
- the column sits in an index key and the widen blows the byte limit → the publish is blocked / build
  complains → drop/redesign the index → a multi-step single PR
- very large table on an old SQL version → may rebuild rather than run metadata-only → added scrutiny
  at >1M rows: the rebuild can block writes or run long, so schedule a window

## Prove it
Strict publishes clean; the delta is `ALTER COLUMN` to the wider type, with nothing blocked and no
rebuild of unrelated objects. If the column is indexed, prove the index either survives or is the only
thing the delta also touches. For the publish loop, see `../../prove-on-dacpac/SKILL.md`.

## The verdict (to the developer)
Widening keeps every value you already have, so it publishes clean and applies in place — nothing gets
read or rewritten. The one thing I checked was structural, not data: this column isn't inside an index
whose key would blow the byte limit, and the NVARCHAR size doubling didn't tip any index over. Anyone
on the team can review it.

## The reasoning (in conversation)
Widening is data-preserving by definition: only the data could make a type change unsafe, and a wider
type can't — every existing value still fits. So the risk here is structural — the index byte budget —
not the data. Widen and narrow are mirror images: widening's risk is the byte budget, narrowing's is
whether the longest existing value (`MAX(LEN)`) still fits. The mistake to avoid is treating a type
change as obviously safe without looking one hop out at what the column participates in — the index it
sits in.

## On the record
The fragment this operation contributes to the pull request (`../../author-pr/SKILL.md`).

**Review & release**
- Any team member can review this: the change is additive and the running application is unaffected.
- Ships as a single schema change, applied in place. No data is read or written.
- Added scrutiny, only where the condition holds (otherwise "None."):
  - Indexed near the limit — the column sits in an index key and the widen pushes it toward the
    1700-byte limit; the index is redesigned in the same PR, which a dev lead should review.
  - Large table on an older SQL version (>1M rows) — the change rebuilds rather than altering
    metadata and may block writes or run long; schedule a window.

**Verification** — run in each environment after deployment
```sql
-- expect the widened type/length (NVARCHAR(256) → max_length 512 bytes; DECIMAL(18,2) → precision 18)
SELECT c.name, t.name AS type_name, c.max_length, c.precision, c.scale
FROM sys.columns c
JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID('dbo.<table>') AND c.name = '<column>';
```

**Rollback** — Narrowing back to the original type is lossless only while no stored value exceeds the
old length; once a longer value has been written, `ALTER COLUMN` to the narrower type is blocked or
truncates. The forward widen changes no data, so reversing the schema is safe immediately after
deployment, before any wider value is stored.

**Not verified**
- Application impact — a wider column is additive to callers, but a client that assumed the old length
  (a fixed-size buffer, a downstream contract) is not exercised on the disposable copy; the application
  owner confirms it tolerates the new length.
- Other environments — row counts and SQL Server version differ; a widen that is metadata-only here may
  rebuild where the table is larger or the server older.
- Production scale / timing — if the change rebuilds, blocking or duration at production row counts is
  not shown by the disposable copy.
- Reversibility — only the forward widen is proven; narrowing back is lossless only before a longer
  value is stored (see Rollback).
