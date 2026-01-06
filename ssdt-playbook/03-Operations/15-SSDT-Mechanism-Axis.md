# 15. The SSDT Mechanism Axis

---

## What This Section Covers

The dimension framework tells you **how risky** a change is. The tier system tells you **who handles it**. The mechanism axis tells you **how to implement it** — which SSDT features and scripts you'll use.

Every change uses one of five mechanisms. Knowing which mechanism applies helps you plan your implementation correctly.

---

## The Five Mechanisms

```
┌─────────────────────────────────────────────────────────────────────────┐
│                                                                         │
│   1. PURE DECLARATIVE                                                   │
│      Just edit the .sql files. SSDT does everything.                    │
│                                                                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│   2. DECLARATIVE + POST-DEPLOYMENT                                      │
│      Schema change + data migration script that runs after.             │
│                                                                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│   3. PRE-DEPLOYMENT + DECLARATIVE                                       │
│      Data prep script first, then schema change.                        │
│                                                                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│   4. SCRIPT-ONLY                                                        │
│      SSDT can't handle it. Entirely scripted operation.                 │
│                                                                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│   5. MULTI-PHASE                                                        │
│      Spans multiple deployments. Each phase may use different mechanisms.│
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Mechanism 1: Pure Declarative

**What it means:** You edit the `.sql` file(s) to express the desired end state. SSDT compares your project to the target database and generates the necessary DDL.

**You write:**
```sql
-- In your table definition file
[MiddleName] NVARCHAR(50) NULL,
```

**SSDT generates:**
```sql
ALTER TABLE [dbo].[Person] ADD [MiddleName] NVARCHAR(50) NULL;
```

**When to use:**
- Adding tables, columns, indexes, constraints
- Simple modifications (widening columns, changing nullability NULL→NOT NULL when safe)
- Removing objects (with appropriate settings)
- Renames (with refactorlog)

**When it's NOT sufficient:**
- Existing data needs transformation
- Existing data might violate new constraints
- SSDT would generate unsafe or inefficient DDL

**Typical tiers:** Tier 1-2

---

## Mechanism 2: Declarative + Post-Deployment

**What it means:** The schema change is declarative, AND you need a post-deployment script to handle data work that must happen after the new structure exists.

**Execution order:**
1. SSDT applies schema changes (new column exists)
2. Post-deployment script runs (populates new column)

**Example:** Adding a column and backfilling it from existing data

**Schema change (declarative):**
```sql
[FullName] NVARCHAR(200) NULL,
```

**Post-deployment script:**
```sql
UPDATE dbo.Person
SET FullName = FirstName + ' ' + LastName
WHERE FullName IS NULL
```

**When to use:**
- New columns need initial values derived from existing data
- Seed data for lookup tables
- Data migrations that depend on new structure existing

**Key requirement:** Post-deployment scripts must be idempotent. They'll run on every deployment.

**Typical tiers:** Tier 2-3

---

## Mechanism 3: Pre-Deployment + Declarative

**What it means:** Data must be prepared BEFORE the schema change can succeed. The pre-deployment script runs first, then SSDT applies the declarative change.

**Execution order:**
1. Pre-deployment script runs (cleans/prepares data)
2. SSDT applies schema changes

**Example:** Making a column NOT NULL when NULLs currently exist

**Pre-deployment script:**
```sql
UPDATE dbo.Customer
SET Email = 'unknown@placeholder.com'
WHERE Email IS NULL
```

**Schema change (declarative):**
```sql
[Email] NVARCHAR(200) NOT NULL,  -- Changed from NULL
```

**When to use:**
- Adding NOT NULL constraint to column with existing NULLs
- Adding FK constraint when orphan data must be cleaned first
- Adding check constraint when existing data might violate
- Dropping dependencies before a column change

**Key requirement:** Pre-deployment scripts must also be idempotent.

**Typical tiers:** Tier 2-3

---

## Mechanism 4: Script-Only

**What it means:** SSDT's declarative model can't express this operation, or would handle it poorly. You script the entire operation manually.

**What SSDT can't handle declaratively:**
- `ENABLE`/`DISABLE` constraints
- `ALTER SCHEMA TRANSFER` (moving objects between schemas)
- CDC enable/disable
- Online index operations with specific options
- Complex data transformations that must be atomic with schema changes

**Example:** Enabling CDC on a table

```sql
-- This goes in post-deployment; it's not a schema definition
EXEC sys.sp_cdc_enable_table
    @source_schema = 'dbo',
    @source_name = 'Customer',
    @role_name = 'cdc_reader',
    @capture_instance = 'dbo_Customer_v1',
    @supports_net_changes = 1
```

**When to use:**
- CDC management
- Schema transfers
- Constraint enable/disable
- Complex index operations
- Anything SSDT doesn't model

**Risk:** You're outside SSDT's safety net. No `BlockOnPossibleDataLoss` protection. Extra care required.

**Typical tiers:** Tier 3-4

---

## Mechanism 5: Multi-Phase

**What it means:** The complete change requires multiple sequential deployments. Each phase may use a different mechanism.

**Why multi-phase is necessary:**
- Can't rollback safely after certain points
- Old and new code must coexist during transition
- Need to verify success before proceeding
- CDC constraints require sequenced instance management

**Example:** Explicit data type conversion (VARCHAR → DATE)

**Phase 1 (Release N):** Declarative + Post-Deployment
- Add new DATE column (declarative)
- Migrate data with conversion (post-deployment)

**Phase 2 (Release N+1):** Pure Declarative
- Drop old VARCHAR column (declarative)
- Rename new column to original name (declarative + refactorlog)

**When to use:**
- Explicit type conversions
- Table structural changes (split, merge)
- Any breaking change requiring backward compatibility period
- CDC-enabled table changes in production

**Key discipline:** Each phase must be independently deployable and rollback-able. Document the complete sequence before starting.

**Typical tiers:** Tier 3-4

---

## Mechanism Decision Guide

```
Can SSDT handle this change purely through .sql file edits?
  │
  ├─► YES → Does existing data need transformation?
  │           │
  │           ├─► YES → Does transformation need new structure first?
  │           │           │
  │           │           ├─► YES → DECLARATIVE + POST-DEPLOYMENT
  │           │           │
  │           │           └─► NO → Does data need prep before schema change?
  │           │                       │
  │           │                       ├─► YES → PRE-DEPLOYMENT + DECLARATIVE
  │           │                       │
  │           │                       └─► NO → Review; probably post-deployment
  │           │
  │           └─► NO → PURE DECLARATIVE
  │
  └─► NO → Can it be done in a single deployment?
              │
              ├─► YES → SCRIPT-ONLY
              │
              └─► NO → MULTI-PHASE
```

---

## Mechanism by Common Operation

| Operation | Typical Mechanism |
|-----------|-------------------|
| Add table | Pure Declarative |
| Add nullable column | Pure Declarative |
| Add NOT NULL column (new table) | Pure Declarative |
| Add NOT NULL column (populated table) | Pre-Deployment + Declarative or Declarative + Post-Deployment (depending on default strategy) |
| Add FK (clean data) | Pure Declarative |
| Add FK (orphan data) | Multi-Phase (NOCHECK → clean → trust) |
| Add index | Pure Declarative |
| Widen column | Pure Declarative |
| Narrow column | Pre-Deployment + Declarative (after validation) |
| Change type (implicit) | Pure Declarative |
| Change type (explicit) | Multi-Phase |
| NULL → NOT NULL | Pre-Deployment + Declarative |
| NOT NULL → NULL | Pure Declarative |
| Rename column | Pure Declarative (with refactorlog) |
| Drop column | Pure Declarative (with deprecation workflow) |
| Enable CDC | Script-Only |
| Seed lookup table | Declarative + Post-Deployment |
| Split table | Multi-Phase |
| Add/remove IDENTITY | Multi-Phase |

---

## Combining Mechanism with Tier

The mechanism tells you *how*; the tier tells you *who* and *how carefully*:

| Mechanism | Common Tier Range | Notes |
|-----------|-------------------|-------|
| Pure Declarative | 1-3 | Varies by operation risk |
| Declarative + Post-Deployment | 2-3 | Data work adds complexity |
| Pre-Deployment + Declarative | 2-3 | Data cleanup adds risk |
| Script-Only | 3-4 | Outside SSDT safety net |
| Multi-Phase | 3-4 | Inherently complex |

A Tier 1 change is almost always Pure Declarative. A Tier 4 change might use any mechanism (or multiple across phases).

---

## Documenting Your Mechanism

In your PR, specify:
1. Which mechanism you're using
2. If multi-phase: what each phase does and which release it belongs to
3. If script-only: why SSDT can't handle it declaratively

This helps reviewers understand your implementation approach and verify it's appropriate for the change.
