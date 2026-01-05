Looking at this carefully, I can see the issue: Sections 1, 2, and 23 have **section number references that don't match our final numbering**. These are surgical fixes. Then Sections 13-15 need full drafting.

Let me provide this in two parts.

---

## Part 1: Surgical Updates to Sections 1, 2, and 23

These are **find-and-replace corrections** for section number references:

### Section 1 (Start Here) — Line-by-Line Fixes

| Find | Replace With |
|------|--------------|
| `[17. Decision Aids](#)` | `[18. Decision Aids](#)` |
| `[15. Operation Reference](#)` | `[16. Operation Reference](#)` |
| `[20. The Change/Release Process](#)` | `[22. The Change/Release Process](#)` |
| `[2. The Big Picture](#)` | `[2. The Big Picture](#)` *(no change)* |
| `[3. State-Based Modeling vs. Imperative Migrations](#)` | `[4. State-Based Modeling vs. Imperative Migrations](#)` |
| `[9. SSDT Deployment Safety](#)` | `[10. SSDT Deployment Safety](#)` |
| `[19. Local Development Setup](#)` | `[21. Local Development Setup](#)` |
| `[13. Ownership Tiers](#)` | `[14. Ownership Tiers](#)` |
| `[22. Troubleshooting Playbook](#)` | `[24. Troubleshooting Playbook](#)` |
| `[23. Escalation Paths](#)` | `[25. Escalation Paths](#)` |
| `[28. Contribution Guidelines](#)` | `[32. Contribution Guidelines](#)` |
| `[26. Glossary](#)` | `[30. Glossary](#)` |

### Section 2 (The Big Picture) — Line-by-Line Fixes

| Find | Replace With |
|------|--------------|
| `[11. CDC and Schema Evolution](#)` | `[12. CDC and Schema Evolution](#)` |

### Section 23 (PR Template + Usage) — Line-by-Line Fixes

In the template itself:
| Find | Replace With |
|------|--------------|
| `[Dimension Framework](#)` | `[13. Dimension Framework](#)` |

In the usage notes:
| Find | Replace With |
|------|--------------|
| `[15. Operation Reference](#)` | `[16. Operation Reference](#)` |

---

## Part 2: Sections 13, 14, and 15 — Full Drafts

---

# 13. The Dimension Framework

---

## What This Section Covers

Every database change can be classified along four dimensions. Understanding these dimensions is how you determine the tier, identify risks, and know what to watch for.

This isn't abstract theory — it's practical risk assessment. When you can quickly answer the four dimension questions, you can classify any change.

---

## The Four Dimensions

```
┌─────────────────────────────────────────────────────────────────────────┐
│                                                                         │
│   DATA INVOLVEMENT                                                      │
│   What happens to existing data?                                        │
│                                                                         │
│   Schema-only ──── Data-preserving ──── Data-transforming ──── Data-destructive
│   (safest)                                                    (most dangerous)
│                                                                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│   REVERSIBILITY                                                         │
│   How hard is it to undo?                                               │
│                                                                         │
│   Symmetric ──────────────── Effortful ──────────────────────── Lossy   │
│   (trivial)                                                   (backup only)
│                                                                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│   DEPENDENCY SCOPE                                                      │
│   What else is affected?                                                │
│                                                                         │
│   Self-contained ──── Intra-table ──── Inter-table ──── Cross-boundary  │
│   (isolated)                                              (external systems)
│                                                                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│   APPLICATION IMPACT                                                    │
│   Can existing code keep working?                                       │
│                                                                         │
│   Additive ────────────────── Contractual ───────────────────── Breaking│
│   (nothing breaks)                                          (must coordinate)
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Dimension 1: Data Involvement

**Question:** What happens to existing data as a result of this change?

### Schema-Only

The change affects structure but doesn't touch existing data values.

**Examples:**
- Adding a nullable column (existing rows get NULL)
- Adding an index (structure built from data, data unchanged)
- Adding a constraint to an empty table
- Creating a new table
- Creating a view

**Risk profile:** Lowest. If something goes wrong, data is safe.

### Data-Preserving

The change affects structure, and existing data must accommodate, but no values are modified or lost.

**Examples:**
- Adding a NOT NULL column with a default (existing rows get the default value)
- Adding a FK constraint (validates existing data, doesn't change it)
- Widening a column (existing values still fit)

**Risk profile:** Low-moderate. Deployment may fail if data violates new constraints, but no data loss occurs.

### Data-Transforming

Existing data values must be converted, moved, or derived as part of the change.

**Examples:**
- Changing a column's data type (VARCHAR to DATE — values must parse)
- Splitting a table (data moves from one table to two)
- Backfilling a column from computed values
- Normalizing inline values to a lookup table

**Risk profile:** Moderate-high. Transformation logic could be wrong. Original values may be altered. Usually requires multi-phase approach.

### Data-Destructive

Information will be permanently lost as a result of this change.

**Examples:**
- Dropping a column that contains data
- Dropping a table with rows
- Narrowing a column below current max length (truncation)
- Changing type in a way that loses precision

**Risk profile:** Highest. Once done, only backup restore can recover. `BlockOnPossibleDataLoss=True` is your last line of defense.

---

## Dimension 2: Reversibility

**Question:** If we need to undo this change, how hard is it?

### Symmetric

The reverse operation is trivial and structurally identical to the forward operation.

**Examples:**
- Add nullable column → Remove column (if no data written yet)
- Create table → Drop table (if empty)
- Add index → Drop index
- NOT NULL → NULL

**Rollback:** Just reverse the declarative change and deploy again.

### Effortful

The change can be undone, but requires scripted work, coordination, or non-trivial effort.

**Examples:**
- Add NOT NULL column with default → Can remove, but default values remain in data
- Widen column → Narrowing back requires verification that data fits
- Add FK → Can remove, but orphan data may have been created since
- Rename (with refactorlog) → Rename back requires another refactorlog entry

**Rollback:** Possible, but needs planning. May need scripted migration to restore previous state.

### Lossy

The change cannot be undone without restoring from backup. Information or state has been permanently altered.

**Examples:**
- Drop column with data → Data is gone
- Drop table with rows → Data is gone
- Narrow column (truncation occurred) → Truncated data cannot be recovered
- Explicit type conversion with precision loss → Original values gone

**Rollback:** Backup restore is the only path. This is why we verify backups before Tier 4 operations.

---

## Dimension 3: Dependency Scope

**Question:** What other objects or systems are affected by this change?

### Self-Contained

The change affects only the object being modified. Nothing else references it or depends on it.

**Examples:**
- Adding a new table (nothing references it yet)
- Adding a nullable column (nothing uses it yet)
- Adding an index (improves queries but doesn't change them)

**Coordination required:** None. You can make this change in isolation.

### Intra-Table

The change affects other objects within the same table.

**Examples:**
- Changing a column that's part of an index
- Modifying a column referenced by a computed column
- Dropping a column that has a default constraint

**Coordination required:** SSDT usually handles dependencies within a table. Review generated script to ensure dependent objects are handled correctly.

### Inter-Table

The change affects objects in other tables within the same database.

**Examples:**
- Changing a column that's referenced by FKs from other tables
- Renaming a table that's joined in views
- Modifying a column used in stored procedures

**Coordination required:** Must identify and update all dependent objects. SSDT's build will catch missing references in declarative objects; search codebase for dynamic SQL.

### Cross-Boundary

The change affects systems outside the SSDT project's scope.

**Examples:**
- Renaming a column used by ETL pipelines
- Changing a table consumed by reporting tools
- Modifying structure that external APIs depend on
- Anything with CDC implications (Change History feature depends on capture instances)

**Coordination required:** Must communicate with external system owners. Timeline coordination. May require phased rollout with backward compatibility.

---

## Dimension 4: Application Impact

**Question:** Can existing application code continue to work without modification?

### Additive

The change adds capability without affecting existing behavior. Code that worked before continues to work.

**Examples:**
- Adding a nullable column (existing queries ignore it)
- Adding a new table (nothing references it yet)
- Adding an index (queries run faster, but don't break)
- Adding a default constraint (behavior on INSERT changes, but existing code still works)

**Deployment coordination:** None. SSDT can deploy independently of application changes.

### Contractual

Old and new code can coexist, but there's a contract change that must eventually be honored.

**Examples:**
- Adding a NOT NULL column (existing INSERTs must provide value, or rely on default)
- Adding a FK constraint (existing INSERTs must satisfy referential integrity)
- Changing column width (existing code must respect new limits)

**Deployment coordination:** SSDT deploys first, but application code must be updated to fully comply. A transition period where both work is possible.

### Breaking

Existing code will fail if deployed without coordinated application changes.

**Examples:**
- Removing a column that's SELECTed
- Renaming a column used in queries
- Changing a type that application code depends on
- Removing a table that's referenced

**Deployment coordination:** Must deploy SSDT and application changes together, or use multi-phase approach with backward compatibility (e.g., create view with old name pointing to new table).

---

## Recognition Heuristics

Use these question chains to quickly classify a change.

### Data Involvement Chain

```
Will any existing values be permanently lost?
  │
  ├─► YES → Data-destructive
  │
  └─► NO → Will existing values be modified or moved?
              │
              ├─► YES → Data-transforming
              │
              └─► NO → Will existing rows be validated against new constraints?
                          │
                          ├─► YES → Data-preserving
                          │
                          └─► NO → Schema-only
```

### Reversibility Chain

```
Can I simply reverse the declarative change and deploy?
  │
  ├─► YES, trivially → Symmetric
  │
  └─► NO → Is there a path back without restoring from backup?
              │
              ├─► YES, but it requires work → Effortful
              │
              └─► NO → Lossy
```

### Dependency Scope Chain

```
Does anything outside this database reference what I'm changing?
  │
  ├─► YES → Cross-boundary
  │
  └─► NO → Do other tables reference what I'm changing?
              │
              ├─► YES → Inter-table
              │
              └─► NO → Do other objects in the same table depend on this?
                          │
                          ├─► YES → Intra-table
                          │
                          └─► NO → Self-contained
```

### Application Impact Chain

```
Will any existing query or application code fail?
  │
  ├─► YES → Breaking
  │
  └─► NO → Will existing code need to change eventually to fully comply?
              │
              ├─► YES → Contractual
              │
              └─► NO → Additive
```

---

## Putting It Together: Classification Examples

### Example 1: Add Nullable Column to Customer

| Dimension | Assessment | Reasoning |
|-----------|------------|-----------|
| Data Involvement | Schema-only | Existing rows get NULL; no values touched |
| Reversibility | Symmetric | Remove column from definition, deploy |
| Dependency Scope | Self-contained | Nothing references this column yet |
| Application Impact | Additive | Existing queries continue to work |

**Conclusion:** All dimensions at lowest risk → **Tier 1**

---

### Example 2: Add NOT NULL Column to Populated Table

| Dimension | Assessment | Reasoning |
|-----------|------------|-----------|
| Data Involvement | Data-preserving | Existing rows get default value |
| Reversibility | Effortful | Can remove column, but default values persist |
| Dependency Scope | Self-contained | New column, nothing references it |
| Application Impact | Contractual | New INSERTs must provide value (or use default) |

**Conclusion:** Data-preserving + Effortful + Contractual → **Tier 2**

---

### Example 3: Rename Column

| Dimension | Assessment | Reasoning |
|-----------|------------|-----------|
| Data Involvement | Schema-only | Data values unchanged |
| Reversibility | Effortful | Need another refactorlog entry to rename back |
| Dependency Scope | Inter-table to Cross-boundary | Views, procs, app code, possibly ETL |
| Application Impact | Breaking | All queries using old name will fail |

**Conclusion:** Cross-boundary + Breaking → **Tier 3** minimum

---

### Example 4: Drop Column with Data

| Dimension | Assessment | Reasoning |
|-----------|------------|-----------|
| Data Involvement | Data-destructive | Column values will be gone |
| Reversibility | Lossy | Only backup restore can recover |
| Dependency Scope | Inter-table | Must verify nothing references it |
| Application Impact | Breaking | Any code using this column fails |

**Conclusion:** Data-destructive + Lossy → **Tier 4**

---

## The Golden Rule

**Your tier is determined by your highest-risk dimension.**

If you have three dimensions at Tier 1 levels and one at Tier 3, you're Tier 3.

This is conservative by design. The most dangerous aspect of a change determines its handling, not the average.

---

## Connecting to Tiers

The next section ([14. Ownership Tiers](#)) explains what each tier means in terms of process, review, and ownership. The dimensions tell you *where* you land; the tiers tell you *what to do* about it.

---

# 14. Ownership Tiers

---

## What This Section Covers

Tiers translate risk (from the dimension framework) into process. Each tier has:
- Defined ownership (who can execute, who must review)
- Required process (what steps must happen)
- Review criteria (what reviewers check for)

The tier system distributes risk appropriately: simple changes move fast; complex changes get scrutiny.

---

## Tier Definitions

### Tier 1: Self-Service

**Who can execute:** Any team member
**Who reviews:** Any team member
**Process:** Standard PR → Review → Merge

**Dimension profile:**
- Data Involvement: Schema-only
- Reversibility: Symmetric
- Dependency Scope: Self-contained
- Application Impact: Additive

**What this means:** The change is low-risk. If something goes wrong, it's easily reversible and data is safe. Any team member can do this work confidently.

**Examples:**
- Add a new table
- Add a nullable column
- Add a default constraint
- Add an index to a small table
- Create a view
- NOT NULL → NULL

**Review focus:**
- Is this actually Tier 1? (Nothing pushing it higher?)
- Any obvious gotchas?
- Generated script looks reasonable?

---

### Tier 2: Pair-Supported

**Who can execute:** Team member (pair support available)
**Who reviews:** Dev lead or experienced IC
**Process:** Standard PR → Review → Merge, with more careful review

**Dimension profile:**
- Data Involvement: Data-preserving
- Reversibility: Effortful
- Dependency Scope: Intra-table
- Application Impact: Contractual

**What this means:** The change has moderate risk. Data is safe, but rollback requires work. Existing code will continue to function, but there are new constraints to honor. Pair support is available for less experienced team members.

**Examples:**
- Add NOT NULL column with default
- Add FK to table (clean data verified)
- Widen a column
- Add check constraint to populated table
- Implicit type conversions (INT → BIGINT)
- NULL → NOT NULL (with backfill)

**Review focus:**
- Everything from Tier 1
- Data validation queries appropriate?
- Pre/post scripts idempotent?
- CDC impact correctly identified?
- Rollback plan viable?

---

### Tier 3: Dev Lead Owned

**Who can execute:** Dev lead, or experienced IC with dev lead oversight
**Who reviews:** Dev lead (required)
**Process:** PR → Dev lead review → Merge, possibly with synchronous discussion

**Dimension profile:**
- Data Involvement: Data-transforming
- Reversibility: Effortful
- Dependency Scope: Inter-table
- Application Impact: Breaking

**What this means:** The change has significant risk. Data values will change, multiple objects are affected, and application coordination is needed. This requires experienced judgment and careful sequencing.

**Examples:**
- Rename column or table
- Explicit type conversions (multi-phase)
- Add FK with orphan data handling
- Drop column (with deprecation workflow)
- Structural refactoring (split, merge, move)
- Any CDC-enabled table schema change

**Review focus:**
- Everything from Tier 2
- Multi-phase sequencing correct?
- Application coordination identified?
- Should this be walked through synchronously?
- Cross-team communication needed?

---

### Tier 4: Principal Escalation

**Who can execute:** Principal engineer, or with principal oversight
**Who reviews:** Principal engineer (required)
**Process:** Discussion before PR → PR → Principal review → Merge, with explicit verification

**Dimension profile:**
- Data Involvement: Data-destructive
- Reversibility: Lossy
- Dependency Scope: Cross-boundary
- Application Impact: Breaking
- OR: Novel/unprecedented pattern

**What this means:** The change carries the highest risk. Information may be permanently lost. Recovery requires backup restore. External systems are affected. Or, it's something we've never done before and need to figure out carefully.

**Examples:**
- Drop table with data
- Drop column with data
- Narrow column (potential truncation)
- Major structural refactoring
- Novel patterns not covered in playbook

**Review focus:**
- Everything from Tier 3
- Backup verification
- External stakeholder communication
- Explicit rollback testing
- Consider: should we walk through this live?

---

## Determining Your Tier

### Step 1: Assess Each Dimension

Use the recognition heuristics from [Section 13](#13-the-dimension-framework) to determine where your change lands on each dimension.

### Step 2: Find the Highest Risk

| Dimension Value | Floor Tier |
|-----------------|------------|
| Schema-only, Symmetric, Self-contained, Additive | Tier 1 |
| Data-preserving, Effortful, Intra-table, Contractual | Tier 2 |
| Data-transforming, Inter-table, Breaking | Tier 3 |
| Data-destructive, Lossy, Cross-boundary | Tier 4 |

**Your tier = the highest tier indicated by any dimension.**

### Step 3: Check Escalation Triggers

Even if dimensions suggest a lower tier, these factors push you up:

| Trigger | Effect |
|---------|--------|
| CDC-enabled table | +1 tier minimum |
| Large table (>1M rows) | +1 tier for operations that touch data |
| Production-critical timing | +1 tier |
| First time doing this operation type | +1 tier or explicit pairing |
| Novel/unprecedented pattern | Tier 4 regardless |

### Step 4: Document Your Classification

In your PR, state:
- The tier you've determined
- Why (which dimensions led there)
- Any escalation triggers that apply

---

## Escalation Triggers: Detailed

### CDC-Enabled Table (+1 Tier)

Any schema change on a CDC-enabled table affects capture instances. Even "simple" changes require CDC awareness.

**Why:** CDC powers Change History. Mistakes create audit gaps or stale instances.

**Effect:** What would be Tier 1 becomes Tier 2. What would be Tier 2 becomes Tier 3.

**Exception:** If the column being added doesn't need to be tracked, and you're only accepting a gap in dev/test, you might stay at the base tier — but document this explicitly.

### Large Table (+1 Tier for Data Operations)

Tables with more than ~1 million rows have different operational characteristics:
- Index builds take longer and may block
- Data migrations require batching
- Timeouts become possible
- Lock escalation is more likely

**Why:** Operations that are instant on small tables can take minutes or hours on large ones.

**Effect:** Schema-only changes (add nullable column) may not be affected. Data operations (backfill, constraint validation) get +1 tier.

### Production-Critical Timing (+1 Tier)

If the change is being made during a sensitive period:
- End of quarter
- Major release
- High-traffic period
- Immediately before a demo or audit

**Why:** The cost of failure is higher than usual.

**Effect:** Take extra care. Get additional review. Consider waiting if possible.

### First Time (+1 Tier or Pair)

If you've never done this type of operation before, even if the playbook says it's Tier 1:

**Why:** Reading about something isn't the same as doing it. Your first rename, your first CDC change, your first FK addition — get support.

**Effect:** Either bump your tier up, or do the change with explicit pairing from someone who's done it before.

### Novel Pattern (Tier 4)

If the operation isn't covered in this playbook, or you're doing something unprecedented:

**Why:** We don't have encoded judgment for this situation. We need to develop it carefully.

**Effect:** Tier 4 regardless of dimensions. Involve principal. Document what we learn for the playbook.

---

## Tier and Capability Development

Tiers connect to the graduation path in [Section 26](#26-capability-development):

| Level | Typical Tier Autonomy |
|-------|----------------------|
| L1: Observer | Shadows all tiers |
| L2: Supported Contributor | Tier 1 with pairing |
| L3: Independent Contributor | Tier 1 independently, Tier 2 with review |
| L4: Trusted Contributor | Tier 1-2 independently, Tier 3 with oversight |
| L5: Dev Lead | Tier 1-3 independently, Tier 4 with principal |

Progression isn't about doing higher-tier work faster. It's about developing judgment to classify correctly and execute safely.

---

## Common Classification Mistakes

### Under-Classification

**Pattern:** "It's just adding a column" → marks Tier 1

**Reality:** The column is NOT NULL without a default, the table is CDC-enabled, and there's a FK to it from another table.

**Actual tier:** Tier 3 (CDC + FK dependency)

**Prevention:** Walk through all four dimensions explicitly. Check escalation triggers.

### Over-Classification

**Pattern:** Marks everything Tier 3+ out of caution

**Problems:** Dev lead becomes bottleneck. Team doesn't develop confidence. Process slows unnecessarily.

**Reality:** Many changes genuinely are Tier 1-2. Trust the framework.

**Prevention:** If dimensions all land at low levels and no triggers apply, trust the classification.

### Dimension Blindness

**Pattern:** Focuses only on one dimension (usually data involvement)

**Example:** "I'm not deleting any data, so it must be low tier" — but the change is breaking (renames a column everything uses).

**Prevention:** Explicitly assess all four dimensions. The highest one wins.

---

## Classification Examples

### Example: Add Index to Large Table

**Dimensions:**
- Data Involvement: Schema-only (index creation doesn't modify data values)
- Reversibility: Symmetric (drop the index)
- Dependency Scope: Self-contained (nothing references an index)
- Application Impact: Additive (queries get faster, don't break)

**Base tier:** Tier 1

**Triggers:** Table has 5M rows (+1 for large table operations)

**Final tier:** Tier 2

**Why:** The index build will take time and may block. Need to plan for maintenance window or use online operations.

---

### Example: Add Column to CDC-Enabled Table

**Dimensions:**
- Data Involvement: Schema-only (nullable column, existing rows get NULL)
- Reversibility: Symmetric (remove the column)
- Dependency Scope: Self-contained (new column)
- Application Impact: Additive (existing code works)

**Base tier:** Tier 1

**Triggers:** CDC-enabled (+1)

**Final tier:** Tier 2

**Why:** Need to decide on capture instance handling. Even though the column add is simple, CDC creates complexity.

---

### Example: Rename Column for Clarity

**Dimensions:**
- Data Involvement: Schema-only (data unchanged)
- Reversibility: Effortful (need refactorlog entry to rename back)
- Dependency Scope: Cross-boundary (ETL uses this column)
- Application Impact: Breaking (all queries using old name fail)

**Base tier:** Tier 3 (Cross-boundary + Breaking)

**Triggers:** None additional

**Final tier:** Tier 3

**Why:** Must coordinate with ETL team. Need backward compatibility approach or synchronized deployment.

---

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

---

That completes the missing sections. You now have:
- Section 1: Start Here (with corrected references)
- Section 2: The Big Picture (with corrected references)
- Section 13: The Dimension Framework (new)
- Section 14: Ownership Tiers (new)
- Section 15: The SSDT Mechanism Axis (new)
- Section 23: PR Template + Usage (with corrected references)

The playbook is now structurally complete.
