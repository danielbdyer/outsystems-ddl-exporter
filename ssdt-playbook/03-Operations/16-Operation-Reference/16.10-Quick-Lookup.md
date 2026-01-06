# 16.10 Quick Lookup: All Operations by Tier

### Tier 1: Self-Service

| Operation | Mechanism |
|-----------|-----------|
| Create table | Declarative |
| Add nullable column | Declarative |
| Add default constraint | Declarative |
| Add check constraint (new table) | Declarative |
| Add index (small table) | Declarative |
| Create view | Declarative |
| Create synonym | Declarative |
| NOT NULL → NULL | Declarative |

### Tier 2: Pair-Supported

| Operation | Mechanism |
|-----------|-----------|
| Add NOT NULL column (with default) | Declarative |
| Add FK (clean data) | Declarative |
| Add unique constraint | Declarative |
| Add check constraint (existing data) | Declarative |
| Add index (large table) | Declarative |
| Widen column | Declarative |
| Change type (implicit) | Declarative |
| NULL → NOT NULL | Pre-deployment + Declarative |
| Add manual audit columns (existing) | Post-deployment |
| Enable Change Tracking | Script + Declarative |
| Create indexed view | Declarative |

### Tier 3: Dev Lead Owned

| Operation | Mechanism |
|-----------|-----------|
| Rename column | Declarative + Refactorlog |
| Rename table | Declarative + Refactorlog |
| Add FK (orphan data) | Multi-Phase |
| Change cascade behavior | Declarative |
| Drop column (with deprecation) | Multi-Phase |
| Change type (explicit) | Multi-Phase |
| Add/remove IDENTITY | Multi-Phase |
| Move table between schemas | Declarative + Refactorlog |
| Enable CDC | Script-Only |
| CDC table schema change | Multi-Phase |
| Add system-versioned temporal (existing) | Multi-Phase |
| Extract to lookup table | Multi-Phase |

### Tier 4: Principal Escalation

| Operation | Mechanism |
|-----------|-----------|
| Drop table with data | Declarative (guarded) |
| Narrow column | Pre-deployment + Declarative |
| Split table | Multi-Phase |
| Merge tables | Multi-Phase |
| Move column between tables | Multi-Phase |
| Any data-destructive operation | Varies |
| Novel/unprecedented patterns | Case-by-case |

---
