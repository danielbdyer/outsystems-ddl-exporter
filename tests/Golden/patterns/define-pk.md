# define-pk

A new primary key on a table the change does not create; a new table's key stands in its create-entity. A clustered key
builds over every existing row, and a duplicate key stops it with Msg 1505, a NULL key column with Msg 8111. A key
changed from one definition to another is drop-pk then define-pk.

```text
recognize:
  match: created PrimaryKeyConstraint
  ships: one release; the build reads every row, when no two rows share the key and no key column holds NULL
```
