# 28.6 CDC Enable/Disable Template

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
