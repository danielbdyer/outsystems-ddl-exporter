# add-unique

A new UNIQUE constraint or unique index, filtered or not, on a table the change does not create. SQL Server builds it
over every existing row, and two rows with one key stop it with Msg 1505; a unique index that is not filtered admits
one NULL, so two NULLs stop it too.

```text
recognize:
  match: created UniqueConstraint
  match: created Index Unique=true
  ships: one release, in place, when no two rows share the key
```
