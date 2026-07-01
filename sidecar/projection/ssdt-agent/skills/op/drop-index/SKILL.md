---
name: drop-index
description: Use when the developer says "we don't need that index anymore", "remove the index, it's not used" — dropping an index. Loses no data (always publishes clean), but "not used" is an assumption; the real proof is usage evidence, not a publish.
---

# Drop an index

> **Default (provisional — the data decides).** Mechanism 1 Pure Declarative, single-phase, Tier 1 — reversible, no data loss. But the risk is behavioral (a slower query), so the honest proof lives outside the dacpac.

## OutSystems phrasing
"we don't need that index anymore", "remove the index, it's not used".

## SSDT meaning
Delete the index definition from the `.sql`. SSDT emits `DROP INDEX`. No row data is touched — an index is derived structure, so dropping it loses no information.

## The named trap
No data loss, but a **silent performance regression** — the index might be the one keeping a hot query fast, and "not used" is an assumption until proven. Recognize it when the developer says "I don't think anything uses it" without evidence. None material to the publish.

## How it flips (the specifics only)
- genuinely unused index → M1, single-phase, Tier 1.
- index backs a hot query / FK lookup → still M1 mechanically, but the regression risk pushes review to **Tier 2**; prove "unused" first.
- \+ CDC-enabled table → **+1 Tier** (high-stakes table — see `../../_index/cdc/SKILL.md`).

## Prove it
The proving ground carries no production query load, so the real proof is **usage evidence**, not a publish. Before dropping, demand usage stats from a prod-shaped source: `SELECT * FROM sys.dm_db_index_usage_stats WHERE object_id = OBJECT_ID('<table>')` — zero user_seeks / user_scans / user_lookups over a representative window is the evidence. On the proving ground, confirm the delta is a clean `DROP INDEX` with no collateral DROP of a constraint that depended on it. See `../../prove-on-dacpac/SKILL.md` + `../../talk-to-local-sql/SKILL.md`.

## Verdict to the developer
"Dropping the index loses no data — it's pure declarative and reversible. Before we ship it, I want the usage stats: if it's had zero seeks over the last month it's safe; if it's backing a hot query, dropping it slows that query down silently."

## Teach it (the graduation)
A clean publish is not a green light when the risk is *behavioral* (a slower query) rather than *structural* (lost rows) — for those, the evidence is measured usage, not a veto. Fail mode avoided: trusting a hunch that "nobody uses it".
