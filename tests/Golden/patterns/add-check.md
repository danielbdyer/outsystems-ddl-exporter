# add-check

A new check on a table the change does not create. DacFx validates it against the existing rows, and a row that breaks
it stops the publish, so a reconcile in a pre-deploy script comes first.

```text
recognize:
  match: created CheckConstraint
  ships: one release, in place, when no row breaks the check
```
