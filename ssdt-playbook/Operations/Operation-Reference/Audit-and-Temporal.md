# 16.8 Audit and Temporal

*These patterns track changes over time — audit columns and system-versioned temporal tables.*

---

### Add System-Versioned Temporal Table

| Summary | Tier | Mechanism |
|---------|------|-----------|
| Enable automatic history tracking | 2 (new) / 3 (existing) | Pure Declarative (new) / Multi-Phase (existing) |

**For new table:**
```sql
CREATE TABLE [dbo].[Employee]
(
    [EmployeeId] INT NOT NULL PRIMARY KEY,
    [Name] NVARCHAR(100) NOT NULL,
    [Department] NVARCHAR(50) NOT NULL,
    
    [ValidFrom] DATETIME2 GENERATED ALWAYS AS ROW START NOT NULL,
    [ValidTo] DATETIME2 GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME (ValidFrom, ValidTo)
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.EmployeeHistory))
```

**Querying:**
```sql
-- Current state
SELECT * FROM dbo.Employee

-- Point in time
SELECT * FROM dbo.Employee FOR SYSTEM_TIME AS OF '2024-06-15'

-- Full history
SELECT * FROM dbo.Employee FOR SYSTEM_TIME ALL WHERE EmployeeId = 42
```

**Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Existing table | Converting existing table to temporal requires multi-phase approach. |
| History table | System-managed; can't directly modify. |
| SSDT table rebuilds | If SSDT needs to rebuild table (column reorder), it disables/re-enables versioning. |

---

### Add Manual Audit Columns

| Summary | Tier | Mechanism |
|---------|------|-----------|
| Add CreatedAt, UpdatedAt, CreatedBy, UpdatedBy columns | 1 (new) / 2 (existing) | Pure Declarative / Post-Deployment for backfill |

**Standard audit columns:**
```sql
[CreatedAt] DATETIME2(7) NOT NULL 
    CONSTRAINT [DF_Order_CreatedAt] DEFAULT (SYSUTCDATETIME()),
[CreatedBy] NVARCHAR(128) NOT NULL 
    CONSTRAINT [DF_Order_CreatedBy] DEFAULT (SYSTEM_USER),
[UpdatedAt] DATETIME2(7) NULL,
[UpdatedBy] NVARCHAR(128) NULL
```

**For existing table — backfill:**
```sql
UPDATE dbo.[Order]
SET 
    CreatedAt = ISNULL(OrderDate, '2020-01-01'),
    CreatedBy = 'MIGRATION'
WHERE CreatedAt IS NULL
```

**Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| UpdatedAt/UpdatedBy | Database won't auto-populate. Requires trigger or application code. |
| Triggers | Can add trigger for auto-update, but adds overhead. |

---
