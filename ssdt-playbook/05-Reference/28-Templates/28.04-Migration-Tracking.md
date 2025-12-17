# 28.4 Migration Tracking Table

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
