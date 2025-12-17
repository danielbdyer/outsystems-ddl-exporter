# 12. CDC and Schema Evolution

*(Consolidating from earlier discussion)*

---

## The Core Constraint

CDC capture instances are schema-bound. When you create a capture instance, it records the table's schema at that moment. Changes to the table don't automatically update the capture instance.

**Operations requiring instance recreation:**
- Add column (if you want it tracked)
- Drop column
- Rename column
- Change data type

**Operations that don't affect CDC:**
- Add/modify/drop constraints
- Add/modify/drop indexes
- Changes to non-CDC-enabled tables

---

## Development Strategy: Accept Gaps

In development, velocity matters more than audit completeness.

**Approach:**
1. Batch schema changes
2. Disable CDC on affected tables before deploy
3. Deploy schema changes
4. Re-enable CDC after deploy

**Automation template:**

```sql
-- Pre-deployment: Disable CDC on all tables
DECLARE @sql NVARCHAR(MAX) = ''
SELECT @sql += 'EXEC sys.sp_cdc_disable_table 
    @source_schema = ''' + OBJECT_SCHEMA_NAME(source_object_id) + ''', 
    @source_name = ''' + OBJECT_NAME(source_object_id) + ''', 
    @capture_instance = ''' + capture_instance + ''';'
FROM cdc.change_tables

EXEC sp_executesql @sql

-- [SSDT deployment happens]

-- Post-deployment: Re-enable CDC (from your table list)
-- ... enable scripts ...
```

**Accepted risks:**
- History gaps during development
- Change History feature shows incomplete data in dev/test
- Building habits that need adjustment for production

---

## UAT Strategy: Communicate Gaps

In UAT, clients will see the Change History feature. Set expectations.

**Client messaging template:**

> "The Change History feature tracks all modifications to records. During this testing phase:
> 
> 1. History starts from [date] — changes before that aren't captured.
> 2. During deployments, there may be brief gaps when changes aren't recorded.
> 3. New fields appear in history going forward only.
> 
> In production, we use a process that eliminates gaps."

**Practices:**
- Maintain a gap log
- Notify before deployments
- Smoke test Change History after each deploy

---

## Production Strategy: No Gaps

In production, use the dual-instance pattern.

**Phase sequence:**

```
Release N:
1. Create new capture instance with new schema
2. Apply schema change
3. Both instances active — consumer reads from both

Release N+1 (after retention window):
4. Drop old capture instance
5. Consumer reads only from new instance
```

**Consumer abstraction:**

Your Change History code should query through an abstraction that:
- Unions results from all active instances
- Handles schema differences (missing columns in old instance)
- Manages LSN ranges correctly

---

## CDC Table Registry

Maintain a list of CDC-enabled tables. Check it before any schema change.

**Query to find CDC-enabled tables:**

```sql
SELECT 
    OBJECT_SCHEMA_NAME(source_object_id) AS SchemaName,
    OBJECT_NAME(source_object_id) AS TableName,
    capture_instance AS CaptureInstance,
    create_date AS EnabledDate
FROM cdc.change_tables
ORDER BY SchemaName, TableName
```

**Before any schema change:** "Is this table on the list? If yes, follow CDC protocol."

---

Now let me continue with the Execution Layer — the Multi-Phase Pattern Templates and Anti-Patterns Gallery.

---

