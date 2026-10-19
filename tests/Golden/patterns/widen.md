# widen

An existing column given a greater length, max, or a greater precision. Every value fits the new type.

```text
recognize:
  match: altered Column Length:rises
  match: altered Column IsMax:false→true Length:any
  match: altered Column Precision:rises
  ships: one release, in place
```
