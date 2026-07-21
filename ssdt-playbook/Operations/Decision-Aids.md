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
          │ owns this.  │    │  • Computed columns that use it?        │
          └─────────────┘    │  • Indexes on this column?              │
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

| In OutSystems | Operation | Declarative? | Notes |
|---------------|-----------|--------------|-------|
| **CREATION** | | | |
| Create an Entity | Create table | ✅ Yes | Add .sql file |
| Add an optional Attribute | Create column (nullable) | ✅ Yes | Edit table file |
| Add a mandatory Attribute | Create column (NOT NULL) | ✅ Yes | Need default for existing rows |
| Identifier, Reference, or unique Attribute | Create PK/FK/unique/check | ✅ Yes | Inline beneath their column; composite keys and multi-column checks at table level |
| Add an Index | Create index | ✅ Yes | Inline or separate file |
| | | | |
| **MODIFICATION** | | | |
| Increase an Attribute's length | Widen column | ✅ Yes | Just change the definition |
| Reduce an Attribute's length | Narrow column | ⚠️ Depends | May need pre-validation; BlockOnPossibleDataLoss guards |
| Change an Attribute's Data Type | Change type (implicit) | ✅ Yes | INT→BIGINT, VARCHAR→NVARCHAR |
| Change an Attribute's Data Type (incompatible) | Change type (explicit) | ❌ No | Needs multi-phase: add new, migrate, drop old |
| Make an Attribute mandatory | NULL → NOT NULL | ⚠️ Depends | Need default or pre-backfill |
| Make an Attribute optional | NOT NULL → NULL | ✅ Yes | Just change the definition |
| Rename an Attribute | Rename column | ✅ Yes | **Must use refactorlog** |
| Rename an Entity | Rename table | ✅ Yes | **Must use refactorlog** |
| ≈ Auto Number on the Identifier | Add/remove IDENTITY | ❌ No | Can't ALTER to add/remove; needs table swap |
| | | | |
| **CONSTRAINTS** | | | |
| Set a Default Value | Add default | ✅ Yes | Inline in table definition |
| Change a Default Value | Modify default | ✅ Yes | Change the value; SSDT drops and recreates |
| Remove a Default Value | Remove default | ✅ Yes | Remove from definition |
| Add a Reference Attribute | Add FK (clean data) | ✅ Yes | Inline beneath its column |
| Add a Reference Attribute (existing data) | Add FK (orphan data) | ❌ No | Need WITH NOCHECK via script, then trust |
| — *(SSDT concept)* | Enable/disable constraint | ❌ No | Script-only (operational, not declarative) |
| | | | |
| **INDEXES** | | | |
| Add or remove an Index | Add/drop index | ✅ Yes | Add/remove definition |
| Change an Index | Change index columns | ✅ Yes | SSDT regenerates |
| — *(maintenance)* | Rebuild/reorganize | ❌ No | Maintenance operation, not schema |
| — *(SSDT concept)* | Online index operations | ⚠️ Partial | May need script for WITH (ONLINE=ON) |
| | | | |
| **STRUCTURAL** | | | |
| — *(no OutSystems equivalent)* | Split table | ❌ No | Multi-phase: create, migrate, drop |
| — *(no OutSystems equivalent)* | Merge tables | ❌ No | Multi-phase: create, migrate, drop |
| ≈ Move an Attribute to another Entity | Move column between tables | ❌ No | Multi-phase |
| — *(modules aren't schemas)* | Move table between schemas | ⚠️ Partial | Declarative with refactorlog, or ALTER SCHEMA TRANSFER |

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

## 18.5 Tier Summary Card

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
│  • Table >1M rows (for data operations)                                 │
│  • Production-critical timing                                           │
│  • First time doing this operation type                                 │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 18.6 Operation Quick Reference

**One-line summaries for common operations:**

| In OutSystems | Operation | Tier | Mechanism | Watch For |
|---------------|-----------|------|-----------|-----------|
| Create an Entity | Add table | 1 | Declarative | Nothing — safest operation |
| Add an optional Attribute | Add nullable column | 1 | Declarative | Nothing |
| Add a mandatory Attribute | Add NOT NULL column | 1-2 | Declarative | Need default for existing rows |
| Add an Index | Add index | 1-2 | Declarative | Large table = blocking time |
| Add a Reference Attribute | Add FK (clean data) | 2 | Declarative | Verify no orphans first |
| Add a Reference Attribute (existing data) | Add FK (orphan data) | 3 | Multi-phase | WITH NOCHECK → clean → trust |
| Set a Default Value | Add default | 1 | Declarative | Nothing |
| — *(no OutSystems equivalent)* | Add check constraint | 2 | Declarative | Existing data may violate |
| Make an Attribute unique | Add unique constraint | 2 | Declarative | Check for duplicates first |
| Increase an Attribute's length | Widen column | 2 | Declarative | Index rebuild possible |
| Reduce an Attribute's length | Narrow column | 4 | Pre + Declarative | Validate data fits; BlockOnPossibleDataLoss |
| Change an Attribute's Data Type | Change type (implicit) | 2 | Declarative | INT→BIGINT is safe |
| Change an Attribute's Data Type (incompatible) | Change type (explicit) | 3-4 | Multi-phase | Add new → migrate → drop old |
| Make an Attribute mandatory | NULL → NOT NULL | 2-3 | Pre + Declarative + logged guard-relaxation | Backfill first and prove 0 remain — necessary, not sufficient: the data-loss guard checks row presence, not NULL content, so a populated table stays blocked until `BlockOnPossibleDataLoss` is deliberately relaxed for that deployment (see §17.2) |
| Make an Attribute optional | NOT NULL → NULL | 1-2 | Declarative | Safe; consider why |
| Rename an Attribute | Rename column | 3 | Declarative + refactorlog | **Without refactorlog = data loss** |
| Rename an Entity | Rename table | 3 | Declarative + refactorlog | **Without refactorlog = data loss** |
| Delete an Attribute | Drop column | 3-4 | Declarative | Follow deprecation workflow |
| Delete an Entity | Drop table | 4 | Declarative | Verify truly unused; backup |
| ≈ Auto Number on the Identifier | Add/remove IDENTITY | 3-4 | Multi-phase (table swap) | Full table rebuild |
| — *(no OutSystems equivalent)* | Split table | 4 | Multi-phase | Multiple releases |
| — *(no OutSystems equivalent)* | Merge tables | 4 | Multi-phase | Multiple releases |

---

