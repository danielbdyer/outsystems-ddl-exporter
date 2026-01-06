# 28.1 New Table Template

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
