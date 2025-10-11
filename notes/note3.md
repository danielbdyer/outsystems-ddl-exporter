# Week 1 Final Playbooks
## Troubleshooting & Code Review Guides

---

## 📘 Playbook 5: Troubleshooting Flowchart & Common Issues

**Purpose**: Get unstuck fast when things go wrong  
**Time to resolve**: 5-30 minutes depending on issue

---

### Master Troubleshooting Flowchart

```
Something went wrong?
│
├─ Build Failed? → Section A: Build Errors
│
├─ Deployment Failed? → Section B: Deployment Errors
│
├─ OutSystems Issue? → Section C: Integration Studio Problems
│
├─ Performance Problem? → Section D: Slow Queries
│
└─ Git/Merge Conflict? → Section E: Version Control Issues
```

---

## Section A: Build Failed

### Flowchart

```
Build failed in Visual Studio?
│
├─ Error: "SQL71501: Unresolved reference"
│  └─ Fix: Referenced object doesn't exist
│     ├─ Check spelling of table/column
│     ├─ Verify object exists in project
│     └─ Add database reference if external
│
├─ Error: "SQL71006: Only one statement per batch"
│  └─ Fix: Missing GO separators
│     └─ Add GO between CREATE statements
│
├─ Error: "Incorrect syntax near ')'"
│  └─ Fix: Missing or extra comma
│     ├─ Check commas between columns
│     └─ Check no comma before closing )
│
├─ Error: "SQL71558: Cannot rename [X] to [Y]"
│  └─ Fix: Conflicting refactorlog
│     └─ Use Refactor → Rename, don't edit manually
│
└─ Other syntax error
   └─ Copy error to #database-help with screenshot
```

### Common Build Errors & Fixes

#### Error 1: Unresolved Reference

**Full error**:
```
SQL71501: Procedure [dbo].[GetOrders] has an unresolved 
reference to object [dbo].[Orders].[CustomerId]
```

**Meaning**: You referenced a column/table that doesn't exist in the project

**Fixes**:

**Fix 1: Typo in name**
```sql
-- Wrong:
SELECT CustomerID FROM Orders  -- ❌ ID capitalized wrong

-- Right:
SELECT CustomerId FROM Orders  -- ✅ Matches [CustomerId] definition
```

**Fix 2: Object not in project**
```
Solution: Add the table/view/procedure to project
- If it's your table: Create it
- If it's external: Add database reference
```

**Fix 3: Wrong schema**
```sql
-- Wrong:
SELECT * FROM Orders  -- ❌ Missing schema

-- Right:
SELECT * FROM [dbo].[Orders]  -- ✅ Explicit schema
```

---

#### Error 2: Missing GO Separator

**Full error**:
```
SQL71006: Only one statement is allowed per batch. 
Use GO to separate statements.
```

**Meaning**: Multiple DDL statements without GO between them

**Fix**:
```sql
-- Wrong:
CREATE TABLE Table1 (...);
CREATE TABLE Table2 (...);  -- ❌ Error here

-- Right:
CREATE TABLE Table1 (...);
GO  -- ← Separator added
CREATE TABLE Table2 (...);
GO
```

**Rule**: Put GO after every CREATE TABLE, CREATE INDEX, ALTER TABLE, etc.

---

#### Error 3: Comma Issues

**Full error**:
```
Incorrect syntax near ')'.
```

**Meaning**: Usually missing or extra comma

**Common cases**:

**Missing comma**:
```sql
CREATE TABLE Customers (
    CustomerId INT PRIMARY KEY,
    FirstName NVARCHAR(50)  -- ❌ Missing comma
    LastName NVARCHAR(50)
);
```

**Extra comma**:
```sql
CREATE TABLE Customers (
    CustomerId INT PRIMARY KEY,
    FirstName NVARCHAR(50),
    LastName NVARCHAR(50),  -- ❌ Extra comma before )
);
```

**Fix**: Review column list carefully

---

#### Error 4: Conflicting Refactorlog

**Full error**:
```
SQL71558: The referenced table [Products] could not 
be found. Use Rename Refactor instead of manual rename.
```

**Meaning**: You manually renamed a table instead of using Refactor menu

**Fix**:
1. Undo your manual rename
2. Right-click table name → Refactor → Rename
3. Enter new name
4. Let Visual Studio update refactorlog
5. Build again

**Prevention**: NEVER manually rename tables/columns in CREATE statements

---

### Quick Build Checklist

If build fails:
1. ☐ Read error message carefully
2. ☐ Double-click error to jump to problem location
3. ☐ Check for typos (case-sensitive!)
4. ☐ Check for missing commas
5. ☐ Check for missing GO separators
6. ☐ Try Clean Solution → Rebuild
7. ☐ Still stuck after 15 min? → #database-help with error screenshot

---

## Section B: Deployment Failed

### Flowchart

```
Deployment to database failed?
│
├─ Error: "Timeout expired"
│  └─ Fix: Increase timeout or batch operations
│
├─ Error: "Possible data loss detected"
│  └─ Fix: Dropping column/table, need migration
│     └─ Talk to dev lead
│
├─ Error: "Cannot insert NULL into non-null column"
│  └─ Fix: Adding NOT NULL without DEFAULT
│     └─ Add DEFAULT or make nullable
│
├─ Error: "Violation of PRIMARY KEY constraint"
│  └─ Fix: Duplicate data in post-deployment script
│     └─ Use MERGE instead of INSERT
│
└─ Error: "Object already exists"
   └─ Fix: Object in database but not project
      └─ Schema drift - add to project
```

### Common Deployment Errors

#### Error 1: Timeout Expired

**Full error**:
```
Timeout expired. The timeout period elapsed prior to 
completion of the operation or the server is not responding.
```

**Causes**:
- Large table operation (millions of rows)
- Long-running data migration
- Server is overloaded

**Fixes**:

**Fix 1: Increase timeout**
```powershell
SqlPackage.exe /Action:Publish `
    /SourceFile:"MyDatabase.dacpac" `
    /TargetServerName:"Server" `
    /p:CommandTimeout=3600  # 60 minutes
```

**Fix 2: Move large operations to separate script**
```sql
-- Instead of in DACPAC deployment, run separately
-- batch-update-script.sql
DECLARE @BatchSize INT = 10000;
WHILE EXISTS (SELECT 1 FROM Orders WHERE Processed = 0)
BEGIN
    UPDATE TOP (@BatchSize) Orders
    SET Processed = 1
    WHERE Processed = 0;
    
    WAITFOR DELAY '00:00:05';  -- 5 second pause
END
```

**Fix 3: Deploy during off-hours**
- Less load on server
- More resources available

---

#### Error 2: Possible Data Loss

**Full error**:
```
Deployment blocked due to possible data loss.
(SqlPackage.Deploy.ErrorDetectionException)
```

**Causes**:
- Dropping column that has data
- Changing data type to smaller size (VARCHAR(100) → VARCHAR(50))
- Dropping table

**Fixes**:

**If data loss is intentional** (dev environment):
```powershell
SqlPackage.exe /Action:Publish `
    /p:BlockOnPossibleDataLoss=False  # Allow data loss
```

**If data must be preserved** (test/prod):
1. Write pre-deployment script to backup data
2. Apply schema change
3. Write post-deployment script to restore/migrate data
4. Get dev lead review

**Example**:
```sql
-- Pre-deployment: Backup column to temp table
SELECT CustomerId, OldColumnData
INTO #TempBackup
FROM Customers;

-- (DACPAC drops OldColumnData column)

-- Post-deployment: Migrate to new structure
UPDATE c
SET c.NewColumn = t.OldColumnData
FROM Customers c
INNER JOIN #TempBackup t ON c.CustomerId = t.CustomerId;

DROP TABLE #TempBackup;
```

---

#### Error 3: Cannot Insert NULL

**Full error**:
```
Cannot insert the value NULL into column 'IsActive', 
table 'Orders'; column does not allow nulls.
```

**Cause**: Adding NOT NULL column to existing table without DEFAULT

**Fix**: Add DEFAULT value
```sql
-- Wrong:
[IsActive] BIT NOT NULL  -- ❌ Existing rows can't become NOT NULL

-- Right:
[IsActive] BIT NOT NULL DEFAULT 1  -- ✅ Existing rows get 1
```

---

#### Error 4: Primary Key Violation in Post-Deployment

**Full error**:
```
Violation of PRIMARY KEY constraint 'PK_OrderStatus'. 
Cannot insert duplicate key.
```

**Cause**: Post-deployment script trying to INSERT duplicate data

**Fix**: Use MERGE instead of INSERT
```sql
-- Wrong:
INSERT INTO OrderStatus VALUES (1, 'Pending');  -- ❌ Fails on second run

-- Right:
MERGE INTO OrderStatus AS Target
USING (VALUES (1, 'Pending')) AS Source (Id, Name)
ON Target.OrderStatusId = Source.Id
WHEN NOT MATCHED THEN
    INSERT VALUES (Source.Id, Source.Name);  -- ✅ Idempotent
```

---

## Section C: Integration Studio Problems

### Flowchart

```
OutSystems Integration Studio issue?
│
├─ Can't connect to database
│  └─ Check VPN, firewall, connection string
│
├─ Refresh table shows no changes
│  └─ Connected to wrong database or change not deployed
│
├─ Extension won't publish
│  └─ Invalid data type mapping or compilation error
│
└─ Service Studio doesn't see new attributes
   └─ Refresh Server Data, restart Service Studio
```

### Common Integration Studio Issues

#### Issue 1: Can't Connect to Database

**Symptoms**:
- Connection dialog fails
- "Login failed" error
- Timeout connecting

**Fixes**:

**Fix 1: Check connection string**
```
Server: DevServer.database.windows.net  ← Correct server?
Database: MyDatabase  ← Correct database name?
Authentication: SQL Server  ← Correct auth type?
Username/Password: ...  ← Correct credentials?
```

**Fix 2: Test in SSMS first**
```
1. Open SQL Server Management Studio
2. Use same connection info
3. Can you connect?
   - Yes → Integration Studio config issue
   - No → Network/permissions issue
```

**Fix 3: Check VPN/Firewall**
- Connected to VPN if required?
- Firewall allows connection?
- Database server allows your IP?

---

#### Issue 2: Refresh Table Shows No Changes

**Symptoms**:
- Added column in database
- Refresh Table in Integration Studio
- New column doesn't appear

**Fixes**:

**Fix 1: Verify change deployed**
```sql
-- In SSMS, connect to same database Integration Studio uses
SELECT COLUMN_NAME 
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_NAME = 'Customers';

-- Do you see your new column?
- Yes → Integration Studio connection issue
- No → Database change not deployed to this server
```

**Fix 2: Check connected to correct environment**
```
Integration Studio → Menu → Connect → Database
→ Verify connection string points to DEV (not TEST/PROD)
```

**Fix 3: Force refresh**
```
1. Close Integration Studio
2. Delete cached connection (if option available)
3. Reopen Integration Studio
4. Reconnect to database
5. Refresh table again
```

---

#### Issue 3: Extension Won't Publish

**Symptoms**:
- Click Publish in Integration Studio
- Error: "Invalid data type mapping"
- Or: "Compilation error"

**Common causes & fixes**:

**Cause 1: Unsupported data type**
```
SQL: VARCHAR(MAX) → OutSystems: ❌ No mapping
Fix: Use NVARCHAR(2000) or split into multiple columns

SQL: GEOGRAPHY → OutSystems: ❌ Not supported
Fix: Store as Text (WKT format) or separate Lat/Long columns

SQL: DECIMAL(20,6) → OutSystems: Works but check precision
```

**Cause 2: Column name with spaces**
```
Integration Studio shows: "Phone Number" ← Spaces not allowed

Fix:
1. Right-click attribute → Properties
2. Name: PhoneNumber (remove space)
3. Publish again
```

**Cause 3: Missing identifier**
```
Error: "Entity must have identifier"

Fix:
1. Right-click primary key column
2. Properties → Set as Identifier
3. Publish again
```

---

#### Issue 4: Service Studio Doesn't See New Attributes

**Symptoms**:
- Published extension successfully
- Open Service Studio
- Manage Dependencies → New attribute not visible

**Fixes**:

**Fix 1: Refresh Server Data**
```
Service Studio
→ Menu: Module → Refresh Server Data
→ Wait 30-60 seconds
→ Try Manage Dependencies again
```

**Fix 2: Restart Service Studio**
```
1. Close Service Studio completely
2. Reopen
3. Manage Dependencies
→ New attributes should appear
```

**Fix 3: Clear cache**
```
1. Close Service Studio
2. Navigate to: %LOCALAPPDATA%\OutSystems\ServiceStudio
3. Delete "Cache" folder
4. Restart Service Studio
```

---

## Section D: Performance Problems

### Flowchart

```
Query is slow?
│
├─ Get execution plan (SSMS: Ctrl+M)
│  │
│  ├─ See "Table Scan"? → Need index
│  ├─ See "Index Scan"? → Wrong index or need better
│  ├─ See "Key Lookup"? → Need covering index
│  └─ See "Sort" or "Hash Join"? → Need appropriate index
│
└─ Immediate action:
   ├─ Add index on WHERE columns
   ├─ Add index on JOIN columns
   └─ Consider covering index for frequent queries
```

### Quick Performance Diagnostics

#### Step 1: Get Execution Plan

**In SSMS**:
```sql
-- Enable execution plan
SET STATISTICS TIME ON;
SET SHOWPLAN_TEXT ON;
GO

-- Run your slow query
SELECT * FROM Orders WHERE CustomerId = 123;

-- Check:
-- 1. CPU time (in messages tab)
-- 2. Execution plan (separate tab)
```

#### Step 2: Identify Problem

**Look for these in execution plan**:

| Operator | Meaning | Fix |
|----------|---------|-----|
| **Table Scan** | Reading every row | Add index on filter columns |
| **Index Scan** | Reading entire index | Wrong index, need more selective |
| **Key Lookup** | Extra table read per row | Add covering index (INCLUDE) |
| **Sort** | Sorting results | Add index on ORDER BY columns |
| **Hash Match (Join)** | Joining without index | Add index on JOIN columns |

#### Step 3: Quick Fixes

**Problem 1: Table Scan on WHERE clause**
```sql
-- Slow query:
SELECT * FROM Orders WHERE CustomerId = 123;  -- Table Scan

-- Fix: Add index
CREATE NONCLUSTERED INDEX IX_Orders_CustomerId
    ON Orders(CustomerId);
```

**Problem 2: Key Lookup on SELECT columns**
```sql
-- Query uses index but then looks up other columns
SELECT OrderId, OrderDate, TotalAmount
FROM Orders 
WHERE CustomerId = 123;  -- Uses index, but Key Lookup for other columns

-- Fix: Covering index
CREATE NONCLUSTERED INDEX IX_Orders_CustomerId_Covering
    ON Orders(CustomerId)
    INCLUDE (OrderDate, TotalAmount);  -- Include other columns
```

**Problem 3: Non-SARGable query**
```sql
-- Wrong: Function on indexed column
WHERE YEAR(OrderDate) = 2024  -- ❌ Can't use index

-- Right: Direct comparison
WHERE OrderDate >= '2024-01-01' 
  AND OrderDate < '2025-01-01'  -- ✅ Can use index
```

---

## Section E: Version Control Issues

### Flowchart

```
Git/merge conflict?
│
├─ Conflict in .sql file
│  └─ Fix: Edit .sql file, keep both changes if possible
│
├─ Conflict in refactorlog
│  └─ Fix: Keep both rename operations, careful with order
│
└─ Uncommitted changes block pull
   └─ Fix: Commit or stash changes first
```

### Resolving Merge Conflicts

#### Conflict Type 1: Different Columns Added

**Git shows**:
```sql
< < < < < < HEAD (your changes)
    [PhoneNumber] VARCHAR(20) NULL,
= = = = = =
    [DateOfBirth] DATE NULL,
> > > > > > main (their changes)
```

**Resolution**: Keep both!
```sql
    [PhoneNumber] VARCHAR(20) NULL,
    [DateOfBirth] DATE NULL,
```

**Steps**:
1. Remove conflict markers (`< < < < < < HEAD`, `= = = = = =`, `> > > > > > main` when shown without spaces)

> ℹ️ Git renders these markers without spaces (`<<<<<<<`, `=======`, `>>>>>>>`). They are spaced here so automated scans don't mistake this guide for an unresolved conflict.
2. Combine both columns
3. Ensure comma placement correct
4. Build project (Ctrl+Shift+B)
5. If build passes, commit merge

---

#### Conflict Type 2: Refactorlog

**Conflict in .refactorlog XML**:
```xml
< < < < < < HEAD
<Operation Name="Rename Refactor" Key="abc123" ...>
    <Property Name="NewName" Value="[ProductCategory]" />
= = = = = =
<Operation Name="Rename Refactor" Key="def456" ...>
    <Property Name="NewName" Value="[OrderStatus]" />
> > > > > > main
```

**Resolution**: Keep both operations
```xml
<Operation Name="Rename Refactor" Key="abc123" ...>
    <Property Name="NewName" Value="[ProductCategory]" />
</Operation>
<Operation Name="Rename Refactor" Key="def456" ...>
    <Property Name="NewName" Value="[OrderStatus]" />
</Operation>
```

**⚠️ Critical**: Don't delete refactorlog operations!
- They prevent data loss during deployment
- Both renames are independent

---

## Quick Reference: When to Escalate

### Handle Yourself ✅ (5-15 min):
- Build errors (syntax, missing comma, typo)
- Simple deployment to Dev
- Adding columns, tables, indexes
- Post-deployment script tweaks
- Integration Studio refresh

### Ask Dev Lead 🟡 (15+ min stuck):
- Deployment errors you can't debug
- Refactorlog conflicts
- Complex merge conflicts
- Performance optimization
- Data migration planning
- "Blocked for > 15 minutes"

### Escalate Immediately 🔴:
- Production deployment failed
- Data loss occurred
- Multiple people blocked
- Security concerns
- Cannot rollback error

**Who to contact**:
- Dev Lead: Your assigned lead (PR reviews, questions)
- DB Team: #database-help (technical issues)
- On-Call: @db-on-call (production emergencies)

---

## Self-Help First Steps

**Before asking for help**:

1. ☐ Read error message completely
2. ☐ Google exact error text (often finds answer)
3. ☐ Check this troubleshooting guide
4. ☐ Try obvious fix (typo? missing comma?)
5. ☐ Review your recent changes (what did you change?)
6. ☐ Check if someone else had same issue (search #database-help history)
7. ☐ Tried for 15+ minutes? → NOW ask for help

**When asking for help, include**:
- What you were trying to do
- Exact error message (copy/paste or screenshot)
- What you've tried already
- Environment (Dev? Test? Prod?)
- How urgent (blocking? nice-to-have?)

---

## 📘 Playbook 6: Code Review Checklist for Dev Leads

**Purpose**: Consistent, thorough PR reviews  
**Time**: 10-15 minutes per PR  
**Audience**: Dev Leads

---

## Review Process Overview

```
PR submitted
│
├─ Quick Check (2 min)
│  ├─ Build passes? → Continue
│  └─ Build fails? → Comment: "Fix build errors first", stop review
│
├─ Automated Checks (1 min)
│  ├─ All green? → Continue
│  └─ Any red? → Check what failed
│
├─ Schema Review (5 min)
│  ├─ Naming conventions
│  ├─ Data types
│  ├─ NULL vs NOT NULL
│  └─ Foreign keys have indexes
│
├─ Script Review (3 min)
│  ├─ Pre/post scripts idempotent?
│  └─ Refactorlog correct?
│
└─ Business Logic (2 min)
   ├─ Makes sense for feature?
   ├─ Breaking change?
   └─ Deployment risk?
```

---

## Section 1: Automated Checks (2 min)

### Build Status

**Check**: Green checkmark on PR

**If failed**:
```
Comment:
"Build failed - please fix errors and repush. 
Check build logs for details."

Action: Do not continue review until build passes
```

**Why**: No point reviewing if it won't compile

---

### Schema Compare

**Check**: Does generated script look reasonable?

**Look for**:
```
✅ ALTER TABLE ADD column (good - additive)
✅ CREATE INDEX (good - additive)
⚠️ ALTER TABLE DROP column (caution - data loss?)
⚠️ DROP TABLE (caution - data loss?)
❌ Massive script (red flag - what's happening?)
```

**If DROP operations**:
```
Question: "This drops [object]. Is data loss intentional?
If yes, document migration plan. If no, use Refactor → Rename."
```

---

## Section 2: Naming Conventions (2 min)

### Quick Checklist

Check against team standards:

```
Tables:
☐ Singular noun (Order not Orders)
☐ PascalCase (OrderItem not order_item)
☐ No prefixes (Product not tblProduct)

Columns:
☐ PascalCase (FirstName not firstname)
☐ Descriptive (FirstName not FName)
☐ No reserved words (avoid Name, Value, etc.)

Primary Keys:
☐ [TableName]Id pattern (CustomerId, OrderId)
☐ INT IDENTITY (or BIGINT for high volume)
☐ NOT NULL PRIMARY KEY

Foreign Keys:
☐ FK_ChildTable_ParentTable
☐ Example: FK_Orders_Customers

Indexes:
☐ IX_TableName_Column1_Column2
☐ Example: IX_Orders_CustomerId_OrderDate

Constraints:
☐ CK_TableName_ColumnName (check constraints)
☐ DF_TableName_ColumnName (defaults)
```

**Common issues**:

❌ **Plural table names**
```
Comment: "Please rename 'Customers' to 'Customer' (team standard: singular)"
```

❌ **snake_case or lowercase**
```
Comment: "Please use PascalCase: 'order_date' → 'OrderDate'"
```

❌ **Abbreviations**
```
Comment: "Please spell out: 'Qty' → 'Quantity', 'Amt' → 'Amount'"
```

---

## Section 3: Data Types Review (2 min)

### Common Issues Checklist

```
☐ User-facing text uses NVARCHAR (not VARCHAR)
☐ Dates use DATETIME2 (not old DATETIME)
☐ Money uses DECIMAL(10,2) (not FLOAT or MONEY type)
☐ Flags use BIT (not CHAR(1) or TINYINT)
☐ No NVARCHAR(MAX) unless truly needed
☐ INT sufficient (or needs BIGINT for high volume?)
```

**Review examples**:

❌ **VARCHAR for names**
```sql
[FirstName] VARCHAR(50)  -- ❌

Comment: "Use NVARCHAR for user-entered names (supports Unicode)"
Suggestion: [FirstName] NVARCHAR(50)
```

❌ **Old DATETIME**
```sql
[OrderDate] DATETIME  -- ❌

Comment: "Use DATETIME2 (SQL Server 2008+ standard)"
Suggestion: [OrderDate] DATETIME2
```

❌ **FLOAT for money**
```sql
[Price] FLOAT  -- ❌ Rounding errors!

Comment: "Use DECIMAL for money to avoid rounding errors"
Suggestion: [Price] DECIMAL(10,2)
```

❌ **NVARCHAR(MAX) unnecessarily**
```sql
[Description] NVARCHAR(MAX)  -- ❌ Performance impact

Comment: "Consider specific length: NVARCHAR(500) or NVARCHAR(2000).
Only use MAX if truly unlimited length needed."
```

---

## Section 4: NULL vs NOT NULL (1 min)

### Review Checklist

```
☐ Primary keys: NOT NULL (always)
☐ Foreign keys: Usually NOT NULL (unless optional relationship)
☐ Required business fields: NOT NULL
☐ Optional fields: NULL
☐ NOT NULL on existing table: Has DEFAULT or migration plan?
```

**Common issues**:

❌ **NOT NULL without DEFAULT on existing table**
```sql
-- Adding to table with data:
[IsActive] BIT NOT NULL  -- ❌ Will fail deployment!

Comment: "Table has existing data. Either:
1. Add DEFAULT: [IsActive] BIT NOT NULL DEFAULT 1
2. Or add as NULL first, migrate data, then NOT NULL later"
```

❌ **Foreign key is NULL but shouldn't be**
```sql
[CustomerId] INT NULL  -- ❌ Order without customer?

Comment: "Should this FK be NOT NULL? An order must have a customer."
Suggestion: [CustomerId] INT NOT NULL
```

---

## Section 5: Indexes (2 min)

### Critical Check: Foreign Keys Have Indexes

**Rule**: Every FK column needs supporting index

**Review**:
```
1. Find all FOREIGN KEY constraints in PR
2. For each FK column, verify index exists
3. If missing, request addition
```

**Example**:
```sql
-- PR shows:
ALTER TABLE Orders
ADD CONSTRAINT FK_Orders_Customers
    FOREIGN KEY (CustomerId)
    REFERENCES Customers(CustomerId);

-- ☐ Check: Is there also an index on CustomerId?

-- If missing:
Comment: "Please add index on CustomerId to support this FK:

CREATE NONCLUSTERED INDEX IX_Orders_CustomerId
    ON Orders(CustomerId);

This prevents locking issues and improves JOIN performance."
```

### Other Index Reviews

```
☐ Index naming follows convention (IX_TableName_Columns)
☐ Composite index column order makes sense (most selective first)
☐ INCLUDE clause used appropriately (covering index)
☐ No obvious duplicate indexes
```

---

## Section 6: Pre/Post Deployment Scripts (2 min)

### Idempotency Check

**Critical**: Scripts must be safe to run multiple times

**Review patterns**:

✅ **Good patterns**:
```sql
-- MERGE for reference data
MERGE INTO OrderStatus ...

-- IF NOT EXISTS for one-time operations
IF NOT EXISTS (SELECT 1 FROM SchemaVersion WHERE Version = 1)
BEGIN
    -- Do work
    INSERT INTO SchemaVersion ...
END

-- Conditional UPDATE
UPDATE Customers
SET PhoneNumber = '555-0000'
WHERE PhoneNumber IS NULL;  -- ✅ Only touches NULL rows
```

❌ **Bad patterns**:
```sql
-- Plain INSERT (will duplicate!)
INSERT INTO OrderStatus VALUES (1, 'Pending');  -- ❌

-- UPDATE without WHERE (affects all rows every time!)
UPDATE Orders SET Processed = 1;  -- ❌

-- CREATE without check (will fail on re-run)
CREATE TABLE #Temp (...);  -- ❌ Already exists on re-run
```

**If non-idempotent**:
```
Comment: "This script is not idempotent (will fail/duplicate on re-run).
Please use MERGE or IF NOT EXISTS pattern. See example in
[link to Playbook 4]."
```

---

## Section 7: Refactorlog Review (1 min)

### Check for Manual Renames

**⚠️ Critical check**: Refactorlog must exist for renames

**Review**:
```
1. Look at files changed in PR
2. Any .refactorlog file?
   - Yes → Rename was done correctly ✅
   - No but table/column renamed → DANGER ❌
```

**If renamed without refactorlog**:
```
BLOCKING Comment:
"This appears to be a rename but no refactorlog entry.
This will cause DATA LOSS on deployment.

Please:
1. Revert this commit
2. Use Refactor → Rename in Visual Studio
3. Commit the refactorlog file
4. Resubmit PR

Do not proceed with this PR as-is."
```

**Why this is critical**:
- Without refactorlog, DACPAC sees: DROP old table, CREATE new table
- All data in old table lost!
- Refactorlog tells DACPAC to use sp_rename (preserves data)

---

## Section 8: Breaking Changes (2 min)

### Identify Breaking Changes

**Breaking change = affects existing OutSystems apps**

**Examples**:
- Dropping columns
- Renaming columns (even with refactorlog!)
- Changing data types (VARCHAR(100) → VARCHAR(50))
- Dropping tables
- Changing NOT NULL constraints

**Review**:
```
☐ Does PR description mention breaking changes?
☐ If breaking, is impact documented?
☐ Is deployment plan documented?
☐ Are affected OutSystems apps listed?
☐ Is coordination with app teams planned?
```

**If breaking but not documented**:
```
Comment:
"This is a breaking change (drops/renames [X]). Please document:
- Which OutSystems apps are affected?
- What's the deployment sequence?
- Who is updating affected apps?
- Testing plan?

Cannot approve until impact is clear."
```

**Deployment sequence for breaking changes**:
```
Correct order:
1. Deploy DB change to Dev
2. Update OutSystems apps in Dev
3. Test in Dev
4. Deploy DB change to Test
5. Update OutSystems apps in Test
6. QA in Test
7. Coordinate Prod deployment (DB + apps same evening)
```

---

## Section 9: Business Logic Check (2 min)

### Does This Make Sense?

**Questions to ask**:

```
☐ Does this align with feature requirements?
☐ Are field lengths appropriate for business need?
☐ Do constraints make business sense?
☐ Are default values reasonable?
☐ Is soft delete needed ([IsDeleted] flag)?
```

**Examples**:

**Check 1: Field length**
```sql
[Email] NVARCHAR(50)  -- ⚠️ Too short?

Question: "Email addresses can be up to 254 chars.
Consider NVARCHAR(100) or NVARCHAR(254)?"
```

**Check 2: Missing audit columns**
```sql
CREATE TABLE Orders (
    OrderId INT PRIMARY KEY,
    CustomerId INT,
    OrderDate DATETIME2
);
-- ⚠️ No CreatedBy, ModifiedDate?

Comment: "Please add standard audit columns per team guidelines:
- CreatedDate DATETIME2 NOT NULL DEFAULT GETDATE()
- CreatedBy NVARCHAR(100) NOT NULL DEFAULT SUSER_NAME()
- ModifiedDate DATETIME2 NULL
- ModifiedBy NVARCHAR(100) NULL"
```

---

## Section 10: Security Review (1 min)

### Quick Security Checks

```
☐ No passwords or API keys in scripts?
☐ No customer PII in comments?
☐ No hard-coded connection strings?
☐ Sensitive columns (SSN, Credit Card) properly typed?
☐ No SQL injection vectors (if dynamic SQL)?
```

**If security concern**:
```
BLOCKING Comment:
"Security issue: [describe issue]

Please:
- Remove sensitive data
- Use SQLCMD variables for config
- Consult security team if unsure

Cannot merge until resolved."
```

---

## Complete Review Checklist

Use this for every PR:

### Pre-Review (30 seconds)
- [ ] PR has description explaining what/why
- [ ] Build status is green
- [ ] Title is descriptive

### Automated Checks (1 min)
- [ ] Build passes
- [ ] No large unexpected script changes

### Schema Review (5 min)
- [ ] Naming conventions followed (singular, PascalCase)
- [ ] Data types appropriate (NVARCHAR for text, DATETIME2)
- [ ] NULL vs NOT NULL correct
- [ ] NOT NULL has DEFAULT if existing table has data
- [ ] Primary keys present and correct

### Index Review (2 min)
- [ ] All FK columns have supporting indexes
- [ ] Index naming convention followed
- [ ] No obvious duplicate indexes

### Script Review (2 min)
- [ ] Pre/post deployment scripts idempotent
- [ ] Reference data uses MERGE
- [ ] Large operations use batching

### Refactoring Review (1 min)
- [ ] Renames use refactorlog (not manual)
- [ ] Refactorlog committed with changes

### Breaking Changes (2 min)
- [ ] Breaking changes identified and documented
- [ ] Impact on OutSystems apps documented
- [ ] Deployment plan clear

### Business Logic (1 min)
- [ ] Makes sense for feature
- [ ] Audit columns included (team standard)
- [ ] Field lengths appropriate

### Security (30 seconds)
- [ ] No sensitive data in scripts
- [ ] No SQL injection vectors

---

## Approval Decision Tree

```
After reviewing:
│
├─ All checks pass?
│  └─ Approve with: "LGTM! ✅ [optional praise/suggestions]"
│
├─ Minor issues (suggestions)?
│  └─ Approve with comments: "Approved with suggestions: [list]"
│
├─ Must-fix issues (no blocking bugs)?
│  └─ Request changes: "Please address: [list items]"
│
└─ Critical issues (data loss, security)?
   └─ Block: "Cannot approve due to: [critical issue]. Please [fix]."
```

### Comment Templates

**Approval (all good)**:
```
LGTM! ✅

Reviewed:
- Naming conventions ✅
- Data types ✅
- FK indexes ✅
- Scripts idempotent ✅

[Optional: "Nice work on X" or "Consider Y for future"]
```

**Request Changes**:
```
Requesting changes:

1. [Issue 1 with explanation]
   Suggested fix: [specific code/approach]

2. [Issue 2]
   Suggested fix: ...

Please update and re-request review. Happy to pair if helpful!
```

**Blocking (critical issue)**:
```
❌ Cannot approve - critical issue:

[Explain data loss / security concern / etc.]

This must be resolved before merging. Please:
1. [Specific action]
2. [Specific action]

Tag me when ready for re-review.
```

---

## Time-Saving Tips

### For Frequent Reviewers

**Tip 1: Create review checklist file**
```
Keep a local checklist.txt you copy/paste and fill in
Faster than remembering everything
```

**Tip 2: Save common comments as snippets**
```
"Please use NVARCHAR for text (Unicode support)"
"Add index on FK column: [example code]"
"Use MERGE for idempotency: [example]"
```

**Tip 3: Use GitHub saved replies**
```
GitHub Settings → Saved Replies
Add your common review comments
Insert with dropdown in PR comments
```

**Tip 4: Batch reviews**
```
Set aside 2-3 specific times per day for PR reviews
More efficient than context switching constantly
Aim for: < 2 hour response time during business hours
```

---

## When to Pair Review

**Schedule pairing session if**:
- Developer is new to team (learning)
- Complex refactoring (high risk)
- Multiple reviewers disagree (resolve together)
- Repeated issues in PRs (teach patterns)

**Benefits of pairing**:
- Faster resolution
- Better learning
- Builds trust
- Prevents repeated issues

---

## Metrics to Track

**Your review quality**:
- Average time to first review
- % of PRs with post-merge issues
- Developer feedback on review helpfulness

**Team health**:
- PR throughput (merges per day)
- Time from PR to merge
- Build failure rate
- Post-merge hotfixes needed

---

**Final Note**: The goal is to prevent issues, not to be a gatekeeper. Use reviews to teach and improve, not just to find problems.

---

## Summary: Week 1 Playbooks Complete

You now have:
1. ✅ **Add Column** - Most common operation
2. ✅ **Add Table** - New entities
3. ✅ **Add Index** - Performance optimization
4. ✅ **Post-Deployment Scripts** - Reference data & migrations
5. ✅ **Troubleshooting** - Get unstuck fast
6. ✅ **Code Review** - Consistent, thorough PR reviews

**These enable Week 1 goals**:
- Reduce dev lead bottleneck
- Handle common scenarios independently
- Maintain quality standards
- Keep team moving forward

**Next**: Month 1 optimization playbooks (merge conflicts, refactoring, performance deep-dives)
