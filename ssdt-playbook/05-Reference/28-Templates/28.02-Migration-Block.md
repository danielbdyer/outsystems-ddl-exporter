# 28.2 Post-Deployment Migration Block Template

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
