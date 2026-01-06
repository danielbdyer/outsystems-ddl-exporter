# 28. Templates

---

## 28.1 New Table Template

```sql
/*
Table: dbo.YourTableName
Description: Brief description of what this table stores
Created: YYYY-MM-DD
Ticket: JIRA-XXXX
*/
CREATE TABLE [dbo].[YourTableName]
(
    -- Primary Key
    [YourTableNameId] INT IDENTITY(1,1) NOT NULL,
    
    -- Foreign Keys
    [RelatedTableId] INT NOT NULL,
    
    -- Business Columns
    [Name] NVARCHAR(100) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    [Status] NVARCHAR(20) NOT NULL CONSTRAINT [DF_YourTableName_Status] DEFAULT ('Active'),
    
    -- Audit Columns
    [IsActive] BIT NOT NULL CONSTRAINT [DF_YourTableName_IsActive] DEFAULT (1),
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_YourTableName_CreatedAt] DEFAULT (SYSUTCDATETIME()),
    [CreatedBy] NVARCHAR(128) NOT NULL CONSTRAINT [DF_YourTableName_CreatedBy] DEFAULT (SYSTEM_USER),
    [UpdatedAt] DATETIME2(7) NULL,
    [UpdatedBy] NVARCHAR(128) NULL,
    
    -- Constraints
    CONSTRAINT [PK_YourTableName] PRIMARY KEY CLUSTERED ([YourTableNameId]),
    CONSTRAINT [FK_YourTableName_RelatedTable] FOREIGN KEY ([RelatedTableId]) 
        REFERENCES [dbo].[RelatedTable]([RelatedTableId])
)
GO

-- Indexes
CREATE NONCLUSTERED INDEX [IX_YourTableName_RelatedTableId]
ON [dbo].[YourTableName]([RelatedTableId])
GO
```

---

## 28.2 Post-Deployment Migration Block Template

```sql
/*
Migration: Brief description
Ticket: JIRA-XXXX
Author: Your Name
Date: YYYY-MM-DD

Description:
Explain what this migration does and why.
*/

PRINT 'Migration NNN: Brief description...'

-- Idempotency check
IF EXISTS (SELECT 1 FROM dbo.YourTable WHERE YourCondition)
BEGIN
    -- Perform the migration
    UPDATE dbo.YourTable
    SET YourColumn = 'NewValue'
    WHERE YourCondition
    
    PRINT '  Updated ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' rows.'
END
ELSE
BEGIN
    PRINT '  No rows to update — skipping.'
END
GO
```

---

## 28.3 Idempotent Seed Data Template

```sql
/*
Reference Data: TableName
Description: Seeds the lookup/reference values for TableName
*/

PRINT 'Seeding TableName reference data...'

MERGE INTO [dbo].[TableName] AS target
USING (VALUES
    (1, 'Value1', 'Description 1', 1),
    (2, 'Value2', 'Description 2', 2),
    (3, 'Value3', 'Description 3', 3)
) AS source ([Id], [Code], [Description], [SortOrder])
ON target.[Id] = source.[Id]
WHEN MATCHED THEN
    UPDATE SET 
        [Code] = source.[Code],
        [Description] = source.[Description],
        [SortOrder] = source.[SortOrder]
WHEN NOT MATCHED THEN
    INSERT ([Id], [Code], [Description], [SortOrder])
    VALUES (source.[Id], source.[Code], source.[Description], source.[SortOrder]);

PRINT '  TableName seeded/updated.'
GO
```

---

## 28.4 Migration Tracking Table

If you need migration tracking for complex multi-step migrations:

```sql
-- Create this table in your schema
CREATE TABLE [dbo].[MigrationHistory]
(
    [MigrationId] NVARCHAR(200) NOT NULL,
    [ExecutedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_MigrationHistory_ExecutedAt] DEFAULT (SYSUTCDATETIME()),
    [ExecutedBy] NVARCHAR(128) NOT NULL CONSTRAINT [DF_MigrationHistory_ExecutedBy] DEFAULT (SYSTEM_USER),
    [Description] NVARCHAR(500) NULL,
    
    CONSTRAINT [PK_MigrationHistory] PRIMARY KEY CLUSTERED ([MigrationId])
)
GO
```

Usage in migration scripts:

```sql
IF NOT EXISTS (SELECT 1 FROM dbo.MigrationHistory WHERE MigrationId = 'MIG_2025.01_YourMigration')
BEGIN
    PRINT 'Running migration: MIG_2025.01_YourMigration'
    
    -- Your migration logic here
    
    INSERT INTO dbo.MigrationHistory (MigrationId, Description)
    VALUES ('MIG_2025.01_YourMigration', 'Brief description of what this did')
    
    PRINT 'Migration complete.'
END
ELSE
BEGIN
    PRINT 'Migration MIG_2025.01_YourMigration already applied — skipping.'
END
GO
```

---

## 28.5 Pre-Deployment Data Validation Template

```sql
/*
Pre-Deployment Validation: Describe what you're validating
Ticket: JIRA-XXXX
*/

PRINT 'Pre-deployment validation...'

-- Check for condition that would cause deployment to fail
DECLARE @ViolationCount INT

SELECT @ViolationCount = COUNT(*)
FROM dbo.YourTable
WHERE YourViolatingCondition

IF @ViolationCount > 0
BEGIN
    PRINT '  ERROR: Found ' + CAST(@ViolationCount AS VARCHAR(10)) + ' rows that violate the new constraint.'
    PRINT '  Deployment cannot proceed. Fix the data first.'
    RAISERROR('Pre-deployment validation failed. See above for details.', 16, 1)
    RETURN
END

PRINT '  Validation passed.'
GO
```

---

## 28.6 CDC Enable/Disable Template

**For Development (accepting gaps):**

```sql
-- Pre-deployment: Disable CDC
PRINT 'Disabling CDC on dbo.YourTable...'

IF EXISTS (SELECT 1 FROM cdc.change_tables WHERE source_object_id = OBJECT_ID('dbo.YourTable'))
BEGIN
    EXEC sys.sp_cdc_disable_table
        @source_schema = 'dbo',
        @source_name = 'YourTable',
        @capture_instance = 'dbo_YourTable'
    
    PRINT '  CDC disabled.'
END
ELSE
BEGIN
    PRINT '  CDC was not enabled — skipping.'
END
GO
```

```sql
-- Post-deployment: Re-enable CDC
PRINT 'Re-enabling CDC on dbo.YourTable...'

IF NOT EXISTS (SELECT 1 FROM cdc.change_tables WHERE source_object_id = OBJECT_ID('dbo.YourTable'))
BEGIN
    EXEC sys.sp_cdc_enable_table
        @source_schema = 'dbo',
        @source_name = 'YourTable',
        @role_name = 'cdc_reader',
        @capture_instance = 'dbo_YourTable',
        @supports_net_changes = 1
    
    PRINT '  CDC enabled.'
END
ELSE
BEGIN
    PRINT '  CDC already enabled — skipping.'
END
GO
```

**For Production (dual-instance):**

```sql
-- Post-deployment: Create new capture instance (after schema change)
PRINT 'Creating new CDC capture instance for dbo.YourTable...'

EXEC sys.sp_cdc_enable_table
    @source_schema = 'dbo',
    @source_name = 'YourTable',
    @role_name = 'cdc_reader',
    @capture_instance = 'dbo_YourTable_v2',  -- Versioned
    @supports_net_changes = 1

PRINT '  New capture instance dbo_YourTable_v2 created.'
PRINT '  Old instance dbo_YourTable_v1 still active.'
PRINT '  Drop old instance in next release after retention period.'
GO
```

---

## 28.7 Incident Report Template

Use after any incident for blameless post-mortem:

```markdown
# Incident Report: [Brief Title]

## Summary
- **Date/Time:** YYYY-MM-DD HH:MM (timezone)
- **Duration:** X hours/minutes
- **Severity:** Critical / High / Medium / Low
- **Environment:** Dev / Test / UAT / Prod
- **Ticket:** JIRA-XXXX

## What Happened
[One paragraph description of what occurred]

## Impact
- [Who was affected]
- [What functionality was broken]
- [Data impact, if any]

## Timeline
| Time | Event |
|------|-------|
| HH:MM | First indication of problem |
| HH:MM | Investigation began |
| HH:MM | Root cause identified |
| HH:MM | Fix deployed |
| HH:MM | Incident resolved |

## Root Cause
[What actually caused the incident]

## What Went Well
- [Things that helped during response]
- [Process that worked]

## What Could Be Improved
- [Gaps in process]
- [Missing safeguards]
- [Detection delays]

## Action Items
| Action | Owner | Due Date | Status |
|--------|-------|----------|--------|
| [Action] | [Name] | YYYY-MM-DD | Open |

## Playbook Updates
- [ ] New section needed: [Description]
- [ ] Existing section update: [Section + what to add]
- [ ] New anti-pattern: [Description]
- [ ] Decision aid update: [Description]

## Lessons Learned
[Key takeaways for the team]
```

---

