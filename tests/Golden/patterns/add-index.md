# add-index

A new nonclustered index that is not unique, on a table the change does not create. CREATE INDEX reads every row to
build it and holds a lock that blocks writes for as long as the build runs; no row can stop it. A unique index is
add-unique.

```text
recognize:
  match: created Index Unique=false
  ships: one release, in place; the build reads every row and blocks writes while it runs
```
