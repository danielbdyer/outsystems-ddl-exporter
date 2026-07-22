# 16.9 Quick Lookup: All Operations by Tier

*Each row leads with its OutSystems parallel, then the SSDT operation. `—` = no direct OutSystems equivalent; `≈` = approximate.*

### Tier 1: Self-Service

| In OutSystems | Operation | Mechanism |
|---------------|-----------|-----------|
| Create an Entity | Create table | Declarative |
| Add an optional Attribute | Add nullable column | Declarative |
| Set a Default Value | Add default constraint | Declarative |
| — *(no OutSystems equivalent)* | Add check constraint (new table) | Declarative |
| Add an Index | Add index (small table) | Declarative |
| Make an Attribute optional | NOT NULL → NULL | Declarative |

### Tier 2: Pair-Supported

| In OutSystems | Operation | Mechanism |
|---------------|-----------|-----------|
| Add a mandatory Attribute | Add NOT NULL column (with default) | Declarative |
| Add a Reference Attribute | Add FK (clean data) | Declarative |
| Make an Attribute unique | Add unique constraint | Declarative |
| — *(no OutSystems equivalent)* | Add check constraint (existing data) | Declarative |
| Add an Index | Add index (large table) | Declarative |
| Increase an Attribute's length | Widen column | Declarative |
| Change an Attribute's Data Type | Change type (implicit) | Declarative |
| Make an Attribute mandatory | NULL → NOT NULL | Pre-deployment + Declarative |
| Add audit Attributes | Add manual audit columns (existing) | Post-deployment |

### Tier 3: Dev Lead Owned

| In OutSystems | Operation | Mechanism |
|---------------|-----------|-----------|
| Rename an Attribute | Rename column | Declarative + Refactorlog |
| Rename an Entity | Rename table | Declarative + Refactorlog |
| Add a Reference Attribute (existing data) | Add FK (orphan data) | Multi-Phase |
| Change the Delete Rule | Change cascade behavior | Declarative |
| Delete an Attribute | Drop column (with deprecation) | Multi-Phase |
| Change an Attribute's Data Type (incompatible) | Change type (explicit) | Multi-Phase |
| ≈ Auto Number on the Identifier | Add/remove IDENTITY | Multi-Phase |
| — *(modules aren't schemas)* | Move table between schemas | Declarative + Refactorlog |
| — *(no OutSystems equivalent)* | Add system-versioned temporal (existing) | Multi-Phase |
| Extract a Static Entity | Extract to lookup table | Multi-Phase |

### Tier 4: Principal Escalation

| In OutSystems | Operation | Mechanism |
|---------------|-----------|-----------|
| Delete an Entity | Drop table with data | Scripted DROP (declarative delete is a phantom) |
| Reduce an Attribute's length | Narrow column | Pre-deployment + Declarative |
| — *(no OutSystems equivalent)* | Split table | Multi-Phase |
| — *(no OutSystems equivalent)* | Merge tables | Multi-Phase |
| ≈ Move an Attribute to another Entity | Move column between tables | Multi-Phase |
| — | Any data-destructive operation | Varies |
| — | Novel/unprecedented patterns | Case-by-case |

---
