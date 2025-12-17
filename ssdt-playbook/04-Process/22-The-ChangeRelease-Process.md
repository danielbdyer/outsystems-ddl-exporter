# 22. The Change/Release Process

---

## Overview

Every database change follows a defined process. The process exists to:
- Catch errors before they reach production
- Ensure appropriate review for risk level
- Create audit trail of changes
- Enable safe rollback if needed

The process scales with risk: simple changes move quickly; complex changes get more scrutiny.

---

## Step 1: Classify Your Change

Before touching any code, classify the change.

### 1.1 Identify Affected Tables

- Which tables will this change touch?
- Are any of them CDC-enabled? (Check the CDC Table Registry)

### 1.2 Determine the Dimensions

For each operation in your change, answer:

**Data Involvement:**
- Schema-only (no rows touched)?
- Data-preserving (rows stay, structure changes)?
- Data-transforming (values must convert/move)?
- Data-destructive (information will be lost)?

**Reversibility:**
- Symmetric (trivially reversible)?
- Effortful (requires scripted rollback)?
- Lossy (cannot undo without backup)?

**Dependency Scope:**
- Self-contained (single object)?
- Intra-table (other objects on same table)?
- Inter-table (other tables, views, procs)?
- Cross-boundary (external systems, ETL)?

**Application Impact:**
- Additive (nothing breaks)?
- Contractual (coexistence possible)?
- Breaking (synchronized deployment required)?

### 1.3 Determine the Tier

The tier is the highest risk across any dimension:

| If any dimension lands here... | Floor Tier |
|--------------------------------|------------|
| Schema-only, Symmetric, Self-contained, Additive | Tier 1 |
| Data-preserving, Effortful, Intra-table, Contractual | Tier 2 |
| Data-transforming, Inter-table, Breaking | Tier 3 |
| Data-destructive, Lossy, Cross-boundary | Tier 4 |

**Escalation triggers** (push tier upward regardless of dimensions):
- CDC-enabled table: +1 tier minimum
- Large table (>1M rows): +1 tier for operations that scan/modify data
- Production-critical timing: +1 tier
- Novel or unprecedented pattern: Tier 4

### 1.4 Determine the SSDT Mechanism

- **Pure Declarative:** Just edit the schema files
- **Declarative + Post-Deployment:** Schema change plus data script
- **Pre-Deployment + Declarative:** Prep data, then schema change
- **Script-Only:** SSDT can't handle it; fully scripted
- **Multi-Phase:** Multiple releases required

### 1.5 Document Your Classification

You'll include this in your PR. Write it down now:

```
Change: Add MiddleName column to Customer table
Table: dbo.Customer (CDC-enabled)
Operations: Create column (nullable)
Dimensions: Schema-only, Symmetric, Self-contained, Additive
Base Tier: 1
CDC Impact: Yes — needs instance recreation consideration
Final Tier: 2 (elevated due to CDC)
Mechanism: Declarative + Post-deployment (for CDC re-enable)
```

---

## Step 2: Implement the Change

### 2.1 Create a Branch

```bash
git checkout main
git pull
git checkout -b feature/add-customer-middlename
```

Branch naming convention: `feature/`, `fix/`, or `refactor/` prefix + descriptive name.

### 2.2 Make the Schema Change

For declarative changes, edit the `.sql` file directly:

```sql
-- In /Tables/dbo/dbo.Customer.sql
-- Add the new column in the appropriate position

[MiddleName] NVARCHAR(50) NULL,
```

For renames, use the Visual Studio GUI:
1. In Solution Explorer, navigate to the object
2. Right-click → Rename
3. Enter new name
4. Verify refactorlog was updated

### 2.3 Add Pre/Post Deployment Scripts (If Needed)

If your change requires data work:

```sql
-- /Scripts/PostDeployment/Migrations/XXX_AddCustomerMiddleName.sql

/*
Migration: Add MiddleName column to Customer
Ticket: JIRA-1234
Author: Your Name
Date: 2025-01-15
*/

PRINT 'Migration XXX: Customer.MiddleName setup...'

-- If we needed to backfill (not needed for nullable column, but showing pattern)
-- UPDATE dbo.Customer SET MiddleName = '' WHERE MiddleName IS NULL

PRINT 'Migration XXX complete.'
GO
```

Add the `:r` include to the master PostDeployment.sql if needed.

### 2.4 Handle CDC (If Applicable)

For CDC-enabled tables, add the appropriate CDC management:

```sql
-- For development (accept gaps):
-- Pre-deployment: Disable CDC
-- Post-deployment: Re-enable CDC

-- For production (no gaps):
-- Post-deployment: Create new capture instance
-- Leave old instance until next release
```

---

## Step 3: Build and Test Locally

### 3.1 Build the Project

In Visual Studio: Build → Build Solution (or Ctrl+Shift+B)

**Must succeed with no errors.** Warnings should be reviewed.

### 3.2 Deploy to Local Database

Right-click project → Publish → Select local profile → Publish

**Must succeed.** If it fails:
- Read the error message
- Check if pre-deployment scripts have issues
- Check for data violations (this won't apply to an empty local DB, so also test against a DB with data)

### 3.3 Verify in SSMS

Connect to your local database and verify:
- New objects exist
- Modifications applied correctly
- Constraints are in place
- Indexes created

### 3.4 Review Generated Script

Right-click project → Schema Compare → Compare to local DB → View Script

Read the generated SQL. Ask yourself:
- Is it doing what I expect?
- Are there any DROP statements I didn't anticipate?
- Are there any table rebuilds that seem excessive?
- Does anything look risky?

### 3.5 Test Idempotency

Publish a second time. It should:
- Succeed
- Generate minimal or no changes (if schema matches)
- Not fail on pre/post scripts (they should be idempotent)

---

## Step 4: Open PR

### 4.1 Commit Your Changes

```bash
git add .
git commit -m "Add MiddleName column to Customer table

- Added nullable NVARCHAR(50) column
- CDC impact: requires instance recreation
- Tier 2 change

JIRA-1234"
```

### 4.2 Push and Open PR

```bash
git push -u origin feature/add-customer-middlename
```

Open a PR in Azure DevOps (or your git platform).

### 4.3 Fill Out PR Template

Complete the entire template (see Section 23). Don't skip sections.

Key fields:
- **Classification:** Tier, mechanism, operations
- **CDC Impact:** Yes/no, affected tables
- **Generated Script:** Paste key operations
- **Rollback Plan:** How to reverse if needed

### 4.4 Tag Reviewers

Based on tier:

| Tier | Required Reviewers |
|------|-------------------|
| Tier 1 | Any team member |
| Tier 2 | Dev lead or experienced IC |
| Tier 3 | Dev lead (required) |
| Tier 4 | Principal engineer (required) |

---

## Step 5: Review Process

### For Authors

- Respond to review comments promptly
- If the reviewer questions your classification, discuss — they may see something you missed
- Don't merge until all required approvals are in

### For Reviewers

**Tier 1 Review (5-10 minutes):**
- Classification is correct?
- Change is actually additive/safe?
- No obvious gotchas?
- Generated script looks reasonable?

**Tier 2 Review (15-30 minutes):**
- Everything from Tier 1
- Data validation queries appropriate?
- Pre/post scripts idempotent?
- CDC impact correctly identified?
- Rollback plan viable?

**Tier 3+ Review (30+ minutes):**
- Everything from Tier 2
- Multi-phase sequencing correct?
- Application coordination identified?
- Should this be discussed synchronously before approving?
- Consider: walkthrough call before merge?

### Common Review Feedback

- "This is actually Tier X because..." — Classification adjustment
- "Missing refactorlog entry for the rename" — Critical fix needed
- "Post-deployment script isn't idempotent because..." — Fix required
- "Did you check for orphan data? Show me the query." — Verification request
- "Let's discuss this one synchronously" — Complex enough to warrant a call

---

## Step 6: Merge and Deploy

### 6.1 Merge

After approval:
1. Squash and merge (or your team's preferred merge strategy)
2. Delete the branch

### 6.2 Pipeline Deployment

Merging to main triggers the deployment pipeline:

```
Merge to main
     │
     ▼
┌─────────────────┐
│  Build          │  Compile project, produce dacpac
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│  Deploy to Dev  │  First environment
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│  Dev Validation │  Automated tests, manual verification
└────────┬────────┘
         │
         ▼
    (Promote to Test, UAT, Prod — gated)
```

### 6.3 Post-Deployment Verification

After pipeline completes:
1. Verify pipeline succeeded (check Azure DevOps)
2. Spot-check the database (query to confirm change applied)
3. If CDC change: verify capture instance status
4. If OutSystems-impacting: proceed with Integration Studio refresh

---

## Step 7: Environment Promotion

Changes promote through environments:

```
Dev → Test → UAT → Prod
```

### Promotion Gates

| Promotion | Gate |
|-----------|------|
| Dev → Test | Dev validation complete, basic smoke test |
| Test → UAT | QA sign-off, integration tests pass |
| UAT → Prod | UAT sign-off, change window scheduled, rollback plan confirmed |

### Production Deployment

Production deployments have additional requirements:

- [ ] Change scheduled in maintenance window (if needed)
- [ ] Rollback plan documented and reviewed
- [ ] On-call engineer aware
- [ ] Stakeholders notified
- [ ] Post-deployment validation checklist ready

---

## Multi-Phase Coordination

For multi-phase changes, each phase follows this process independently:

**Release N:**
1. Phase 1 changes: PR → Review → Merge → Deploy → Verify
2. Wait for phase 1 to reach production and stabilize

**Release N+1:**
1. Phase 2 changes: PR → Review → Merge → Deploy → Verify
2. Continue until all phases complete

**Document the sequence:**
- PR for phase 1 should reference the overall plan
- PR for phase 2 should reference phase 1's completion
- Each PR is self-contained but part of a documented sequence

---

