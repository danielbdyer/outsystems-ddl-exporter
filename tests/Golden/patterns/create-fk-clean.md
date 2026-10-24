# create-fk-clean

A new foreign key on a table the change does not create. DacFx validates it against the existing rows: a child row
with no parent stops it, and the change is then create-fk-orphan, a reconcile in a pre-deploy script first.

```text
recognize:
  match: created ForeignKeyConstraint
  ships: one release, in place, when no child row lacks its parent
```
