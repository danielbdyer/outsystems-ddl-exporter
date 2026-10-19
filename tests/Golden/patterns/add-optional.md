# add-optional

A new column that allows NULL, on a table the change does not create. Every existing row takes NULL, so no row can
stop it.

```text
recognize:
  match: created Column Nullable=true Expression=NULL
  ships: one release, in place
```
