# 28.5 Pre-Deployment Data Validation Template

```sql
/*
Pre-Deployment Validation: Describe what you're validating
Ticket: JIRA-XXXX
*/

PRINT 'Pre-deployment validation...'

-- Check for condition that would cause deployment to fail
DECLARE @ViolationCount INT

SELECT @ViolationCount = COUNT(*)
FROM dbo.YourTable
WHERE YourViolatingCondition

IF @ViolationCount > 0
BEGIN
    PRINT '  ERROR: Found ' + CAST(@ViolationCount AS VARCHAR(10)) + ' rows that violate the new constraint.'
    PRINT '  Deployment cannot proceed. Fix the data first.'
    RAISERROR('Pre-deployment validation failed. See above for details.', 16, 1)
    RETURN
END

PRINT '  Validation passed.'
GO
```

---
