---
name: drop-index
description: Use when the developer says "we don't need that index anymore", "remove the index, it's not used" — dropping an index. Loses no data (always publishes clean), but "not used" is an assumption; the real proof is usage evidence, not a publish.
---

# Drop an index

> **Default (provisional — the data decides).** Ships as a single schema change, applied in place
> — no data is read or written, and the drop reverses by re-creating the index. Any team member
> can review it when the index is genuinely unused. But the risk here is behavioral (a slower
> query), not structural (lost rows), so the honest proof lives outside the dacpac: usage
> evidence, not a clean publish. Prove "unused" before classifying.

## OutSystems phrasing
"we don't need that index anymore", "remove the index, it's not used".

## SSDT meaning
Delete the index definition from the `.sql`. SSDT emits `DROP INDEX`. No row data is touched — an
index is derived structure, so dropping it loses no information.

## The named trap
No data loss, but a **silent performance regression** — the index might be the one keeping a hot
query fast, and "not used" is an assumption until proven. Recognize it when the developer says "I
don't think anything uses it" without evidence. None material to the publish.

## How it flips (the specifics only)
- Genuinely unused index → ships as a single schema change, applied in place; any team member can
  review it, because no data is lost and the drop reverses cleanly.
- Index backs a hot query / FK lookup → still a single in-place schema change, but the silent
  performance regression means a dev lead or an experienced developer should review it; prove
  "unused" from usage evidence first.
- On a CDC-tracked table → added scrutiny: the table feeds a change-data-capture stream and is
  high-stakes (see `../../_index/cdc/SKILL.md`).

## Prove it
A disposable copy of Dev carries no production query load, so the real proof is **usage evidence**,
not a publish. Before the drop, usage stats from a prod-shaped source are the standard of proof:
`SELECT * FROM sys.dm_db_index_usage_stats WHERE object_id = OBJECT_ID('<table>')` — zero
user_seeks / user_scans / user_lookups over a representative window is the evidence. On the
disposable copy, confirm the delta is a clean `DROP INDEX` with no collateral DROP of a constraint
that depended on it. See `../../prove-on-dacpac/SKILL.md` + `../../talk-to-local-sql/SKILL.md`.

## The verdict (to the developer)
Dropping the index loses no data — it reverses cleanly, since re-creating the index puts it right
back. The catch is that "not used" is a guess until the numbers back it up. Before this ships, the
usage stats settle it: zero seeks over a representative window means it's safe to drop; if it's
quietly backing a hot query, dropping it slows that query down with nothing to warn anyone. Is
there a prod-shaped source we can pull those usage stats from?

## The reasoning (in conversation)
A clean publish isn't a green light when the risk is behavioral (a slower query) rather than
structural (lost rows). SSDT lets an index drop through every time — it only guards against losing
rows, and an index holds none — so for a behavioral risk the evidence is measured usage, not
whether the publish succeeds. The failure to avoid: trusting a hunch that "nobody uses it" because
the deploy came back clean.

## On the record
Fragments for the pull request (`../../author-pr/SKILL.md`), record register.

**Review & release**
- Any team member can review this when usage evidence shows the index is unused: no data is lost
  and the drop reverses by re-creating the index.
- Ships as a single schema change, applied in place. No data is read or written.
- Added scrutiny, when the index backs a hot query or FK lookup: dropping it is a silent
  performance regression, so a dev lead or an experienced developer should review it — and "unused"
  must be proven from usage evidence first.
- Added scrutiny, when the table is CDC-tracked: it feeds a change-data-capture stream and is
  high-stakes (see `../../_index/cdc/SKILL.md`).

**Verification** — run in each environment after deployment
```sql
-- Before the drop, in each environment (from a prod-shaped source): expect zero
-- user_seeks / user_scans / user_lookups over a representative window — the index is unused.
SELECT * FROM sys.dm_db_index_usage_stats WHERE object_id = OBJECT_ID('<table>');

-- After deployment, in each environment: expect 0 rows — the index is gone.
SELECT name FROM sys.indexes
WHERE object_id = OBJECT_ID('<table>') AND name = '<index>';
```

**Rollback**
Re-create the index from its definition (revert the `.sql` edit and republish); SSDT emits
`CREATE INDEX`. Lossless — an index holds no source data, only a derived structure — but
re-creating it runs a write-blocking build whose duration scales with row count.

**Not verified**
- Application impact — a disposable copy carries no production query load, so whether any query
  depends on this index, and would slow down once it is gone, is not shown by the publish. Usage
  evidence from a prod-shaped source is what settles it (@app-owner).
- Other environments — usage patterns differ by environment; zero seeks in one environment's window
  does not prove zero in Test, UAT, or Prod. Run the usage query in each before promotion.
- Reversibility — re-creating the index restores the structure, but the rebuild time and the
  write-blocking lock at production row counts are not measured on the small copy.
