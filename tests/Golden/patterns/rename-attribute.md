# rename-attribute

An existing column renamed through its refactorlog entry, which DacFx plans as sp_rename and which keeps every value.
A rename without its entry is refused before this block is read.

```text
recognize:
  match: renamed Column
  ships: one release, in place
```
