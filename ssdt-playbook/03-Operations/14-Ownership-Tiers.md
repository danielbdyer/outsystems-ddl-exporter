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

