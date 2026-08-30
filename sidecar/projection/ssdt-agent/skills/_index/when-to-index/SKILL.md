---
name: when-to-index
description: Cross-cutting KNOWLEDGE for the additive ops — the conditions under which a new column or reference should also get an index, and when it should not. Owns the strongest trigger (a foreign key does NOT auto-index the child column — proven F11 — so every new FK column is an unindexed join until you add one), the query-shaped triggers (a column that will be filtered, looked up, joined, or sorted on a growing table), the non-conditions (low cardinality, a small or static table, a write-heavy table with no matching read), the cost (the build takes a write-blocking lock; every index slows writes and costs storage), and the recommendation shape (surface the measured condition as a fork; an index is its own additive change — recommend it, do not force it). add-index owns the HOW; this owns the WHETHER. Additive op skills POINT here.
---

# When to recommend an index — the conditions, the cost, the recommendation

> An index is not part of the change a developer asks for — it is a **performance decision** that rides
> alongside a new column or a new reference. Adding one is cheap to say and real to run, and leaving one
> out is invisible until a screen is slow. This skill owns the conditions for recommending an index, so
> every additive op raises it at the right moment and stays quiet at the wrong one.

## The strongest trigger: a foreign key does not index its child column

Proven (`../../../FINDINGS_AND_CHANGES.md` F11): SQL Server indexes the **parent** side of a foreign
key — its primary-key or unique target — but **never the child column**. So the moment you add
`FK_Order_Customer` on `Order.CustomerId`, the join `Order → Customer`, and every parent-side delete or
cascade check, scan `Order` until you add a nonclustered index on `CustomerId` yourself. **Recommend a
nonclustered index on every new foreign-key column** unless the child table is tiny or static.

## The query-shaped triggers: how will this column be read?

Recommend an index when the new column will be **read by value**, on a table that grows:
- **filtered** — a `WHERE` on it (a list screen filtered by Status, a lookup by Code);
- **joined** — used to join to another table (a foreign key is the common case, above);
- **sorted** — an `ORDER BY` the application relies on;
- **grouped** — a `GROUP BY` in a rollup or report.

The test: *name the query that will read this column.* If you can, and the table is not tiny, an index
earns its place. If you cannot name one, do not recommend it.

## The non-conditions: when NOT to index

Silence is the right output here — recommending an index the table does not need is its own cost:
- **Low cardinality** — few distinct values (a Status, a boolean, a flag). The optimizer scans past a
  low-selectivity index; it rarely helps.
- **A small or static table** — a lookup of a few dozen rows is scanned faster than an index is seeked.
- **A write-heavy table with no matching read** — every index is paid on every insert, update, and
  delete; without a read that uses it, the index is pure cost.
- **A column nothing queries** — an index no query names is dead weight.

## The cost, stated plainly

An index is not free, and the recommendation names the cost so the developer chooses with eyes open:
- The **build** takes a write-blocking lock whose duration scales with row count (`WITH (ONLINE = ON)`
  avoids it, but is Enterprise/Developer edition only) — see `../../op/add-index/SKILL.md`.
- Every index **slows writes** (each insert/update/delete maintains it) and **costs storage**.

## The recommendation shape

An index is its own additive change — **recommend it, do not force it**. Surface it to the developer as
a fork: the measured condition and its cost, and let them decide.

> "`Order.CustomerId` gets a foreign key here, and SQL Server does not index the child side, so the
> `Order → Customer` join will scan `Order`. Add a nonclustered index on `CustomerId` — in this PR, or
> as a fast follow? (The build takes a brief write-blocking lock, scaled to row count.)"

Record the answer as one line, and route the "how" to `../../op/add-index/SKILL.md`. If the developer
defers, name it under *Not checked / still open* so it is not lost. It never adds an index the developer
did not agree to — the schema is theirs.

## Who points here
- **add-index** — owns the HOW; points here for the WHETHER.
- **create-fk-clean / create-fk-orphan** — the foreign-key trigger (F11): recommend an index on the new
  child column.
- **junction** — a composite PK over `(FK1, FK2)` covers a join from `FK1`, but a join from `FK2` alone
  is not seekable on it — recommend an index on `FK2`.
- **add-optional / add-mandatory / create-entity** — the query-shaped trigger: recommend an index when
  the new column or table will be filtered, joined, sorted, or grouped.
