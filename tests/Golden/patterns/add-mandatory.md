# add-mandatory

A new NOT NULL column on a table the change does not create. With a default, DacFx gives every existing row the
default as it adds the column; without one, a populated table refuses it. A computed column, or a period column of
temporal-convert, is not this operation.

```text
recognize:
  match: created Column Nullable=false Expression=NULL GeneratedAlwaysType=ColumnGeneratedAlwaysType.None
  ships: one release with a default; two releases on a populated table without one
```
