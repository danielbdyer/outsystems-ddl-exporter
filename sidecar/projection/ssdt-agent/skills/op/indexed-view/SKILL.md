---
name: indexed-view
description: Use when the developer says "materialize this aggregation for speed", "cache the joined view", "make the summary view fast", "store the results of this view physically" — a SCHEMABINDING view with a unique clustered index. SSDT destination = a declarative view + UNIQUE CLUSTERED index that physically stores results and binds its base columns.
---

# Indexed / materialized view (SCHEMABINDING) (deterministic-only / base-column-binding trap)

> **Default (provisional — the data decides).** On a small or empty base it ships as a single
> declarative change — the view and its unique clustered index, applied in place — and an
> experienced developer or a dev lead reviews it, because it is a stored object that binds its base
> tables, not a plain view. As the base grows, the unique-clustered-index build becomes expensive
> and blocking, and it ships as a scripted or staged change instead. Prove it on a disposable copy
> before classifying.

## OutSystems phrasing
"materialize this aggregation for speed", "cache the joined view", "make the summary view fast".

## SSDT meaning
A view with `WITH SCHEMABINDING` plus a `UNIQUE CLUSTERED` index, which causes SQL Server to
**physically store** the view's result and maintain it on every base-table write. Unlike a plain
view, this one holds data, costs storage and write-time, and **binds the base tables** so they
cannot be altered out from under it. Never write ALTER.

## The named trap
The **deterministic-only / SCHEMABINDING coupling.** An indexed view requires every expression to
be deterministic (no `GETDATE()`, no non-deterministic functions) and `WITH SCHEMABINDING`, so no
bound base column can be changed without first dropping the index and the view. A "trivial"
base-table widen then fails because the indexed view binds the column — SSDT's delta shows the
index and view **dropped and rebuilt**, expensive and blocking on a large base. This is the closest
a view comes to the table-rebuild family (`../identity-swap/SKILL.md` owns that mechanic;
cross-referenced here). A single-op coupling, kept inline rather than lifted.

## How it flips (the specifics only)
- **Small base table** → ships as a single declarative change, single-phase; an experienced
  developer or a dev lead reviews it, because it is a stored object that binds its base tables, not
  a plain view.
- **Large base table** → the unique-clustered-index build is expensive and blocking → ships as a
  scripted single release scheduled with care, or staged across releases; a dev lead must review
  it, and added scrutiny applies: at production row counts the build may block writes or run long,
  so schedule a window.
- Any later change to a **bound base column** → SSDT must drop and rebuild the indexed view → that
  downstream change carries the added cost of the rebuild; prove the rebuild in the delta.
- **Base with >1M rows** → added scrutiny: the maintenance cost on every base-table write is real
  and ongoing.

## Prove it
Preview the delta and confirm the unique clustered index is built, not just a view `CREATE`. Then
prove the binding cost: edit a bound base column and show that SSDT's delta **drops and rebuilds**
the indexed view — that rebuild is the standing price of materialization, and it belongs in front
of the developer before the change is committed. See `prove-on-dacpac`. On the sample, add
`WITH SCHEMABINDING` + `UNIQUE CLUSTERED` to `dbo.vOrderSummary` in a scratch edit (VIE-04).

## The verdict (to the developer)
Materializing this view stores its results physically and keeps them in sync on every write — that
is the read speed you are after. The cost is that it binds the tables underneath: on a disposable
copy of Dev, changing a bound column forced SSDT to drop and rebuild the whole indexed view, which
is expensive and blocking on a large table. So this buys fast reads at the price of making future
schema changes to those base columns heavier. If those columns are stable it is a good trade; if
you expect to keep reshaping them, that is worth weighing before you commit.

## The reasoning (in conversation)
Materialization is not a read-only optimization. It imposes a standing cost on every future change
to the bound columns: the speed-up is bought with rigidity downstream. The failure it is easy to
walk into is a later "trivial" base-column widen turning into a blocking indexed-view rebuild that
nobody scheduled — which is exactly what proving the rebuild in the delta lets you see coming.

## On the record
The fragment this operation contributes to the pull request (`author-pr`), in the record register.

**Review & release**
- An experienced developer or a dev lead should review this: it adds a stored object that binds its
  base tables and maintains a materialized result on every base-table write, not a plain view. On a
  large base, a dev lead must review it.
- Ships as a single declarative change on a small or empty base — the view and its unique clustered
  index, applied in place. On a large base it ships as a scripted single release scheduled with
  care, or staged across releases, because the unique-clustered-index build reads and materializes
  the whole base and is expensive and blocking.
- Added scrutiny: at production row counts the unique-clustered-index build may block writes or run
  long — schedule a window. The materialized result is then maintained on every base-table write, a
  standing write-time cost. Any later change to a bound base column forces SSDT to drop and rebuild
  the indexed view.

**Verification — run in each environment after deployment**
```sql
-- expect one row: dbo.vOrderSummary carries a UNIQUE CLUSTERED index (the result is materialized)
SELECT v.name AS view_name, i.name AS index_name, i.type_desc, i.is_unique
FROM sys.views v
JOIN sys.indexes i ON i.object_id = v.object_id
WHERE v.name = 'vOrderSummary' AND i.type_desc = 'CLUSTERED' AND i.is_unique = 1;
```

**Rollback**
Drop the view — `DROP VIEW dbo.vOrderSummary` — which removes its unique clustered index with it
and releases the SCHEMABINDING on the base columns; dropping the unique clustered index alone
(`DROP INDEX <name> ON dbo.vOrderSummary`) de-materializes but keeps the view. Lossless to the base
tables: the materialized result is derived and is discarded, and reads revert to computing the
aggregation on the fly.

**Not verified**
- Production scale and timing. The disposable copy is small; the cost of building the unique
  clustered index at production row counts — how long it runs and how long it blocks base-table
  writes — is not shown here.
- Write-path impact. Every base-table insert, update, and delete now also maintains the materialized
  view; the added write latency under production load is not measured on the copy.
- Application impact. A later change to a bound base column will require dropping and rebuilding the
  indexed view; whether any pending base-table change depends on that is not confirmed against the
  running application.
- Reversibility. Only the forward create is proven on the copy; the drop above is stated, not
  exercised.
