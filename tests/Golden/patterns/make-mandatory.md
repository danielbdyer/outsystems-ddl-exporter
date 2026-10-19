# make-mandatory

An existing column made NOT NULL. BlockOnPossibleDataLoss stops it on a populated table, even one with no NULL in the
column.

```text
recognize:
  match: altered Column Nullable:true→false
  ships: two releases on a populated table; one release, in place, on an empty one
```
