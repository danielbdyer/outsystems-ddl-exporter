# In-flight multi-phase changes

Every multi-phase change (`skills/_index/multi-phase/SKILL.md`) holds a row here from the
merge of its first phase until its final phase ships. The row names what ships next and the
date its current window closes — and the CI gate fails the build on any row whose window has
passed, so a stalled Phase 2 is a red check, not a memory. Advancing a phase updates the row;
re-dating a window is a conscious act the reviewer signs off in the citing PR; shipping the
final phase deletes the row.

`window closes` is `YYYY-MM-DD` (machine-checked). An example row, fenced so it never parses
as live state:

```
| CHG-0412 | split Customer → Customer + CustomerAddress | 2 | 3 | cut reads over; then drop old columns | 2026-09-15 | #712 |
```

| id | change | phase | of | next action | window closes | PR |
|---|---|---|---|---|---|---|
