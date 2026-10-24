# add-optional

A new column that allows NULL, on a table the change does not create. Every existing row takes NULL, so no row can
stop it. A computed column, or a period column of temporal-convert, is not this operation.

```text
recognize:
  match: created Column Nullable=true Expression=NULL GeneratedAlwaysType=ColumnGeneratedAlwaysType.None
  ships: one release, in place
```
