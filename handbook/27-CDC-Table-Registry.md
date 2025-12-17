# 29. CDC Table Registry

---

## Purpose

This registry tracks all CDC-enabled tables. **Check this before any schema change.**

---

## How to Query the Registry

Run this to see current CDC-enabled tables:

```sql
SELECT 
    OBJECT_SCHEMA_NAME(ct.source_object_id) AS [Schema],
    OBJECT_NAME(ct.source_object_id) AS [Table],
    ct.capture_instance AS [CaptureInstance],
    ct.create_date AS [EnabledDate],
    ct.supports_net_changes AS [NetChanges],
    (
        SELECT STRING_AGG(cc.column_name, ', ') 
        FROM cdc.captured_columns cc 
        WHERE cc.object_id = ct.object_id
    ) AS [TrackedColumns]
FROM cdc.change_tables ct
ORDER BY [Schema], [Table]
```

---

## Current CDC-Enabled Tables

*[This section should be populated with your actual tables. Example format:]*

| Schema | Table | Capture Instance | Enabled Date | Notes |
|--------|-------|------------------|--------------|-------|
| dbo | Customer | dbo_Customer_v1 | 2024-06-15 | Core entity |
| dbo | Order | dbo_Order_v1 | 2024-06-15 | Core entity |
| dbo | Policy | dbo_Policy_v1 | 2024-06-15 | Core entity |
| dbo | Claim | dbo_Claim_v1 | 2024-06-15 | Core entity |
| ... | ... | ... | ... | ... |

**Total CDC-enabled tables:** [XXX]

---

## Before Changing a CDC-Enabled Table

1. **Confirm it's on the list** — Query above or check this page
2. **Classify the change** — Does it require instance recreation? (See 18.5)
3. **Choose your strategy** — Development (gap OK) vs. Production (no gap)
4. **Document in PR** — Use the CDC Impact section of PR template
5. **Follow the protocol** — See Section 12: CDC and Schema Evolution

---

## Maintaining This Registry

**When to update:**
- New table added to CDC
- Table removed from CDC
- Capture instance recreated (version bump)

**How to update:**
- Edit this page directly
- Include in PR description when CDC changes are made
- Keep synchronized with actual database state

---

