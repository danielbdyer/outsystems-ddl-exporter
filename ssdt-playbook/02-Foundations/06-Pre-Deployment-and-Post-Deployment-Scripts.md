# 6. Pre-Deployment and Post-Deployment Scripts

---

## When Declarative Isn't Enough

SSDT's declarative model handles structure beautifully. But databases have *data*, and data often needs transformation that SSDT can't express declaratively.

**SSDT can:**
- Add a column
- Change a column's type
- Add a constraint

**SSDT cannot:**
- Know what value to put in a new NOT NULL column for existing rows
- Transform existing data from one format to another
- Seed reference data
- Clean up orphan records before adding an FK

That's what pre-deployment and post-deployment scripts are for.

---

## The Execution Order

```
┌─────────────────────────────────────────────────────────────────────────┐
│  1. PRE-DEPLOYMENT SCRIPT                                               │
│     - Runs BEFORE any schema changes                                    │
│     - Database is still in "old" state                                  │
│     - Use for: preparing data, dropping blockers                        │
├─────────────────────────────────────────────────────────────────────────┤
│  2. SCHEMA CHANGES (SSDT-generated)                                     │
│     - All the ALTER TABLE, CREATE INDEX, etc.                           │
│     - Database transitions from old state to new state                  │
├─────────────────────────────────────────────────────────────────────────┤
│  3. POST-DEPLOYMENT SCRIPT                                              │
│     - Runs AFTER schema changes complete                                │
│     - Database is now in "new" state                                    │
│     - Use for: data migration, seeding, backfill                        │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Pre-Deployment Scripts

### When to Use

Pre-deployment scripts run *before* SSDT changes the schema. Use them when:

- **Something blocks the schema change:** A constraint or index prevents the ALTER
- **Data must be cleaned first:** You need to remove violating rows before adding a constraint
- **Dependencies must be dropped:** A view or proc must be dropped before the column it references

### Examples

**Backfill NULLs before adding NOT NULL constraint:**

```sql
-- PreDeployment.sql (or included file)

-- We're about to make MiddleName NOT NULL
-- First, backfill any existing NULLs
PRINT 'Pre-deployment: Backfilling NULL MiddleName values...'

IF EXISTS (SELECT 1 FROM dbo.Person WHERE MiddleName IS NULL)
BEGIN
    UPDATE dbo.Person 
    SET MiddleName = '' 
    WHERE MiddleName IS NULL
    
    PRINT 'Backfilled ' + CAST(@@ROWCOUNT AS VARCHAR) + ' rows.'
END
ELSE
BEGIN
    PRINT 'No NULL MiddleName values found — skipping.'
END
GO
```

**Remove orphan data before adding FK:**

```sql
-- We're about to add FK_Order_Customer
-- First, clean up any orphan orders
PRINT 'Pre-deployment: Removing orphan orders...'

DELETE FROM dbo.[Order]
WHERE CustomerId NOT IN (SELECT CustomerId FROM dbo.Customer)

PRINT 'Removed ' + CAST(@@ROWCOUNT AS VARCHAR) + ' orphan orders.'
GO
```

### Structure

Keep pre-deployment scripts lean. They should:
- Do only what's necessary to unblock the schema change
- Be idempotent (safe to run multiple times)
- Print progress for debugging

---

## Post-Deployment Scripts

### When to Use

Post-deployment scripts run *after* SSDT has changed the schema. Use them when:

- **Data needs migration:** Converting data from old format to new
- **New columns need values:** Populating a new column from existing data
- **Reference data needs seeding:** Lookup tables need their initial values
- **One-time fixes:** Corrections that apply to this release only

### The Hybrid Structure

We use a structured approach that balances auditability with cleanliness:

```
/Scripts/PostDeployment/
    PostDeployment.sql              ← Master script (includes others)
    /Migrations/                    ← Permanent, idempotent
        001_BackfillCreatedAt.sql
        002_PopulateStatusLookup.sql
        003_MigrateAddressData.sql
    /ReferenceData/                 ← Permanent, idempotent
        SeedCountries.sql
        SeedStatusCodes.sql
        SeedProductTypes.sql
    /OneTime/                       ← Removed after prod deploy
        Release_2025.02_DataFixes.sql
```

**Master script:**

```sql
-- PostDeployment.sql

/*
Post-Deployment Script
======================
This file runs after schema deployment completes.
Add new migration scripts using :r includes.
All scripts must be idempotent.
*/

PRINT '========================================'
PRINT 'Starting post-deployment scripts'
PRINT '========================================'

-- Permanent migrations (idempotent, cumulative)
PRINT 'Running migrations...'
:r .\Migrations\001_BackfillCreatedAt.sql
:r .\Migrations\002_PopulateStatusLookup.sql
:r .\Migrations\003_MigrateAddressData.sql

-- Reference data (idempotent)
PRINT 'Seeding reference data...'
:r .\ReferenceData\SeedCountries.sql
:r .\ReferenceData\SeedStatusCodes.sql
:r .\ReferenceData\SeedProductTypes.sql

-- One-time scripts for current release
-- Remove these after successful prod deployment
PRINT 'Running one-time release scripts...'
:r .\OneTime\Release_2025.02_DataFixes.sql

PRINT '========================================'
PRINT 'Post-deployment complete'
PRINT '========================================'
GO
```

### Migration Scripts (Permanent)

These stay in the project forever. They must be idempotent — safe to run multiple times.

**Example: Backfill a new column**

```sql
-- /Migrations/001_BackfillCreatedAt.sql

/*
Migration: Backfill CreatedAt column
Ticket: JIRA-1234
Author: Danny
Date: 2025-01-15

This migration populates the new CreatedAt column for existing records.
Uses OrderDate as a proxy where available, otherwise uses a default.
*/

PRINT 'Migration 001: Backfill CreatedAt...'

IF EXISTS (SELECT 1 FROM dbo.[Order] WHERE CreatedAt IS NULL)
BEGIN
    UPDATE dbo.[Order]
    SET CreatedAt = ISNULL(OrderDate, '2020-01-01')
    WHERE CreatedAt IS NULL
    
    PRINT '  Updated ' + CAST(@@ROWCOUNT AS VARCHAR) + ' rows.'
END
ELSE
BEGIN
    PRINT '  No NULL CreatedAt values — skipping.'
END
GO
```

**Example: Seed a lookup table**

```sql
-- /ReferenceData/SeedStatusCodes.sql

/*
Reference Data: Order Status codes
*/

PRINT 'Seeding OrderStatus reference data...'

-- Use MERGE for idempotent upsert
MERGE INTO dbo.OrderStatus AS target
USING (VALUES
    (1, 'Pending', 1),
    (2, 'Processing', 2),
    (3, 'Shipped', 3),
    (4, 'Delivered', 4),
    (5, 'Cancelled', 5)
) AS source (StatusId, StatusName, SortOrder)
ON target.StatusId = source.StatusId
WHEN MATCHED THEN
    UPDATE SET StatusName = source.StatusName, SortOrder = source.SortOrder
WHEN NOT MATCHED THEN
    INSERT (StatusId, StatusName, SortOrder)
    VALUES (source.StatusId, source.StatusName, source.SortOrder);

PRINT '  OrderStatus seeded/updated.'
GO
```

### One-Time Scripts (Transient)

These are for release-specific work. After successful production deployment, they're removed (moved to git history only).

```sql
-- /OneTime/Release_2025.02_DataFixes.sql

/*
One-Time Script: Release 2025.02 data corrections
Remove after production deployment.

Ticket: JIRA-1456
Description: Fix incorrectly migrated phone numbers from legacy import
*/

PRINT 'One-time fix: Correcting phone number format...'

UPDATE dbo.Customer
SET PhoneNumber = '+1' + PhoneNumber
WHERE PhoneNumber NOT LIKE '+%'
  AND LEN(PhoneNumber) = 10

PRINT '  Updated ' + CAST(@@ROWCOUNT AS VARCHAR) + ' phone numbers.'
GO
```

---

## Idempotency Patterns

Every permanent script must be safe to run multiple times. Here's how:

### Pattern 1: Check-Before-Act

```sql
-- Only update rows that need it
IF EXISTS (SELECT 1 FROM dbo.Person WHERE MiddleName IS NULL)
BEGIN
    UPDATE dbo.Person SET MiddleName = '' WHERE MiddleName IS NULL
END
```

### Pattern 2: MERGE for Upserts

```sql
-- Insert or update in one statement
MERGE INTO dbo.Country AS target
USING (VALUES ('US', 'United States'), ('CA', 'Canada')) AS source (Code, Name)
ON target.CountryCode = source.Code
WHEN MATCHED THEN UPDATE SET CountryName = source.Name
WHEN NOT MATCHED THEN INSERT (CountryCode, CountryName) VALUES (source.Code, source.Name);
```

### Pattern 3: NOT EXISTS Guard

```sql
-- Only insert if not already there
IF NOT EXISTS (SELECT 1 FROM dbo.Country WHERE CountryCode = 'US')
BEGIN
    INSERT INTO dbo.Country (CountryCode, CountryName) VALUES ('US', 'United States')
END
```

### Pattern 4: Migration Tracking Table

For complex migrations where simple checks aren't enough:

```sql
-- Check if this migration has run
IF NOT EXISTS (SELECT 1 FROM dbo.MigrationHistory WHERE MigrationId = '003_MigrateAddressData')
BEGIN
    -- Do the migration work
    -- ... complex operations ...
    
    -- Mark as complete
    INSERT INTO dbo.MigrationHistory (MigrationId, ExecutedAt, ExecutedBy)
    VALUES ('003_MigrateAddressData', SYSUTCDATETIME(), SYSTEM_USER)
END
```

**The MigrationHistory table:**

```sql
CREATE TABLE [dbo].[MigrationHistory]
(
    MigrationId NVARCHAR(200) NOT NULL,
    ExecutedAt DATETIME2(7) NOT NULL,
    ExecutedBy NVARCHAR(128) NOT NULL,
    CONSTRAINT PK_MigrationHistory PRIMARY KEY (MigrationId)
)
```

### Testing Idempotency

Before committing, ask: "If I run this twice, what happens?"

- **Good:** Second run does nothing (conditions not met)
- **Bad:** Second run fails (duplicate key)
- **Worse:** Second run corrupts data (double-update)

---

## Common Mistakes

| Mistake | What happens | Fix |
|---------|--------------|-----|
| Non-idempotent INSERT | Duplicate key error on second run | Use `IF NOT EXISTS` or `MERGE` |
| UPDATE without WHERE | All rows updated, including already-correct ones | Add condition to skip already-updated rows |
| Assuming column exists | Script fails if run before schema change | Use pre-deployment for schema-dependent work, post-deployment for new-schema work |
| No progress output | Hard to debug when something fails | Add `PRINT` statements |
| Giant single script | Hard to maintain, hard to debug | Break into focused, included files |

---

## Pre-Deployment vs. Post-Deployment Decision Guide

| Scenario | Use | Why |
|----------|-----|-----|
| Backfill NULLs before NOT NULL constraint | Pre-deployment | Must happen before schema change |
| Clean orphans before adding FK | Pre-deployment | Must happen before constraint exists |
| Drop blocking index for column type change | Pre-deployment | Index prevents ALTER |
| Populate new column from existing data | Post-deployment | New column must exist first |
| Seed lookup table | Post-deployment | Table must exist first |
| Transform data to new format | Post-deployment | New structure must exist |
| One-time data fix | Post-deployment | Usually schema-independent |

---

Let me continue with the remaining consolidated Foundations sections.

---

