# 18. Decision Aids

---

## How to Use These Aids

These are quick-reference tools for in-the-moment decisions. They condense the playbook into actionable formats.

**Print them. Pin them. Reference them until you don't need to.**

---

## 18.1 "What Tier Is This?" Flowchart

```
START: You need to make a schema change
           │
           ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  QUESTION 1: Will any data be lost or destroyed?                        │
│                                                                         │
│  • Dropping a column that has data?                                     │
│  • Narrowing a column below current max length?                         │
│  • Dropping a table with rows?                                          │
│  • Changing type in a way that loses precision?                         │
└─────────────────────────────────────────────────────────────────────────┘
           │
     ┌─────┴─────┐
     │           │
    YES          NO
     │           │
     ▼           ▼
┌─────────┐    ┌─────────────────────────────────────────────────────────┐
│ TIER 4  │    │  QUESTION 2: Will existing data values change or move?  │
│         │    │                                                         │
│ Stop.   │    │  • Converting data types explicitly?                    │
│ Get     │    │  • Splitting/merging tables?                            │
│ Principal│   │  • Moving columns between tables?                       │
│ involved.│   │  • Backfilling values into existing rows?               │
└─────────┘    └─────────────────────────────────────────────────────────┘
                         │
                   ┌─────┴─────┐
                   │           │
                  YES          NO
                   │           │
                   ▼           ▼
          ┌─────────────┐    ┌─────────────────────────────────────────┐
          │   TIER 3    │    │  QUESTION 3: Do other objects depend    │
          │   minimum   │    │  on what you're changing?               │
          │             │    │                                         │
          │ Dev lead    │    │  • FKs from other tables?               │
          │ owns this.  │    │  • Views referencing this column?       │
          └─────────────┘    │  • Procs, computed columns, indexes?    │
                             │  • External systems (ETL, reports)?     │
                             └─────────────────────────────────────────┘
                                       │
                                 ┌─────┴─────┐
                                 │           │
                            CROSS-TABLE    SAME TABLE
                            OR EXTERNAL    ONLY
                                 │           │
                                 ▼           ▼
                        ┌─────────────┐    ┌─────────────────────────────┐
                        │   TIER 3    │    │  QUESTION 4: Can existing   │
                        │             │    │  app code keep working      │
                        │ Dev lead    │    │  unchanged?                 │
                        │ owns this.  │    │                             │
                        └─────────────┘    │  • Queries still valid?     │
                                           │  • No column removals?      │
                                           │  • No required new inputs?  │
                                           └─────────────────────────────┘
                                                     │
                                               ┌─────┴─────┐
                                               │           │
                                              NO          YES
                                               │           │
                                               ▼           ▼
                                      ┌─────────────┐    ┌───────────────┐
                                      │   TIER 2    │    │   TIER 1      │
                                      │   minimum   │    │               │
                                      │             │    │ Self-service  │
                                      │ Pair support│    │ with review   │
                                      │ recommended │    └───────────────┘
                                      └─────────────┘
```

**After determining base tier, check escalation triggers:**

```
┌─────────────────────────────────────────────────────────────────────────┐
│  ESCALATION TRIGGERS (add +1 tier if any apply)                         │
│                                                                         │
│  □ CDC-enabled table?                          → +1 tier minimum        │
│  □ Table has >1M rows?                         → +1 for data operations │
│  □ Production-critical timing?                 → +1 tier                │
│  □ Pattern you've never done before?           → +1 tier or get support │
│  □ Novel/unprecedented pattern?                → TIER 4 regardless      │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 18.2 "Do I Need Multi-Phase?" Checklist

**Check any that apply:**

```
DATA MOVEMENT
□ Existing data must be transformed (type conversion, format change)
□ Data is moving between tables (split, merge, column move)
□ New column needs values derived from existing data

APPLICATION COORDINATION  
□ Old and new code must coexist during transition
□ Column/table being removed is still referenced by app
□ Breaking change requires synchronized app deployment

CDC CONSTRAINTS (Production)
□ CDC-enabled table is changing structure
□ Audit continuity required (no history gaps)

IRREVERSIBILITY
□ Part of the change can't be easily undone
□ Need to verify success before proceeding to next step
□ Rollback complexity justifies separating phases

DEPENDENCIES
□ Must drop something before changing, then recreate after
□ FK relationships must be temporarily disabled
□ Index must be dropped for column change, then rebuilt
```

**Scoring:**
- **0 checked:** Likely single-phase. Proceed normally.
- **1-2 checked:** Consider multi-phase. Review the specific patterns.
- **3+ checked:** Almost certainly multi-phase. See Section 17 for templates.

---

## 18.3 "Can SSDT Handle This Declaratively?" Quick Reference

| Operation | Declarative? | Notes |
|-----------|--------------|-------|
| **CREATION** | | |
| Create table | ✅ Yes | Add .sql file |
| Create column (nullable) | ✅ Yes | Edit table file |
| Create column (NOT NULL) | ✅ Yes | Need default for existing rows |
| Create PK/FK/unique/check | ✅ Yes | Inline or separate file |
| Create index | ✅ Yes | Inline or separate file |
| Create view | ✅ Yes | Add .sql file |
| Create proc/function | ✅ Yes | Add .sql file |
| | | |
| **MODIFICATION** | | |
| Widen column | ✅ Yes | Just change the definition |
| Narrow column | ⚠️ Depends | May need pre-validation; BlockOnPossibleDataLoss guards |
| Change type (implicit) | ✅ Yes | INT→BIGINT, VARCHAR→NVARCHAR |
| Change type (explicit) | ❌ No | Needs multi-phase: add new, migrate, drop old |
| NULL → NOT NULL | ⚠️ Depends | Need default or pre-backfill |
| NOT NULL → NULL | ✅ Yes | Just change the definition |
| Rename column | ✅ Yes | **Must use refactorlog** |
| Rename table | ✅ Yes | **Must use refactorlog** |
| Add/remove IDENTITY | ❌ No | Can't ALTER to add/remove; needs table swap |
| | | |
| **CONSTRAINTS** | | |
| Add default | ✅ Yes | Inline in table definition |
| Modify default | ✅ Yes | Change the value; SSDT drops and recreates |
| Remove default | ✅ Yes | Remove from definition |
| Add FK (clean data) | ✅ Yes | Inline in table definition |
| Add FK (orphan data) | ❌ No | Need WITH NOCHECK via script, then trust |
| Enable/disable constraint | ❌ No | Script-only (operational, not declarative) |
| | | |
| **INDEXES** | | |
| Add/drop index | ✅ Yes | Add/remove definition |
| Change index columns | ✅ Yes | SSDT regenerates |
| Rebuild/reorganize | ❌ No | Maintenance operation, not schema |
| Online index operations | ⚠️ Partial | May need script for WITH (ONLINE=ON) |
| | | |
| **STRUCTURAL** | | |
| Split table | ❌ No | Multi-phase: create, migrate, drop |
| Merge tables | ❌ No | Multi-phase: create, migrate, drop |
| Move column between tables | ❌ No | Multi-phase |
| Move table between schemas | ⚠️ Partial | Declarative with refactorlog, or ALTER SCHEMA TRANSFER |
| | | |
| **CDC** | | |
| Enable/disable CDC | ❌ No | Stored procedure calls, not declarative |
| Create/drop capture instance | ❌ No | Stored procedure calls |

**Legend:**
- ✅ Yes = Pure declarative, just edit the schema files
- ⚠️ Depends/Partial = Declarative with conditions or scripted help
- ❌ No = Script-only or multi-phase required

---

## 18.4 Before-You-Start Checklist

**Copy this for every schema change:**

```
CLASSIFICATION
□ I know which table(s) this change affects
□ I've checked if any affected tables are CDC-enabled
□ I've determined the tier for this change: ___
□ I've identified the SSDT mechanism:
    □ Pure Declarative
    □ Declarative + Post-Deployment
    □ Pre-Deployment + Declarative  
    □ Script-Only
    □ Multi-Phase (releases needed: ___)

PREPARATION
□ I've reviewed the Operation Reference for relevant gotchas
□ I know who needs to review this (based on tier)
□ If rename: I will use GUI rename to create refactorlog entry
□ If NOT NULL on existing table: I have a plan for existing rows
□ If FK: I have verified no orphan data exists
□ If multi-phase: I've mapped all phases to releases

CDC (if applicable)
□ I know which capture instance(s) will be affected
□ I have a plan for instance recreation:
    □ Dev/Test: Accept gap, disable/enable
    □ Production: Dual-instance pattern

IMPLEMENTATION
□ Branch created from latest main
□ Schema changes made (declarative files updated)
□ Pre-deployment scripts added (if needed)
□ Post-deployment scripts added (if needed)
□ Scripts are idempotent

TESTING
□ Project builds successfully
□ Deployed to local database without errors
□ Verified changes in database (SSMS inspection)
□ Reviewed generated deployment script
□ If scripts: Tested idempotency (deployed twice)

READY FOR PR
□ PR template will be filled out completely
□ Appropriate reviewers identified
□ Rollback plan documented
```

---

## 18.5 CDC Impact Checker

### Step 1: Is the Table CDC-Enabled?

**Quick check:**
```sql
SELECT 
    OBJECT_SCHEMA_NAME(source_object_id) AS SchemaName,
    OBJECT_NAME(source_object_id) AS TableName,
    capture_instance
FROM cdc.change_tables
WHERE OBJECT_NAME(source_object_id) = 'YourTableName'
```

**If no results:** Table is not CDC-enabled. No CDC impact.

**If results returned:** Table is CDC-enabled. Continue to Step 2.

---

### Step 2: What Kind of Change?

| Your Change | CDC Impact? | Action Required |
|-------------|-------------|-----------------|
| Add nullable column | Yes (if you want it tracked) | Recreate capture instance |
| Add NOT NULL column | Yes (if you want it tracked) | Recreate capture instance |
| Drop column | Yes | Recreate capture instance |
| Rename column | Yes | Recreate capture instance |
| Change data type | Yes | Recreate capture instance |
| Widen column | No | Capture instance still valid |
| Add/modify constraint | No | Constraints not tracked |
| Add/modify index | No | Indexes not tracked |
| Add/drop FK | No | FKs not tracked |

---

### Step 3: Development or Production?

**Development/Test (Gap Acceptable):**
```
Pre-deployment:
  1. Disable CDC on table
  
[SSDT deploys schema change]

Post-deployment:
  2. Re-enable CDC on table (new capture instance)

⚠️ Changes during deployment window are not captured
```

**Production (No Gap):**
```
Post-deployment (after schema change):
  1. Create NEW capture instance (schema already updated)
     - Name it with version: dbo_TableName_v2
  
[Both v1 and v2 instances now active]
[Consumer code reads from both, unions results]

Next Release:
  2. Drop OLD capture instance (v1)
  3. Consumer code reads only from v2

⚠️ Requires consumer abstraction layer
```

---

### Step 4: Document in PR

```
CDC Impact: Yes

Affected Table(s):
| Table | Current Instance | Action |
|-------|------------------|--------|
| dbo.Customer | dbo_Customer_v1 | Create v2, deprecate v1 next release |

Environment Strategy:
- Dev/Test: Disable/re-enable (accepting gap)
- Production: Dual-instance pattern per Section 12
```

---

## 18.6 Tier Summary Card

**Print this. Keep it visible.**

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           TIER QUICK REFERENCE                          │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  TIER 1: Self-Service                                                   │
│  ─────────────────────                                                  │
│  • Schema-only (no data touched)                                        │
│  • Purely additive (nothing breaks)                                     │
│  • Self-contained (no dependencies)                                     │
│  • Trivially reversible                                                 │
│                                                                         │
│  Examples: Add nullable column, add table, add index, add default       │
│  Review: Any team member                                                │
│                                                                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  TIER 2: Pair-Supported                                                 │
│  ──────────────────────                                                 │
│  • Data-preserving (rows stay, structure changes)                       │
│  • Contractual (old/new can coexist)                                    │
│  • Intra-table dependencies                                             │
│  • Reversible with effort                                               │
│                                                                         │
│  Examples: Add NOT NULL with default, add FK (clean data), widen column │
│  Review: Dev lead or experienced IC                                     │
│                                                                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  TIER 3: Dev Lead Owned                                                 │
│  ──────────────────────                                                 │
│  • Data-transforming (values change/move)                               │
│  • Inter-table dependencies                                             │
│  • Breaking (synchronized deployment needed)                            │
│  • Multi-phase required                                                 │
│                                                                         │
│  Examples: Type conversion, table split, rename, FK with orphans        │
│  Review: Dev lead required                                              │
│                                                                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  TIER 4: Principal Escalation                                           │
│  ────────────────────────────                                           │
│  • Data-destructive (information lost)                                  │
│  • Cross-boundary (external systems affected)                           │
│  • Lossy (can't undo without backup)                                    │
│  • Novel/unprecedented pattern                                          │
│                                                                         │
│  Examples: Drop table with data, narrow column, major structural change │
│  Review: Principal engineer required                                    │
│                                                                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  ESCALATION TRIGGERS: +1 tier for                                       │
│  • CDC-enabled table                                                    │
│  • Table >1M rows (for data operations)                                 │
│  • Production-critical timing                                           │
│  • First time doing this operation type                                 │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 18.7 Operation Quick Reference

**One-line summaries for common operations:**

| Operation | Tier | Mechanism | Watch For |
|-----------|------|-----------|-----------|
| Add table | 1 | Declarative | Nothing — safest operation |
| Add nullable column | 1 | Declarative | Nothing |
| Add NOT NULL column | 1-2 | Declarative | Need default for existing rows |
| Add index | 1-2 | Declarative | Large table = blocking time |
| Add FK (clean data) | 2 | Declarative | Verify no orphans first |
| Add FK (orphan data) | 3 | Multi-phase | WITH NOCHECK → clean → trust |
| Add default | 1 | Declarative | Nothing |
| Add check constraint | 2 | Declarative | Existing data may violate |
| Add unique constraint | 2 | Declarative | Check for duplicates first |
| Widen column | 2 | Declarative | Index rebuild possible |
| Narrow column | 4 | Pre + Declarative | Validate data fits; BlockOnPossibleDataLoss |
| Change type (implicit) | 2 | Declarative | INT→BIGINT is safe |
| Change type (explicit) | 3-4 | Multi-phase | Add new → migrate → drop old |
| NULL → NOT NULL | 2-3 | Pre + Declarative | Backfill NULLs first |
| NOT NULL → NULL | 1-2 | Declarative | Safe; consider why |
| Rename column | 3 | Declarative + refactorlog | **Without refactorlog = data loss** |
| Rename table | 3 | Declarative + refactorlog | **Without refactorlog = data loss** |
| Drop column | 3-4 | Declarative | Follow deprecation workflow |
| Drop table | 4 | Declarative | Verify truly unused; backup |
| Add/remove IDENTITY | 3-4 | Multi-phase (table swap) | Full table rebuild |
| Split table | 4 | Multi-phase | Multiple releases |
| Merge tables | 4 | Multi-phase | Multiple releases |
| CDC table schema change | +1 tier | Environment-dependent | See CDC protocol |

---

