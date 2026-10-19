# narrow

An existing column given a smaller length, a length in place of max, or a smaller precision or scale.
BlockOnPossibleDataLoss stops it on a populated table, even one whose values all fit.

```text
recognize:
  match: altered Column Length:falls
  match: altered Column IsMax:true→false Length:any
  match: altered Column Precision:falls
  match: altered Column Scale:falls
  match: altered Column Precision:falls Scale:falls
  ships: two releases on a populated table; one release, in place, on an empty one
```
