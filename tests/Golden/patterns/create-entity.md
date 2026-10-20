# create-entity

A new table, with its columns, keys, indexes and defaults. No existing row is read or written. A system-versioned
table is temporal-new, and a history table is part of the entry of the table it keeps the history of.

```text
recognize:
  match: created Table RetentionUnit=-2
  ships: one release, in place
```
