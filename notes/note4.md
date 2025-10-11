# Month 1 Optimization Playbooks
## Advanced Scenarios & Complex Operations

---

## 📘 Playbook 7: Resolving Merge Conflicts in Database Projects

**Time**: 15-45 minutes (depends on conflict complexity)  
**Difficulty**: Intermediate-Advanced  
**When to use**: Multiple developers changed same database objects

---

### Understanding Database Merge Conflicts

**Why they happen**:
- Developer A adds PhoneNumber column to Customers
- Developer B adds DateOfBirth column to Customers
- Both branches modify Customers.sql
- Git can't auto-merge = conflict

**Types of conflicts**:
1. **Simple additive** - Both added different things (easy to merge)
2. **Overlapping changes** - Both changed same line (moderate)
3. **Refactorlog conflicts** - Both renamed objects (complex)
4. **Contradictory changes** - Incompatible changes (requires discussion)

---

### Conflict Type 1: Simple Additive Conflicts (Easy)

**Scenario**: Two developers added different columns to same table

**Git shows**:
```sql
CREATE TABLE [dbo].[Customers] (
    [CustomerId] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [FirstName] NVARCHAR(50) NOT NULL,
    [LastName] NVARCHAR(50) NOT NULL,
    [Email] NVARCHAR(100) NOT NULL,
< < < < < < HEAD (your branch)
    [PhoneNumber] VARCHAR(20) NULL,
= = = = = =
    [DateOfBirth] DATE NULL,
> > > > > > main (their branch)
    [CreatedDate] DATETIME2 NOT NULL DEFAULT GETDATE()
);
```

#### Resolution Steps (5 minutes)

**Step 1: Understand both changes**
- Your branch: Added PhoneNumber
- Their branch: Added DateOfBirth
- Both are valid, keep both!

**Step 2: Merge manually**
```sql
CREATE TABLE [dbo].[Customers] (
    [CustomerId] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [FirstName] NVARCHAR(50) NOT NULL,
    [LastName] NVARCHAR(50) NOT NULL,
    [Email] NVARCHAR(100) NOT NULL,
    [PhoneNumber] VARCHAR(20) NULL,      -- ← Your change
    [DateOfBirth] DATE NULL,             -- ← Their change
    [CreatedDate] DATETIME2 NOT NULL DEFAULT GETDATE()
);
```

**Step 3: Remove conflict markers**
Delete these lines:
- `< < < < < < HEAD`
- `= = = = = =`
- `> > > > > > main`

> ℹ️ Actual Git conflict markers appear without spaces (`<<<<<<<`, `=======`, `>>>>>>>`). Spaces are shown here to avoid confusing tooling.

**Step 4: Verify syntax**
- Check commas (each column except last needs comma)
- Check parentheses match
- Check GO statements if needed

**Step 5: Build to verify**
```
Ctrl+Shift+B (Build Solution)
Check: Build succeeded
```

**Step 6: Commit the merge**
```
Git will create merge commit automatically
Message already filled: "Merge branch 'main' into feature/add-phone"
Click: Commit Merge
```

---

### Conflict Type 2: Overlapping Column Order (Moderate)

**Scenario**: Both branches added column in same position

**Git shows**:
```sql
CREATE TABLE [dbo].[Customers] (
    [CustomerId] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [FirstName] NVARCHAR(50) NOT NULL,
    [LastName] NVARCHAR(50) NOT NULL,
< < < < < < HEAD
    [Email] NVARCHAR(100) NOT NULL,
    [PhoneNumber] VARCHAR(20) NULL,
= = = = = =
    [Email] NVARCHAR(100) NOT NULL,
    [MiddleName] NVARCHAR(50) NULL,
> > > > > > main
    [CreatedDate] DATETIME2 NOT NULL DEFAULT GETDATE()
);
```

#### Resolution (10 minutes)

**Decision**: What order makes sense?

**Option 1: Logical grouping**
```sql
-- Group name fields together
[FirstName] NVARCHAR(50) NOT NULL,
[MiddleName] NVARCHAR(50) NULL,      -- ← Their change (goes with names)
[LastName] NVARCHAR(50) NOT NULL,
[Email] NVARCHAR(100) NOT NULL,
[PhoneNumber] VARCHAR(20) NULL,      -- ← Your change (goes with contact info)
```

**Option 2: Chronological (when added)**
```sql
-- Order doesn't matter for function, just pick one
[FirstName] NVARCHAR(50) NOT NULL,
[LastName] NVARCHAR(50) NOT NULL,
[Email] NVARCHAR(100) NOT NULL,
[PhoneNumber] VARCHAR(20) NULL,      -- ← Your change first (arbitrary)
[MiddleName] NVARCHAR(50) NULL,      -- ← Their change second
```

**Key point**: Column order doesn't affect functionality, just readability

**After merging**:
```
1. Build project
2. Generate deploy script (review ALTER TABLE)
3. Verify both columns will be added
4. Commit merge
```

---

### Conflict Type 3: Same Column, Different Definitions (Complex)

**Scenario**: Both branches added same column with different types/constraints

**Git shows**:
```sql
< < < < < < HEAD
    [PhoneNumber] VARCHAR(20) NULL,
= = = = = =
    [PhoneNumber] NVARCHAR(50) NOT NULL,
> > > > > > main
```

#### Resolution (15-20 minutes)

**This requires discussion!**

**Step 1: Understand the difference**
```
Your branch:  VARCHAR(20)   NULL
Their branch: NVARCHAR(50)  NOT NULL

Questions:
- Why VARCHAR vs NVARCHAR? (ASCII vs Unicode)
- Why length 20 vs 50? (US only vs international?)
- Why NULL vs NOT NULL? (Optional vs required?)
```

**Step 2: Talk to the other developer**
```
Slack them:
"Hey, we both added PhoneNumber column with different definitions.
I did VARCHAR(20) NULL, you did NVARCHAR(50) NOT NULL.
Can we sync on which is correct? I think VARCHAR(20) is enough
for US phone numbers, but if we support international, NVARCHAR(50) makes sense."
```

**Step 3: Decide together**
```
Likely decision (international app):
[PhoneNumber] NVARCHAR(50) NULL  -- Unicode for +44, NULL for optional

Or (US only):
[PhoneNumber] VARCHAR(20) NULL   -- ASCII sufficient, NULL for optional
```

**Step 4: Merge with agreed definition**

**Step 5: Inform dev lead**
```
Comment on PR:
"Resolved merge conflict with @developer-b on PhoneNumber column.
Agreed on NVARCHAR(50) NULL to support international formats."
```

---

### Conflict Type 4: Refactorlog Conflicts (Most Complex)

**Scenario**: Both branches renamed objects

**Git shows conflict in `.refactorlog`**:
```xml
<?xml version="1.0" encoding="utf-8"?>
<Operations Version="1.0">
< < < < < < HEAD
  <Operation Name="Rename Refactor" Key="abc-123-def" ChangeDateTime="01/15/2025 10:30:00">
    <Property Name="ElementName" Value="[dbo].[Products]" />
    <Property Name="ElementType" Value="SqlTable" />
    <Property Name="NewName" Value="[Product]" />
  </Operation>
= = = = = =
  <Operation Name="Rename Refactor" Key="xyz-789-ghi" ChangeDateTime="01/15/2025 11:00:00">
    <Property Name="ElementName" Value="[dbo].[OrderStatus]" />
    <Property Name="ElementType" Value="SqlTable" />
    <Property Name="NewName" Value="[OrderState]" />
  </Operation>
> > > > > > main
</Operations>
```

#### Resolution (20-30 minutes)

**⚠️ CRITICAL**: Don't delete refactorlog operations!

**Step 1: Keep both operations**
```xml
<?xml version="1.0" encoding="utf-8"?>
<Operations Version="1.0">
  <!-- Operation from your branch -->
  <Operation Name="Rename Refactor" Key="abc-123-def" ChangeDateTime="01/15/2025 10:30:00">
    <Property Name="ElementName" Value="[dbo].[Products]" />
    <Property Name="ElementType" Value="SqlTable" />
    <Property Name="NewName" Value="[Product]" />
  </Operation>
  
  <!-- Operation from their branch -->
  <Operation Name="Rename Refactor" Key="xyz-789-ghi" ChangeDateTime="01/15/2025 11:00:00">
    <Property Name="ElementName" Value="[dbo].[OrderStatus]" />
    <Property Name="ElementType" Value="SqlTable" />
    <Property Name="NewName" Value="[OrderState]" />
  </Operation>
</Operations>
```

**Step 2: Verify both renames in SQL files**

Check that both renamed objects are correct:
```
1. Your rename: Products → Product (table file named Product.sql)
2. Their rename: OrderStatus → OrderState (table file named OrderState.sql)
```

**Step 3: Build and test deployment script**
```
Build project
Generate deploy script
Expected to see:
- sp_rename '[Products]', '[Product]'
- sp_rename '[OrderStatus]', '[OrderState]'
- NOT: DROP and CREATE (that would lose data!)
```

**Step 4: Test deploy to Dev**
```
Deploy to Dev database
Verify:
1. Product table exists (renamed from Products)
2. OrderState table exists (renamed from OrderStatus)
3. No data loss
```

**Why this is complex**:
- Deleting refactorlog operation = data loss during deployment
- Order can matter if renames are related
- Must verify both renames deploy correctly

---

### Conflict Type 5: Post-Deployment Script Conflicts

**Scenario**: Both branches modified post-deployment script

**Git shows**:
```sql
MERGE INTO [dbo].[OrderStatus] AS Target
USING (VALUES
    (1, 'Pending'),
    (2, 'Processing'),
< < < < < < HEAD
    (3, 'Shipped'),
    (4, 'Delivered')
= = = = = =
    (3, 'InTransit'),
    (4, 'Completed'),
    (5, 'Cancelled')
> > > > > > main
) AS Source ([StatusId], [StatusName])
```

#### Resolution (10-15 minutes)

**Step 1: Understand business logic**
```
Your branch: Added 'Shipped', 'Delivered'
Their branch: Added 'InTransit', 'Completed', 'Cancelled'

Question: Are these the same statuses with different names?
- Shipped = InTransit? (yes, probably)
- Delivered = Completed? (yes, probably)
```

**Step 2: Align with business stakeholders**
```
Ask product owner or dev lead:
"What's the correct status terminology?"

Result: Let's say they choose:
- InTransit (official terminology)
- Delivered (more user-friendly than Completed)
- Cancelled (new status, keep it)
```

**Step 3: Merge with agreed values**
```sql
MERGE INTO [dbo].[OrderStatus] AS Target
USING (VALUES
    (1, 'Pending'),
    (2, 'Processing'),
    (3, 'InTransit'),      -- ← Agreed terminology
    (4, 'Delivered'),      -- ← Keep from your branch
    (5, 'Cancelled')       -- ← Keep from their branch
) AS Source ([StatusId], [StatusName])
```

**Step 4: Update any references**
```
Search project for hardcoded strings:
- 'Shipped' → change to 'InTransit'
- 'Completed' → change to 'Delivered'
```

---

### Advanced: Three-Way Conflicts

**Scenario**: Three developers all modified same file

**Rare but happens in high-churn environments**

#### Strategy (30-45 minutes)

**Step 1: Identify all changes**
```
Use git log or GitHub to see all branches:
- Branch A: Added PhoneNumber
- Branch B: Added DateOfBirth  
- Branch C: Added MiddleName
```

**Step 2: Coordinate merge**
```
Options:
1. Merge one at a time (A, then B, then C)
2. One person merges all at once
3. Create new "consolidation" branch
```

**Step 3: Recommended approach**
```
1. Merge A into main
2. Merge B into main (resolve A+B conflicts)
3. Merge C into main (resolve A+B+C conflicts)
   ← This person has the hardest job!
```

**Step 4: Test thoroughly**
```
After triple merge:
1. Build must pass
2. Deploy to Dev
3. Verify ALL three changes present
4. Test OutSystems if applicable
```

---

### Tools to Help with Conflicts

#### Visual Studio Merge Tool

**How to use**:
```
1. When conflict detected, Visual Studio shows merge editor
2. Three panes:
   - Left: Your changes (HEAD)
   - Middle: Base (common ancestor)
   - Right: Their changes (incoming)
3. Click checkboxes to include changes
4. Edit bottom pane for custom merge
5. Click "Accept Merge"
```

**Benefits**:
- Visual representation
- Click to include/exclude
- Syntax highlighting

#### Command Line (Advanced)

**Check conflict status**:
```bash
git status
# Shows conflicted files in red

git diff --name-only --diff-filter=U
# Lists only conflicted files
```

**Resolve and continue**:
```bash
# After manually fixing conflicts:
git add Customers.sql
git add MyDatabase.refactorlog

# Complete the merge:
git commit
# Or if rebasing:
git rebase --continue
```

**Abort if too complex**:
```bash
# Give up on merge, go back to before conflict:
git merge --abort

# Or if rebasing:
git rebase --abort

# Start fresh
```

---

### Prevention Strategies

#### Strategy 1: Communicate Before Merging

```
Before big changes:
1. Post in #database-dev: "About to refactor Customers table"
2. Check if anyone else working on it
3. Coordinate merge timing
```

#### Strategy 2: Merge Main Frequently

```
In your feature branch:
git checkout feature/my-change
git fetch origin
git merge origin/main  # Pull main into your branch often

Why: Small frequent conflicts easier than one huge conflict
```

#### Strategy 3: Keep PRs Small

```
Instead of:
- One PR with 10 table changes ← Hard to merge

Do:
- 10 PRs with 1 table each ← Easy to merge
```

#### Strategy 4: Claim Files

```
Team practice:
"I'm working on Customers.sql today, avoid if possible"
Post in Slack before starting

Or use GitHub issue assignments
```

---

### When to Ask for Help

**Handle yourself** ✅:
- Simple additive conflicts (both added different things)
- Post-deployment script conflicts (reference data)
- Column order conflicts

**Ask dev lead** 🟡:
- Refactorlog conflicts (data loss risk)
- Contradictory changes (incompatible)
- Same column different definitions (needs agreement)
- Can't understand conflict after 20 minutes

**Escalate** 🔴:
- Multiple refactorlog conflicts
- Production deployment needed urgently
- Conflicts in 10+ files
- Data loss already occurred

---

### Post-Merge Checklist

After resolving any conflict:
- [ ] Build passes (Ctrl+Shift+B = green)
- [ ] Generated deploy script reviewed
- [ ] Both/all changes present in merged file
- [ ] No conflict markers left (`<<<<`, `====`, `>>>>`)
- [ ] Deployed to Dev and tested
- [ ] OutSystems refreshed if needed
- [ ] Commit message mentions conflict resolution
- [ ] Dev lead aware if complex merge

---

## 📘 Playbook 8: Table Refactoring (Complex Changes)

**Time**: 1-4 hours (depends on table size and data)  
**Difficulty**: Advanced  
**When to use**: Major schema changes to existing tables with data

---

### What is Table Refactoring?

**Definition**: Changing table structure in a way that requires careful data handling

**Examples**:
- Split column (FullName → FirstName + LastName)
- Merge columns (FirstName + LastName → FullName)
- Normalize (move columns to separate table)
- Denormalize (combine related tables)
- Change data type (requires data conversion)
- Introduce lookup table (replace VARCHAR codes with FK to table)

**Why it's complex**:
- Existing data must be preserved or migrated
- Cannot just DROP and CREATE (loses data)
- May require multiple deployment steps
- Often breaking change for OutSystems

---

### Refactoring Pattern 1: Split Column

**Scenario**: FullName column needs to become FirstName + LastName

**Challenge**: Parse existing data correctly

#### Planning Phase (30 minutes)

**Step 1: Analyze existing data**
```sql
-- Connect to Dev database in SSMS
-- Check current data format
SELECT TOP 100 FullName 
FROM Customers
ORDER BY NEWID();  -- Random sample

-- Look for patterns:
-- "John Smith" ← Space separated (most common)
-- "Smith, John" ← Comma separated
-- "John" ← Single name (no space)
-- "Mary Jane Smith" ← Multiple spaces (which is last name?)
-- "" ← Empty (NULL?)
```

**Step 2: Design parsing logic**
```sql
-- Decision rules:
-- If space exists: Split on first space
--    - Before space = FirstName
--    - After space = LastName
-- If no space: 
--    - Entire string = FirstName
--    - LastName = '' (empty)
-- If NULL:
--    - FirstName = ''
--    - LastName = ''
```

**Step 3: Test parsing logic**
```sql
-- Test with sample data
SELECT 
    FullName,
    CASE 
        WHEN FullName IS NULL THEN ''
        WHEN CHARINDEX(' ', FullName) = 0 THEN FullName
        ELSE LEFT(FullName, CHARINDEX(' ', FullName) - 1)
    END AS FirstName,
    CASE
        WHEN FullName IS NULL THEN ''
        WHEN CHARINDEX(' ', FullName) = 0 THEN ''
        ELSE SUBSTRING(FullName, CHARINDEX(' ', FullName) + 1, LEN(FullName))
    END AS LastName
FROM Customers
WHERE FullName IS NOT NULL;

-- Verify results make sense
-- Check edge cases (single names, empty, etc.)
```

#### Implementation Phase 1: Add New Columns (15 minutes)

**Step 1: Update table definition in project**
```sql
CREATE TABLE [dbo].[Customers] (
    [CustomerId] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [FullName] NVARCHAR(100) NULL,  -- ← Keep old column for now
    [FirstName] NVARCHAR(50) NULL,  -- ← New column
    [LastName] NVARCHAR(50) NULL,   -- ← New column
    [Email] NVARCHAR(100) NOT NULL
);
```

**Step 2: Deploy to Dev**
```
ALTER TABLE Customers
ADD [FirstName] NVARCHAR(50) NULL,
    [LastName] NVARCHAR(50) NULL;
```

#### Implementation Phase 2: Migrate Data (30 minutes)

**Create post-deployment migration script**:

**Script.PostDeployment.sql**:
```sql
-- Migration 001: Split FullName into FirstName/LastName
IF NOT EXISTS (SELECT 1 FROM SchemaVersion WHERE VersionNumber = 1)
BEGIN
    PRINT 'Running Migration 001: Split FullName';
    
    -- Update in batches to avoid long locks
    DECLARE @BatchSize INT = 1000;
    DECLARE @RowsAffected INT = @BatchSize;
    
    WHILE @RowsAffected = @BatchSize
    BEGIN
        UPDATE TOP (@BatchSize) Customers
        SET 
            [FirstName] = CASE 
                WHEN FullName IS NULL THEN ''
                WHEN CHARINDEX(' ', FullName) = 0 THEN FullName
                ELSE LEFT(FullName, CHARINDEX(' ', FullName) - 1)
            END,
            [LastName] = CASE
                WHEN FullName IS NULL THEN ''
                WHEN CHARINDEX(' ', FullName) = 0 THEN ''
                ELSE SUBSTRING(FullName, CHARINDEX(' ', FullName) + 1, LEN(FullName))
            END
        WHERE [FirstName] IS NULL;  -- Only process unmigrated rows
        
        SET @RowsAffected = @@ROWCOUNT;
        PRINT 'Migrated ' + CAST(@RowsAffected AS VARCHAR) + ' rows';
        
        WAITFOR DELAY '00:00:01';  -- 1 second pause between batches
    END
    
    INSERT INTO SchemaVersion (VersionNumber, Description)
    VALUES (1, 'Split FullName into FirstName and LastName');
    
    PRINT 'Migration 001 completed.';
END
GO
```

**Deploy and verify**:
```sql
-- After deployment, check results:
SELECT FullName, FirstName, LastName
FROM Customers
WHERE FullName IS NOT NULL
ORDER BY NEWID();  -- Random sample

-- Verify:
-- ✓ All rows processed (FirstName not NULL)
-- ✓ Parsing looks correct
-- ✓ Edge cases handled (single names, etc.)
```

#### Implementation Phase 3: Update OutSystems (1 hour)

**Step 1: Update entity in Integration Studio**
```
1. Open Integration Studio
2. Open database extension
3. Refresh Customers table
4. New attributes appear: FirstName, LastName
5. Old attribute remains: FullName
6. Publish extension
```

**Step 2: Update all consuming apps**
```
For each OutSystems app using Customers:

1. Open Service Studio
2. Find screens/aggregates using FullName
3. Change to use FirstName + LastName
   
   Example:
   Old: Customer.FullName
   New: Customer.FirstName + " " + Customer.LastName
   
4. Test thoroughly
5. Publish app
```

**Step 3: Coordination**
```
This takes time! Coordinate:
- List all apps using Customers.FullName
- Assign to developers
- Track completion
- Only proceed to Phase 4 when ALL apps updated
```

#### Implementation Phase 4: Remove Old Column (30 minutes)

**⚠️ Only after all apps updated!**

**Step 1: Verify no apps use FullName**
```
Check OutSystems:
1. Service Center → Environment Health
2. Search for "FullName" references
3. Should be zero (all updated to FirstName/LastName)
```

**Step 2: Remove from table definition**
```sql
CREATE TABLE [dbo].[Customers] (
    [CustomerId] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    -- [FullName] NVARCHAR(100) NULL,  ← Remove this line
    [FirstName] NVARCHAR(50) NULL,
    [LastName] NVARCHAR(50) NULL,
    [Email] NVARCHAR(100) NOT NULL
);
```

**Step 3: Deploy (drops column)**
```
DACPAC generates:
ALTER TABLE Customers
DROP COLUMN [FullName];
```

**Step 4: Refresh OutSystems entity**
```
1. Integration Studio → Refresh Customers table
2. FullName attribute removed
3. Publish extension
4. No apps should break (already updated)
```

---

### Refactoring Pattern 2: Introduce Lookup Table

**Scenario**: OrderStatus as VARCHAR → separate OrderStatus table with FK

**Challenge**: Convert string values to IDs

#### Before:
```sql
CREATE TABLE Orders (
    OrderId INT PRIMARY KEY,
    OrderStatus VARCHAR(20) NOT NULL  -- 'Pending', 'Shipped', etc.
);
```

#### After:
```sql
CREATE TABLE OrderStatus (
    OrderStatusId INT PRIMARY KEY,
    StatusName NVARCHAR(50) NOT NULL
);

CREATE TABLE Orders (
    OrderId INT PRIMARY KEY,
    OrderStatusId INT NOT NULL,  -- FK to OrderStatus
    CONSTRAINT FK_Orders_OrderStatus
        FOREIGN KEY (OrderStatusId)
        REFERENCES OrderStatus(OrderStatusId)
);
```

#### Implementation (2-3 hours)

**Phase 1: Create lookup table**
```sql
-- Add OrderStatus table to project
CREATE TABLE [dbo].[OrderStatus] (
    [OrderStatusId] INT NOT NULL PRIMARY KEY,
    [StatusName] NVARCHAR(50) NOT NULL
);
GO

-- Populate in post-deployment
MERGE INTO OrderStatus AS Target
USING (VALUES
    (1, 'Pending'),
    (2, 'Processing'),
    (3, 'Shipped'),
    (4, 'Delivered')
) AS Source (OrderStatusId, StatusName)
ON Target.OrderStatusId = Source.OrderStatusId
WHEN NOT MATCHED THEN INSERT VALUES (Source.OrderStatusId, Source.StatusName);
```

**Phase 2: Add new FK column (keep old)**
```sql
CREATE TABLE [dbo].[Orders] (
    [OrderId] INT PRIMARY KEY,
    [OrderStatus] VARCHAR(20) NULL,      -- ← Keep old, make nullable
    [OrderStatusId] INT NULL,            -- ← New FK, nullable for now
    CONSTRAINT FK_Orders_OrderStatus
        FOREIGN KEY (OrderStatusId)
        REFERENCES OrderStatus(OrderStatusId)
);
GO

CREATE INDEX IX_Orders_OrderStatusId
    ON Orders(OrderStatusId);  -- ← Don't forget index!
```

**Phase 3: Migrate data**
```sql
-- Post-deployment migration
UPDATE o
SET o.OrderStatusId = os.OrderStatusId
FROM Orders o
INNER JOIN OrderStatus os 
    ON o.OrderStatus = os.StatusName
WHERE o.OrderStatusId IS NULL;

-- Verify all migrated:
SELECT COUNT(*) 
FROM Orders 
WHERE OrderStatusId IS NULL;  -- Should be 0
```

**Phase 4: Update OutSystems apps**
```
All apps using OrderStatus:
1. Change from Text attribute to OrderStatus entity reference
2. Update UI (dropdowns now use OrderStatus list)
3. Test thoroughly
```

**Phase 5: Make FK NOT NULL, remove old column**
```sql
CREATE TABLE [dbo].[Orders] (
    [OrderId] INT PRIMARY KEY,
    -- [OrderStatus] VARCHAR(20) NULL,   ← Remove
    [OrderStatusId] INT NOT NULL,        -- ← NOT NULL now
    CONSTRAINT FK_Orders_OrderStatus
        FOREIGN KEY (OrderStatusId)
        REFERENCES OrderStatus(OrderStatusId)
);
```

---

### Refactoring Pattern 3: Table Split (Vertical Partitioning)

**Scenario**: Customers table too wide, split rarely-used columns

**Before**:
```sql
CREATE TABLE Customers (
    CustomerId INT PRIMARY KEY,
    -- Frequently accessed:
    FirstName NVARCHAR(50),
    LastName NVARCHAR(50),
    Email NVARCHAR(100),
    -- Rarely accessed (causing bloat):
    Biography NVARCHAR(MAX),        -- Large
    ProfilePicture VARBINARY(MAX),  -- Large
    Preferences NVARCHAR(MAX)       -- Large
);
```

**After**:
```sql
-- Frequently accessed (stays lean)
CREATE TABLE Customers (
    CustomerId INT PRIMARY KEY,
    FirstName NVARCHAR(50),
    LastName NVARCHAR(50),
    Email NVARCHAR(100)
);

-- Rarely accessed (separate table)
CREATE TABLE CustomerDetails (
    CustomerId INT PRIMARY KEY,
    Biography NVARCHAR(MAX),
    ProfilePicture VARBINARY(MAX),
    Preferences NVARCHAR(MAX),
    CONSTRAINT FK_CustomerDetails_Customers
        FOREIGN KEY (CustomerId)
        REFERENCES Customers(CustomerId)
);
```

**Benefits**:
- Main table smaller = faster queries
- Details only loaded when needed
- Better memory usage

**Implementation**: Similar to column split pattern

---

### Common Refactoring Mistakes

#### ❌ Mistake 1: Drop Column Without Migration

```sql
-- Don't just drop the column!
ALTER TABLE Customers
DROP COLUMN FullName;  -- ❌ Data lost!

-- Do: Migrate data first, then drop
```

#### ❌ Mistake 2: Change Data Type Without Conversion

```sql
-- Changing VARCHAR(100) → VARCHAR(50)
-- What if existing data is longer than 50 chars?

-- Must check first:
SELECT MAX(LEN(ColumnName)) FROM TableName;
-- If result > 50, need to handle truncation
```

#### ❌ Mistake 3: Forget OutSystems Updates

```
Sequence matters:
1. ❌ Drop column from database
2. ❌ Apps break (still reference old column)

Do instead:
1. ✅ Update all apps first
2. ✅ Then drop column from database
```

---

### Refactoring Checklist

Before starting refactoring:
- [ ] Analyze existing data patterns
- [ ] Test parsing/conversion logic
- [ ] Plan multi-phase approach
- [ ] Identify all affected OutSystems apps
- [ ] Get stakeholder approval for downtime/risk
- [ ] Have rollback plan

During refactoring:
- [ ] Phase 1: Add new structure (keep old)
- [ ] Phase 2: Migrate data (script in post-deployment)
- [ ] Phase 3: Update all OutSystems apps
- [ ] Phase 4: Remove old structure
- [ ] Test at each phase
- [ ] Document in SchemaVersion table

After refactoring:
- [ ] All data migrated correctly
- [ ] All apps using new structure
- [ ] Old structure removed
- [ ] Performance improved (if that was goal)
- [ ] Document what was done and why

---

## 📘 Playbook 9: Recovering from Failed Deployments

**Time**: 15 minutes - 2 hours  
**Difficulty**: Advanced  
**When to use**: Deployment failed and needs recovery

---

### Immediate Response (First 5 Minutes)

#### Step 1: Assess Impact

**Questions to answer quickly**:
```
Environment: Dev / Test / Prod?
Status: Deployment failed / Partial success / Corrupted?
Impact: Who's affected? Production down?
Data: Any data loss?
```

**Priority by environment**:
- **Prod**: 🔴 Highest priority, immediate action
- **Test**: 🟡 Moderate priority, affects QA
- **Dev**: 🟢 Lower priority, developer inconvenience

#### Step 2: Communicate

**If Production**:
```
1. Post in #database-emergency immediately
2. Page @db-on-call
3. Call on-call phone number
4. Don't wait - call AND message

Message template:
"🚨 PROD DEPLOYMENT FAILED
Deploying: [DACPAC name]
Error: [Copy error message]
Impact: [What's broken]
Action: [What you're doing]
Need: [Help needed]"
```

**If Test/Dev**:
```
Post in #database-help:
"Deployment failed to [ENV]
Deploying: [What]
Error: [Summary]
Need help debugging"
```

---

### Recovery Strategy Decision Tree

```
Deployment failed?
│
├─ In transaction?
│  ├─ YES → Automatic rollback ✅
│  │  └─ Database unchanged, safe to retry
│  └─ NO → Partial changes applied ⚠️
│     └─ Need manual recovery
│
└─ What failed?
   ├─ Pre-deployment script → Database unchanged, retry
   ├─ Schema change → May be partial, investigate
   └─ Post-deployment script → Schema applied, data not
```

---

### Recovery Scenario 1: Timeout During Large Operation

**Error**:
```
Timeout expired. The timeout period elapsed prior to 
completion of the operation.
```

**What happened**:
- Started deployment
- Took too long (default 60 seconds)
- SQL Server killed the operation
- **CRITICAL**: Check if transaction rolled back

#### Recovery Steps (15-30 minutes)

**Step 1: Check transaction status**
```sql
-- Connect to database in SSMS
SELECT * FROM sys.dm_tran_active_transactions;

-- If no active transactions:
-- ✅ Rollback occurred, database consistent

-- If active transaction:
-- ⚠️ Hanging transaction, needs manual rollback
```

**Step 2: If hung transaction, kill it**
```sql
-- Find the session:
SELECT session_id, login_name, status, command
FROM sys.dm_exec_sessions
WHERE is_user_process = 1
  AND status = 'sleeping'
  AND open_transaction_count > 0;

-- Kill the session (replace XXX with session_id):
KILL XXX;

-- Verify rollback completed:
SELECT * FROM sys.dm_tran_active_transactions;
-- Should be empty
```

**Step 3: Retry with longer timeout**
```powershell
SqlPackage.exe /Action:Publish `
    /SourceFile:"MyDatabase.dacpac" `
    /TargetServerName:"Server" `
    /p:CommandTimeout=3600  # 60 minutes instead of 60 seconds
```

**Step 4: If still times out, use pre-script**
```sql
-- Move the large operation to separate script
-- Run manually before deployment:
-- large-data-migration.sql

-- Then deploy DACPAC without that operation
```

---

### Recovery Scenario 2: Blocked by Data Loss

**Error**:
```
Deployment blocked due to possible data loss.
Column [PhoneNumber] on table [Customers] will be dropped.
```

**What happened**:
- Tried to drop column with data
- `/p:BlockOnPossibleDataLoss=True` (default) stopped it
- ✅ Good news: Database unchanged

#### Recovery Steps (30-60 minutes)

**Step 1: Decide if data loss is acceptable**

**If Dev environment**:
```
Data loss OK in Dev:
SqlPackage /p:BlockOnPossibleDataLoss=False

Redeploy with this flag
```

**If Test/Prod environment**:
```
Data loss NOT OK!
Need data migration:
1. Write pre-deployment script to backup data
2. Let DACPAC drop column
3. Write post-deployment script to migrate
4. Or: Don't drop column, redesign approach
```

**Step 2: Implement data migration**
```sql
-- Pre-deployment script:
-- Backup column data
SELECT CustomerId, PhoneNumber
INTO #PhoneBackup
FROM Customers;

-- (DACPAC drops PhoneNumber)

-- Post-deployment script:
-- Restore to new structure (if applicable)
-- Or just preserve in audit table
```

---

### Recovery Scenario 3: Post-Deployment Script Failed

**Error**:
```
Execution Timeout Expired. The timeout period elapsed
prior to completion or the server is not responding.
(SqlPackage.Deploy.DacDeployException)
```

**What happened**:
- Schema changes deployed successfully ✅
- Post-deployment script started
- Script timed out ❌
- **Database state**: New schema, old/partial data

#### Recovery Steps (30-90 minutes)

**Step 1: Verify schema deployed**
```sql
-- Check if new columns exist:
SELECT COLUMN_NAME 
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_NAME = 'Customers';

-- If new column exists: Schema deployed ✅
```

**Step 2: Check data migration status**
```sql
-- If using SchemaVersion tracking:
SELECT * FROM SchemaVersion;

-- Check if migration completed:
SELECT COUNT(*) FROM Customers WHERE NewColumn IS NULL;
-- If > 0: Migration incomplete
```

**Step 3: Manually run post-deployment script**
```sql
-- Extract post-deployment script from DACPAC
-- Or from Script.PostDeployment.sql in project

-- Run manually in SSMS with batching:
DECLARE @BatchSize INT = 1000;
-- ... (your migration logic)
```

**Step 4: Mark as complete**
```sql
-- If using SchemaVersion:
INSERT INTO SchemaVersion (VersionNumber, Description)
VALUES (1, 'Manually completed after deployment timeout');
```

**Step 5: Verify**
```sql
-- Check all data migrated:
SELECT COUNT(*) FROM Customers WHERE NewColumn IS NULL;
-- Should be 0

-- Test application functionality
```

---

### Recovery Scenario 4: Wrong Database Deployed To

**Error**: Human error - deployed Dev DACPAC to Test, or Test to Prod

**What happened**:
- Deployed successfully
- But wrong database version now in environment
- May have missing features or extra test data

#### Recovery Steps (1-2 hours)

**Step 1: STOP immediately**
```
Don't make it worse!
- Don't deploy again
- Don't run manual scripts
- Assess damage first
```

**Step 2: Assess damage**
```sql
-- What schema version is deployed?
SELECT * FROM SchemaVersion ORDER BY VersionNumber DESC;

-- Compare to expected version
-- Check for test data in production:
SELECT COUNT(*) FROM Customers WHERE Email LIKE '%@test.com';
```

**Step 3: Restore from backup**

**If Prod (most critical)**:
```
1. Take immediate backup of current state (for forensics)
2. Restore from last known good backup
3. Replay transactions since backup (if possible)
4. Or: Accept data loss since last backup
```

**If Test (moderate critical)**:
```
1. Restore from backup
2. Or: Redeploy correct DACPAC
3. Refresh test data
```

**Step 4: Deploy correct DACPAC**
```powershell
# Double-check you're using correct file!
SqlPackage.exe /Action:Publish `
    /SourceFile:"MyDatabase.PROD.v1.2.3.dacpac" `  # Verify version!
    /TargetServerName:"ProdServer" `               # Verify server!
    /TargetDatabaseName:"MyDatabase"               # Verify database!
```

**Step 5: Verify**
```sql
-- Schema version correct?
SELECT * FROM SchemaVersion;

-- No test data?
SELECT COUNT(*) FROM Customers WHERE Email LIKE '%@test.com';
-- Should be 0 in Prod

-- Application works?
-- Manual smoke test
```

---

### Recovery Scenario 5: Deployment Succeeded but App Broken

**Issue**: Deployment reported success, but OutSystems app broken

**What happened**:
- Database deployed ✅
- But forgot to refresh Integration Studio ❌
- Or: Breaking change not coordinated

#### Recovery Steps (15-30 minutes)

**Step 1: Check OutSystems error**
```
Service Center → Error Logs
Look for: "Invalid attribute" or "Column not found"

This confirms: Schema mismatch between DB and OutSystems
```

**Step 2: Refresh Integration Studio**
```
1. Open Integration Studio
2. Open database extension
3. Refresh affected tables
4. Publish extension
5. Service Studio → Refresh Server Data
```

**Step 3: If breaking change**
```
Must update app code:
1. Open Service Studio
2. Fix references to changed columns/tables
3. Test locally
4. Publish app
```

**Step 4: If emergency production fix needed**
```
Option A: Rollback database (if possible)
Option B: Emergency hotfix to OutSystems app
Option C: Temporarily add back dropped column

Choose based on risk and time available
```

---

### Prevention Checklist

**Before deploying**:
- [ ] Reviewed deployment script (Generate Script first)
- [ ] Tested in Dev successfully
- [ ] Deployment plan documented
- [ ] Rollback plan documented
- [ ] Backup taken (Prod/Test)
- [ ] Stakeholders notified
- [ ] Maintenance window scheduled
- [ ] On-call available during deployment

**During deployment**:
- [ ] Monitor deployment logs
- [ ] Watch for warnings/errors
- [ ] Don't walk away from production deployment
- [ ] Have SSMS connected to verify

**After deployment**:
- [ ] Verify schema changes applied
- [ ] Verify data migration completed
- [ ] Test application functionality
- [ ] Check OutSystems entity synced
- [ ] Monitor for errors (first 30 minutes critical)

---

### Rollback Procedures

#### Rollback Method 1: Previous DACPAC

**If you have it**:
```powershell
# Deploy previous version
SqlPackage.exe /Action:Publish `
    /SourceFile:"MyDatabase.PREVIOUS.dacpac" `
    /TargetServerName:"Server" `
    /TargetDatabaseName:"MyDatabase"

# DACPAC will reverse changes
```

**Limitation**: Can't rollback if data loss occurred (dropped column)

#### Rollback Method 2: Restore from Backup

**Most reliable**:
```sql
-- Restore database from backup taken before deployment
RESTORE DATABASE MyDatabase
FROM DISK = 'C:\Backups\MyDatabase_PreDeployment.bak'
WITH REPLACE;

-- Verify:
SELECT * FROM SchemaVersion;
-- Should show previous version
```

**Limitation**: Loses any data changes since backup

#### Rollback Method 3: Manual Reverse Script

**Write script to undo**:
```sql
-- If deployment added columns:
ALTER TABLE Customers DROP COLUMN PhoneNumber;

-- If deployment dropped columns (and you backed up):
ALTER TABLE Customers ADD PhoneNumber VARCHAR(20);
UPDATE Customers SET PhoneNumber = ... -- from backup

-- If deployment changed data:
-- Restore from backup table
```

**Limitation**: Manual, error-prone, time-consuming

---

### Escalation Contacts

**By severity**:

**🔴 P1 - Production down**:
- Call on-call phone immediately
- Post in #database-emergency
- Page @db-on-call
- Loop in VP Engineering

**🟡 P2 - Production degraded**:
- Post in #database-emergency
- Tag @db-on-call
- Dev lead coordinates

**🟢 P3 - Test/Dev issue**:
- Post in #database-help
- Tag @dev-leads
- Work during business hours

---

**Next: Continue with Performance Quick Checks and Learning Path?**
