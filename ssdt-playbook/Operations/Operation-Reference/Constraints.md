# 16.6 Constraints and Validation

*OutSystems enforced some constraints automatically. Now you have explicit control.*

---

### Set a Default Value (Add a Default Constraint)

| Summary | Tier | Mechanism |
|---------|------|-----------|
| Set a value to use when none is provided | 1 | Pure Declarative |

**What you do:**
```sql
[Status] NVARCHAR(20) NOT NULL CONSTRAINT [DF_Order_Status] DEFAULT ('Pending'),
```

Or for existing column:
```sql
-- Add as separate statement
ALTER TABLE [dbo].[Order] ADD CONSTRAINT [DF_Order_Status] DEFAULT ('Pending') FOR [Status]
```

SSDT generates the appropriate ALTER.

**Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Always name constraints | Use `DF_TableName_ColumnName`. Auto-generated names are ugly and vary. |
| Existing default | If changing, SSDT drops old and creates new. Brief window with no default. |

---

### Make an Attribute Unique (Unique Index)

| Summary | Tier | Mechanism |
|---------|------|-----------|
| Enforce distinct values in a column | 2 | Pure Declarative |

**What you do:**
```sql
CREATE UNIQUE INDEX [UIX_Customer_Email]
    ON [dbo].[Customer]([Email])
```

**Pre-flight check:**
```sql
-- Find duplicates
SELECT Email, COUNT(*) AS Count
FROM dbo.Customer
GROUP BY Email
HAVING COUNT(*) > 1
-- Must return 0 rows
```

**Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Duplicates | Deploy fails if duplicates exist. Clean first. |
| NULLs | Standard unique index allows one NULL. For multiple NULLs, use filtered unique index. |

---

### Add a Validation Rule (Check Constraint)

| Summary | Tier | Mechanism |
|---------|------|-----------|
| Enforce business rules at the database level | 1-2 | Pure Declarative |

**What you do:**
```sql
CONSTRAINT [CK_OrderLine_PositiveQuantity] CHECK ([Quantity] > 0)
CONSTRAINT [CK_Order_ValidDates] CHECK ([EndDate] >= [StartDate])
```

**Pre-flight check:**
```sql
-- Find violations
SELECT * FROM dbo.OrderLine WHERE Quantity <= 0
-- Must return 0 rows
```

**Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Existing violations | It doesn't cleanly fail-and-vanish: SSDT adds the check **`WITH NOCHECK`** (so it lands), then a separate **`WITH CHECK CHECK`** validates the existing rows and fails on the violating row (`Msg 547`). The deploy is refused, but the constraint **lingers, untrusted** (`is_not_trusted=1`) — the bad row survives and the optimizer ignores the rule. Reconcile the data first, then let it validate trusted (`is_not_trusted=0`); if a prior attempt left an untrusted check, drop or re-validate it. |
| Complex checks | Very complex checks can impact performance. Keep them simple. |

---

### Enable/Disable Constraint

| Summary | Tier | Mechanism |
|---------|------|-----------|
| Temporarily suspend constraint enforcement | 3 | Script-Only |

This is **operational**, not declarative. SSDT manages existence, not enabled state.

**Disable:**
```sql
ALTER TABLE dbo.[Order] NOCHECK CONSTRAINT FK_Order_Customer
```

**Re-enable WITH validation:**
```sql
ALTER TABLE dbo.[Order] WITH CHECK CHECK CONSTRAINT FK_Order_Customer
```

**Note:** `NOCHECK` leaves the constraint untrusted. `WITH CHECK CHECK` validates and restores trust.

**Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Untrusted constraints | Optimizer ignores them. Always restore trust. |
| Use case | Typically for bulk loads or multi-phase migrations. |

---
