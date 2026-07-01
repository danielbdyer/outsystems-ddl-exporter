# Operations — Indexes (FAMILY INDEX)

> Index changes look harmless ("just an index") but two of them are NOT declarative at all —
> they are operational, job-owned work that has no home in the dacpac. The declarative ones
> still flip on data: a non-unique → unique change vetoes on duplicates, and a large-table
> index build blocks writes for the build's duration. Never classify an index change from the
> `.sql` text alone — the row count and the duplicate count decide the tier.

**This file is now an INDEX.** The op specifics live in the per-op skills; the shared reasoning
lives in `_index/`. Nothing here restates a guard or a flip mechanism.

## The ops (table of contents)

| Op | Per-op skill | What it is / how it flips |
|---|---|---|
| add-index | `../op/add-index/SKILL.md` | Additive, always M1 clean. Build over all rows takes a write-blocking lock; row count is the cost predictor; ONLINE=Enterprise-only. |
| modify-index | `../op/modify-index/SKILL.md` | DROP+CREATE rebuild. Non-unique→unique vetoes on duplicates → M3 Pre-Deploy. |
| drop-index | `../op/drop-index/SKILL.md` | M1 clean, reversible. "Unused" is an assumption — the proof is `sys.dm_db_index_usage_stats`, not a publish. |
| rebuild / reorganize | `../op/rebuild-index/SKILL.md` | ⚠️ OPERATIONAL — refuse-and-route. No declarative destination; a post-deploy REBUILD is anti-idempotent. |

## Shared concerns for this family

- **The unique-veto** (non-unique → unique on duplicates) is a claim about the data proven at build time → `../_index/constraint-is-a-claim/SKILL.md` (duplicate probe).
- **CDC** +1 on a tracked table → `../_index/cdc/SKILL.md`.
- Single-op couplings NOT lifted: ONLINE=Enterprise (add-index) and the operational-not-declarative one-liner (rebuild-index, shared only with toggle-trust) stay inline in their op skills.

## Handbook offset reminder
Uniform +3: file `13` = §16 (Operation Reference), `14` = §17 (patterns), `15` = §18 (decision
cascade / declarative table), `16` = §19 (anti-patterns gallery). Cite by filename.
