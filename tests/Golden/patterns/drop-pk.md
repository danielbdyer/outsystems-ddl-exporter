# drop-pk

An existing primary key dropped from a table the change keeps. A key a foreign key references fails the build with
SQL71516; an unreferenced one drops in one statement, and the table is a heap afterwards unless the change defines
another key.

```text
recognize:
  match: dropped PrimaryKeyConstraint
  ships: one release, in place, when no foreign key references the key
```
