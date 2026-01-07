# 11. Multi-Phase Evolution

---

## Why Some Changes Can't Be Atomic

Some schema changes can't safely happen in a single deployment:

- **Data dependencies:** New structure needs data from old structure
- **Application coordination:** Old and new code must coexist during transition
- **Risk management:** Each phase can be validated before proceeding
- **CDC constraints:** Audit continuity requires careful sequencing

**The fundamental pattern:** Create new → Migrate data → Remove old

---

## Phase-to-Release Mapping

Not all phases can share a release. The question is: "Can we safely rollback if something goes wrong after this phase?"

| Phase combination | Same release? | Rationale |
|-------------------|---------------|-----------|
| Create new structure + migrate data | Often yes | Rollback = drop new structure, data still in old |
| Migrate data + drop old structure | **No** | Rollback impossible — old structure is gone |
| Create + migrate | Maybe | Depends on data volume and complexity |
| Anything touching CDC | Often separate | Need to verify capture instances are correct |

**Rule of thumb:** If the phase is irreversible (data loss, structure removal), it should be a separate release with explicit verification before proceeding.

---

## Rollback Considerations

At each phase, know your rollback:

| Phase | Rollback approach |
|-------|-------------------|
| Created new column/table | Drop it |
| Migrated data to new structure | Leave it (or delete if needed) |
| Dropped old column/table | Restore from backup |
| Changed CDC instance | Recreate old instance (with gap) |

**Point of no return:** Once you drop the old structure, rollback requires backup restoration. Make sure you've validated before crossing that line.

---

## Multi-Phase Operations Catalog

These operations typically require multi-phase treatment:

| Operation | Why Multi-Phase | See Pattern |
|-----------|-----------------|-------------|
| Explicit data type conversion | Data must transform; old and new must coexist | 17.1 |
| NULL → NOT NULL on populated table | Existing NULLs need values first | 17.2 |
| Add/remove IDENTITY | Can't ALTER to add IDENTITY; requires table swap | 17.3 |
| Add FK with orphan data | Need to clean data or use NOCHECK→trust sequence | 17.4 |
| Safe column removal | Verify unused before dropping | 17.5 |
| Table split | New structure + data migration + app coordination | 17.6 |
| Table merge | Same as split, reverse direction | 17.7 |
| Rename with compatibility | Old name must keep working during transition | 17.8 |
| CDC-enabled table schema change | Capture instance management | 17.9 |

---

