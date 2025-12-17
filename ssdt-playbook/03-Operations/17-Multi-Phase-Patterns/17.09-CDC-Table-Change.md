# 17.9 Pattern: CDC-Enabled Table Schema Change (Production)

**When to use:** Changing schema on a CDC-enabled table without audit gaps

**Scenario:** Add `MiddleName` column to CDC-enabled `Employee` table

### Phase 1 (Release N): Create New Capture Instance

```sql
-- PostDeployment script
PRINT 'Creating new CDC capture instance for Employee...'

-- Create new instance with new schema (after column is added)
EXEC sys.sp_cdc_enable_table
    @source_schema = 'dbo',
    @source_name = 'Employee',
    @capture_instance = 'dbo_Employee_v2',  -- Versioned name
    @role_name = 'cdc_reader',
    @supports_net_changes = 1

PRINT 'New capture instance created. Both v1 and v2 are now active.'
```

### Phase 2 (Release N): Add Column (Declarative)

```sql
-- Table definition change
[MiddleName] NVARCHAR(50) NULL,
```

The new capture instance (v2) tracks this column. The old instance (v1) doesn't know about it.

### Phase 3 (Release N): Update Consumer Abstraction

Change History code now queries both instances and unions results.

### Phase 4 (Release N+1, after retention): Drop Old Instance

```sql
-- PostDeployment script (after retention period)
PRINT 'Dropping old CDC capture instance...'

EXEC sys.sp_cdc_disable_table
    @source_schema = 'dbo',
    @source_name = 'Employee',
    @capture_instance = 'dbo_Employee_v1'

PRINT 'Old capture instance dropped.'
```

**Rollback notes:**
- Phase 1-3: Drop new instance, keep old
- Phase 4: Cannot restore dropped instance (but data is beyond retention anyway)

---

