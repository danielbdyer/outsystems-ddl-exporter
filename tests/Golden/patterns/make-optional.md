# make-optional

An existing column made NULL. DacFx writes no BlockOnPossibleDataLoss check for it, and every existing value stays; the
risk is a reader that assumed a value in every row.

```text
recognize:
  match: altered Column Nullable:false→true
  ships: one release, in place
```
