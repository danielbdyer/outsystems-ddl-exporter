# 16.5 Working with Static Entities (Lookup Tables)

*In OutSystems, Static Entities had their data built in. Now you have a table structure (declarative) plus seed data (post-deployment script).*

---

### Create a Lookup Table with Seed Data

| Summary | Tier | Mechanism |
|---------|------|-----------|
| Create a reference/code table with fixed values | 1-2 | Declarative (structure) + Post-Deployment (data) |

**Two pieces:**
1. Table structure (declarative `.sql` file)
2. Seed data (idempotent post-deployment script)

**Structure:**
```sql
-- /Tables/dbo/dbo.OrderStatus.sql
CREATE TABLE [dbo].[OrderStatus]
(
    [StatusId] INT NOT NULL
        CONSTRAINT [PK_OrderStatus_StatusId]
            PRIMARY KEY CLUSTERED,
    [StatusCode] NVARCHAR(20) NOT NULL,
    [StatusName] NVARCHAR(50) NOT NULL,
    [SortOrder] INT NOT NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_OrderStatus_IsActive] DEFAULT (1)
)

GO

CREATE UNIQUE INDEX [UIX_OrderStatus_StatusCode]
    ON [dbo].[OrderStatus]([StatusCode])
```

**Seed data:**
```sql
-- /Scripts/PostDeployment/ReferenceData/SeedOrderStatus.sql

MERGE INTO [dbo].[OrderStatus] AS target
USING (VALUES
    (1, 'PENDING', 'Pending', 1, 1),
    (2, 'PROCESSING', 'Processing', 2, 1),
    (3, 'SHIPPED', 'Shipped', 3, 1),
    (4, 'DELIVERED', 'Delivered', 4, 1),
    (5, 'CANCELLED', 'Cancelled', 5, 1)
) AS source ([StatusId], [StatusCode], [StatusName], [SortOrder], [IsActive])
ON target.[StatusId] = source.[StatusId]
WHEN MATCHED THEN
    UPDATE SET 
        [StatusCode] = source.[StatusCode],
        [StatusName] = source.[StatusName],
        [SortOrder] = source.[SortOrder],
        [IsActive] = source.[IsActive]
WHEN NOT MATCHED THEN
    INSERT ([StatusId], [StatusCode], [StatusName], [SortOrder], [IsActive])
    VALUES (source.[StatusId], source.[StatusCode], source.[StatusName], source.[SortOrder], source.[IsActive]);
```

**Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| IDENTITY vs. explicit IDs | For lookup tables, usually use explicit IDs (no IDENTITY) so values are consistent across environments. |
| Idempotency | Use MERGE for upsert. Don't use plain INSERT. |
| FK dependencies | Seed parent tables before child tables. |

**Related:**
- Template: [28.03 Seed Data](../../Reference/Templates/Seed-Data.md)

---

### Add/Modify Seed Data

| Summary | Tier | Mechanism |
|---------|------|-----------|
| Add or update values in a lookup table | 1-2 | Post-Deployment Script |

**What you do:**

Edit the seed data script. Add new values or modify existing:

```sql
-- Add to the VALUES list
    (6, 'RETURNED', 'Returned', 6, 1),  -- New value
```

MERGE handles both insert (new) and update (existing).

**Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Deleting values | MERGE doesn't delete by default. If you need to deactivate, set `IsActive = 0` rather than deleting. |
| FK references | Can't delete values that are referenced by FKs. |

---

### Extract Values to a Lookup Table

| Summary | Tier | Mechanism |
|---------|------|-----------|
| Convert inline values to a normalized lookup table | 3 | Multi-Phase |

**Scenario:** `Order.Status` is `VARCHAR(20)` with values like 'Pending', 'Active'. Extract to `OrderStatus` table.

**Phase 1:** Create lookup table, populate with distinct values
**Phase 2:** Add `Order.StatusId` column
**Phase 3:** Post-deployment: populate `StatusId` from `Status` text
**Phase 4:** Application transitions to FK
**Phase 5:** Next release: drop `Status` column, add FK constraint

**Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Data quality | What if `Status` has typos or variations? Clean before extracting. |
| Multi-release | This spans multiple releases. Plan the sequence. |

---
