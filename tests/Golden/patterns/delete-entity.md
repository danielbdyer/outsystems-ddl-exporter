# delete-entity

An existing table dropped. BlockOnPossibleDataLoss stops the drop of a populated table, so the drop is a scripted DROP
TABLE in the release that removes the table's file.

```text
recognize:
  match: dropped Table
  ships: one release, with a scripted DROP TABLE
```
