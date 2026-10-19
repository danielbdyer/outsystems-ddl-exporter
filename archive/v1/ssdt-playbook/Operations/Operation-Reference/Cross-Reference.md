# 16.10 Cross-Reference: Anti-Patterns and Patterns

*Each row leads with its OutSystems parallel, then the SSDT operation. `—` = no direct OutSystems equivalent; `≈` = approximate.*

---

| In OutSystems | Operation | Related Anti-Pattern | Related Multi-Phase Pattern |
|---------------|-----------|---------------------|----------------------------|
| Rename an Attribute or Entity | Rename column/table | [19.1 The Naked Rename](#191-the-naked-rename) | [17.8 Schema Migration with Backward Compatibility](#178-pattern-schema-migration-with-backward-compatibility) |
| Add a mandatory Attribute | Add NOT NULL column | [19.2 The Optimistic NOT NULL](#192-the-optimistic-not-null) | [17.2 NULL → NOT NULL](#172-pattern-null--not-null-on-populated-table) |
| Add a Reference Attribute | Add FK | [19.3 The Forgotten FK Check](#193-the-forgotten-fk-check) | [17.4 Add FK with Orphan Data](#174-pattern-add-fk-with-orphan-data) |
| Reduce an Attribute's length | Narrow column | [19.4 The Ambitious Narrowing](#194-the-ambitious-narrowing) | — |
| — *(SSDT concept)* | Refactorlog handling | [19.5 The Refactorlog Cleanup](#195-the-refactorlog-cleanup) | — |
| Change an Attribute's Data Type (incompatible) | Change type (explicit) | — | [17.1 Explicit Conversion Data Type Change](#171-pattern-explicit-conversion-data-type-change) |
| ≈ Auto Number on the Identifier | Add/remove IDENTITY | — | [17.3 Add/Remove IDENTITY](#173-pattern-addremove-identity-property) |
| Delete an Attribute | Drop column | — | [17.5 Safe Column Removal](#175-pattern-safe-column-removal-4-phase) |
| — *(no OutSystems equivalent)* | Split table | — | [17.6 Table Split](#176-pattern-table-split) |
| — *(no OutSystems equivalent)* | Merge tables | — | [17.7 Table Merge](#177-pattern-table-merge) |

---
