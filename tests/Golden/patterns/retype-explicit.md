# retype-explicit

An existing column given another data type, with or without another length, precision or scale. The kernel judges no
type wider or narrower than another (decision 2.9), so every type change is retype-explicit until the Fits
precondition counts no value that fails to convert: v2's retype-implicit, a type every value fits, holds no block.
BlockOnPossibleDataLoss stops the change on a populated table.

```text
recognize:
  match: altered Column DataType Length:any IsMax:any Precision:any Scale:any
  ships: several releases on a populated table (a new column, the values converted, the old one dropped); one release on an empty one
```
