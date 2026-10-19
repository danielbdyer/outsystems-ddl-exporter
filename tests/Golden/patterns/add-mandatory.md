# add-mandatory

A new NOT NULL column on a table the change does not create. With a default, DacFx gives every existing row the
default as it adds the column; without one, a populated table refuses it.

```text
recognize:
  match: created Column Nullable=false Expression=NULL
  ships: one release with a default; two releases on a populated table without one
```
