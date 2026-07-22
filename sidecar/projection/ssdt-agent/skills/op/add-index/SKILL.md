---
name: add-index
description: Use when the developer says "add an index on Customer.Email", "make this attribute searchable", "the list screen is slow, can we index it" — adding a nonclustered index. Additive and ships as a single declarative schema change, but the build runs over every row and takes a write-blocking lock.
---

# Add an index

> **Default (provisional — the data decides).** Ships as a single declarative schema change, applied in place — additive, nothing lost. Any team member can review it: the change is additive and the running application is unaffected. But the *build cost* — a write-blocking lock whose duration scales with row count — lives in the data, not the `.sql`.

## OutSystems phrasing
"add an index on Customer.Email", "make this attribute searchable", "the list screen is slow, can we index it".

## SSDT meaning
An index definition added to the table's `.sql` (inline `INDEX IX_Customer_Email (Email)` or a separate `CREATE NONCLUSTERED INDEX` object). SSDT's publish engine emits `CREATE INDEX` and **builds the index over every existing row** at deploy time — a real, blocking operation on a populated table, not a metadata flip.

## The named trap
Not a §19 anti-pattern by name — the silent cost is the trap: a non-`ONLINE` build takes a schema-modification lock and **blocks all writes for the build's duration** (an unasked-for outage on a big table). `WITH (ONLINE = ON)` makes it non-blocking but is **Enterprise/Developer edition only** — it fails on Standard. This ONLINE=Enterprise coupling is a single-op concern; it stays inline here (not lifted).

## How it flips (the specifics only)
- table empty / small → ships as a single declarative schema change, applied in place; any team member can review it — additive, the running application is unaffected, and the build is trivial.
- table populated, large (the build blocks writes) → still a single declarative schema change, but the build adds scrutiny: at production row counts it takes a write-blocking lock and may run long, so an experienced developer should review it and the maintenance window is named.
- \+ >1M rows → the write-outage risk is acute: a dev lead should review it, and `ONLINE = ON` may be required (Enterprise-gated) to avoid blocking writes for the build's duration.
- ONLINE=ON needed but target is Standard → the declarative build stays; flag that the online option is unavailable on Standard, and the build will block writes for its duration.

## Prove it
Build the dacpac, run Strict `sqlpackage /Action:Script`, and confirm the delta is a clean `CREATE INDEX` with **no drop, no table rebuild** — a clean Strict publish confirms the change ships as a single declarative schema change. The disposable copy is small, so the observed build time is not the production build time — **row count is the predictor**; say so explicitly when the target is large. See `../../prove-on-dacpac/SKILL.md` + `../../talk-to-local-sql/SKILL.md`.

## The verdict (to the developer)
"Adding the index is additive — I published it to a disposable copy of your data, SSDT just ran CREATE INDEX, and nothing was lost. The one caveat is the build itself: on the real table it locks writes while it runs, and how long that lasts scales with the row count. On a large table we'd either run it online (that needs SQL Server Enterprise) or schedule it in a low-traffic window. Do you know which edition the target is, and when a good window would be?"

## The reasoning (in conversation)
The thing to hold onto: separate what the engine does from what it costs. What it does is always the same — CREATE INDEX, a clean additive change with nothing lost. What it costs is a write-blocking lock whose duration scales with the row count, and only the row count tells you how long. The failure this avoids is shipping an index blind and blocking production writes for an unplanned stretch.

## On the record
Contributes to the pull request (`../../author-pr/SKILL.md`):

**Review & release**
- Any team member can review this: the change is additive and the running application is unaffected.
- Ships as a single declarative schema change: SSDT emits `CREATE INDEX` and builds the index over every existing row.
- Added scrutiny, when the target table is large: at production row counts the build takes a write-blocking lock and may block writes or run long — schedule a window, or use `WITH (ONLINE = ON)` where the edition is Enterprise/Developer.

**Verification** — run in each environment after deployment
```sql
-- expect 1 row, is_disabled = 0: the index landed and is enabled
SELECT name, type_desc, is_disabled
FROM sys.indexes
WHERE object_id = OBJECT_ID('dbo.Customer') AND name = 'IX_Customer_Email';
```

**Rollback**
`DROP INDEX IX_Customer_Email ON dbo.Customer;` — lossless: the index holds no source data, only a derived structure. Re-adding it incurs the same write-blocking build cost.

**Not verified**
- Production build time and lock duration. The disposable copy is small, so its build time is not the production build time; row count is the predictor and is not exercised here.
- Write impact at production scale. Whether the build blocks live writes long enough to matter depends on production row count and concurrency, neither of which the copy shows.
- Target edition. `ONLINE = ON` requires Enterprise/Developer; on Standard the build blocks regardless. The target's edition is not confirmed here.
