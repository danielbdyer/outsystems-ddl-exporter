# delete-attribute

An existing column dropped, with its default. BlockOnPossibleDataLoss stops the drop of a column on a populated table.

```text
recognize:
  match: dropped Column
  ships: two releases on a populated table; one release on an empty one
```
