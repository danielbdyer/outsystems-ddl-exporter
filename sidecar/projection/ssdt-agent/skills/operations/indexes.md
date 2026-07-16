# Operations — Indexes (FAMILY INDEX)

> Index changes look harmless ("just an index") but two of them are NOT declarative at all —
> they are operational, job-owned work that has no home in the dacpac. The declarative ones
> still flip on data: a non-unique → unique change is blocked on duplicates, and a large-table
> index build blocks writes for the build's duration. Never classify an index change from the
> `.sql` text alone — the row count and the duplicate count decide how it ships and who must
> review it.

**This file is now an INDEX.** The op specifics live in the per-op skills; the shared reasoning
lives in `_index/`. Nothing here restates a guard or the specifics of how a change flips.

## The ops (table of contents)

| Op | Per-op skill | What it is / how it flips |
|---|---|---|
| add-index | `../op/add-index/SKILL.md` | Additive; ships as a single schema change, applied in place. Build over all rows takes a write-blocking lock; row count is the cost predictor; ONLINE=Enterprise-only. |
| modify-index | `../op/modify-index/SKILL.md` | DROP+CREATE rebuild. Non-unique→unique is blocked on duplicates; ships as a pre-deployment script that resolves them first, then the rebuild lands validated. |
| drop-index | `../op/drop-index/SKILL.md` | Ships as a single schema change, reversible. "Unused" is an assumption — the proof is `sys.dm_db_index_usage_stats`, not a publish. |
| rebuild / reorganize | `../op/rebuild-index/SKILL.md` | ⚠️ OPERATIONAL — refuse-and-route. No declarative destination; a post-deploy REBUILD is anti-idempotent. |

## Shared concerns for this family

- **The blocked unique change** (non-unique → unique on duplicates) is a claim about the data, proven at build time → `../_index/constraint-is-a-claim/SKILL.md` (duplicate probe).
- **CDC** adds scrutiny on a tracked table → `../_index/cdc/SKILL.md`.
- Single-op couplings NOT lifted: ONLINE=Enterprise (add-index) and the operational-not-declarative one-liner (rebuild-index, shared only with toggle-trust) stay inline in their op skills.

## Handbook offset reminder
Uniform +3: file `13` = §16 (Operation Reference), `14` = §17 (patterns), `15` = §18 (decision
cascade / declarative table), `16` = §19 (anti-patterns gallery). Cite by filename.
