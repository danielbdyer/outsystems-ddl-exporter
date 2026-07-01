---
name: add-unique
description: Use when the developer says "this attribute should be unique", "no two customers can share an email", "stop duplicate codes" — adding a UNIQUE constraint or unique index. Builds enforcement over all existing rows; duplicates veto, and one-NULL-only bites nullable columns.
---

# Add a unique constraint

> **Default (provisional — the data decides).** Mechanism 1 Pure Declarative, single-phase, Tier 2 when the data already satisfies uniqueness — but PROVE no duplicates (and no multi-NULL) first.

## OutSystems phrasing
"this attribute should be unique", "no two customers can share an email", "stop duplicate codes".

## SSDT meaning
`CONSTRAINT UQ_<Table>_<Col> UNIQUE (<Col>)` (or a unique index). SSDT builds the uniqueness enforcement over **all existing rows** at deploy.

## The named trap
**Duplicates veto the build** — the deploy fails the instant two rows share a value. Second trap: **UNIQUE treats NULL as a value and allows exactly ONE NULL row**; a nullable column with several NULLs also vetoes — the fix for legitimate multi-NULL is a **filtered unique index**: `CREATE UNIQUE INDEX … WHERE <Col> IS NOT NULL`. Both are the constraint-is-a-claim veto-on-a-value family — see `../../_index/constraint-is-a-claim/SKILL.md` (incl. the UNIQUE-one-NULL + filtered-index remedy); do not re-derive the claim mechanics here.

## How it flips (the specifics only)
- no duplicates, no multi-NULL problem → M1, single-phase, Tier 2 (prove it).
- duplicates PRESENT → **FLIP to M3 Pre-Deploy+Declarative, single-PR**: pre-deploy de-dupe clears them BEFORE the unique build, Tier 3 — see `../../_index/constraint-is-a-claim/SKILL.md`.
- nullable column with >1 NULL row → filtered unique index, OR resolve the NULLs first; stays single-PR if the filtered index suffices.
- \+ >1M rows → **+1 Tier** (build + dedupe cost).
- \+ CDC-enabled → high-stakes table; apply the team's +1 rule (see `../../_index/cdc/SKILL.md`) even though CDC does not track the constraint.

## Prove it
Run the duplicate probe FIRST: `SELECT <Col>, COUNT(*) FROM <table> GROUP BY <Col> HAVING COUNT(*) > 1` (and a NULL count for nullable columns). Then build + Strict publish: clean → uniqueness holds; a build failure ("duplicate key was found") is the veto. Author the pre-deploy de-dupe (or the filtered index), re-run Strict clean. See `../../prove-on-dacpac/SKILL.md` + `../../talk-to-local-sql/SKILL.md`. Seed: Status's `UX_Status_Code` is the clean positive; Product's `DUPE` rows drive the veto flip.

## Verdict to the developer
"You asked to make Email unique. I published it to a copy of your data and SSDT refused — 4 rows share an email. This is a Pre-Deploy + Declarative change: I dedupe those 4 first, then the unique constraint builds clean. Proven. (If multiple blank emails are legitimate, I'll use a filtered unique index that ignores NULLs instead.)"

## Teach it (the graduation)
Before adding any uniqueness rule, run `GROUP BY … HAVING COUNT(*) > 1` and a NULL count — the answer decides the mechanism, the SQL never can. See `../../_index/constraint-is-a-claim/SKILL.md`. Fail mode avoided: shipping a unique constraint that vetoes on real duplicates.
