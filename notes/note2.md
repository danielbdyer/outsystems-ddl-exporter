# Week 1 Complete Playbooks
## Standalone Task Guides for Common Database Operations

---

## 📘 Playbook 1: Add Column to Existing Table

**Time**: 10-15 minutes  
**Difficulty**: Entry Level  
**When to use**: Adding new optional or required fields to existing tables

---

### Quick Decision Tree

```
Need to add a column?
│
├─ Does table have data already?
│  ├─ YES → Can it be NULL?
│  │  ├─ YES → Add as NULL (simplest)
│  │  └─ NO → Must provide DEFAULT value
│  └─ NO (new table) → Can be NOT NULL without DEFAULT
│
└─ Will OutSystems use this column?
   ├─ YES → Must refresh Integration Studio after deployment
   └─ NO → Database-only change, no OutSystems refresh
```

---

### Scenario 1: Add Nullable Column (Simplest)

**Example**: Add optional PhoneNumber to Customers table

#### Step 1: Edit Table Definition (2 min)

**Open table file**:
```
Solution Explorer → Tables folder → Customers.sql
Double-click to open
```

**Current table**:
```sql
CREATE TABLE [dbo].[Customers] (
    [CustomerId] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [FirstName] NVARCHAR(50) NOT NULL,
    [LastName] NVARCHAR(50) NOT NULL,
    [Email] NVARCHAR(100) NOT NULL
);
```

**Add new column** (before closing `);`):
```sql
CREATE TABLE [dbo].[Customers] (
    [CustomerId] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [FirstName] NVARCHAR(50) NOT NULL,
    [LastName] NVARCHAR(50) NOT NULL,
    [Email] NVARCHAR(100) NOT NULL,
    [PhoneNumber] VARCHAR(20) NULL  -- ← New line
);
```

**Data Type Considerations**:
- **VARCHAR(20)** - US/international phone numbers (no Unicode needed)
- **NULL** - Phone number is optional (can be blank)
- **Max length 20** - Accommodates formatted numbers: +1 (555) 123-4567

#### Step 2: Build Project (1 min)

```
Press: Ctrl+Shift+B
Or: Build menu → Build Solution
```

**Check Output window** (bottom of Visual Studio):
```
✅ Success: "Build succeeded. 0 failed."
❌ Fail: Red errors listed
```

**Common build errors**:
```
Error: "Missing comma"
Fix: Add comma after previous column

Error: "Unexpected token )"
Fix: Check for extra/missing commas

Error: "SQL71501: Unresolved reference"
Fix: Check spelling of table/column names
```

#### Step 3: Preview Deployment (2 min)

**Generate script to see what will happen**:
```
Right-click database project → Publish
Click: "Generate Script" (don't publish yet)
```

**Expected script output**:
```sql
ALTER TABLE [dbo].[Customers]
ADD [PhoneNumber] VARCHAR(20) NULL;
```

**Verify**:
- ✅ Shows ALTER TABLE (not DROP/CREATE)
- ✅ Column name correct
- ✅ Data type correct
- ✅ NULL/NOT NULL correct

#### Step 4: Commit to Git (2 min)

```
Team Explorer → Changes tab
```

**You should see**:
- Modified: `Customers.sql` (shows M)

**Write commit message**:
```
Add PhoneNumber column to Customers table

- Added optional VARCHAR(20) column
- For feature: Customer contact management (PROJ-123)
- Nullable field, no data migration needed
```

**Commit**:
```
Click: "Commit All"
```

#### Step 5: Push and Create PR (3 min)

```
Team Explorer → Sync → Push
```

**GitHub/Azure DevOps**:
```
1. Navigate to repository
2. You'll see banner: "Your branch has changes"
3. Click: "Create Pull Request"
4. Title: "Add PhoneNumber to Customers"
5. Description:
   - Why: [Business reason]
   - Breaking change: No
   - OutSystems impact: Need to refresh Integration Studio
   - Testing: Verified build passes
6. Assign reviewer: Your dev lead
7. Click: "Create"
```

#### Step 6: After Approval - Deploy to Dev (3 min)

**Option A: From Visual Studio** (local deployment):
```
Right-click project → Publish
Target: Your Dev database connection
Click: "Publish"
Wait for: "Publish succeeded"
```

**Option B: Via SqlPackage** (command line):
```powershell
SqlPackage.exe /Action:Publish `
    /SourceFile:"bin\Debug\MyDatabase.dacpac" `
    /TargetServerName:"DevServer" `
    /TargetDatabaseName:"MyDatabase"
```

#### Step 7: Refresh OutSystems (5 min)

**⚠️ CRITICAL STEP - Don't skip!**

1. **Open Integration Studio**
2. **Open your database extension** (e.g., `MyApp_Database.xif`)
3. **Find Customers entity** in left panel
4. **Right-click → Refresh Table**
5. **Verify** you see:
   ```
   ✅ Green: PhoneNumber (new attribute)
   Type: Text
   Length: 20
   Is Mandatory: No
   ```
6. **Click Apply**
7. **File → Publish** (F5)
8. **Wait for**: "Extension published successfully"

**Test in Service Studio**:
```
1. Open app that uses Customers
2. Manage Dependencies → Refresh
3. Verify PhoneNumber attribute appears
4. Add to a form or aggregate
5. Publish app
6. Test in browser
```

---

### Scenario 2: Add NOT NULL Column with DEFAULT

**Example**: Add IsActive flag to Customers table (existing table with 1M rows)

**Challenge**: Can't add NOT NULL to existing table without DEFAULT

**Solution**:
```sql
CREATE TABLE [dbo].[Customers] (
    [CustomerId] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [FirstName] NVARCHAR(50) NOT NULL,
    [LastName] NVARCHAR(50) NOT NULL,
    [Email] NVARCHAR(100) NOT NULL,
    [PhoneNumber] VARCHAR(20) NULL,
    [IsActive] BIT NOT NULL DEFAULT 1  -- ← NOT NULL with DEFAULT
);
```

**What happens on deployment**:
```sql
-- DACPAC generates:
ALTER TABLE [dbo].[Customers]
ADD [IsActive] BIT NOT NULL DEFAULT 1;

-- All existing rows get IsActive = 1 automatically
-- New rows get IsActive = 1 if not specified
```

**Alternative approach** (if default value varies):
```sql
-- First deployment: Add as NULL
[IsActive] BIT NULL

-- Post-deployment script: Set values based on logic
UPDATE [dbo].[Customers]
SET [IsActive] = 1
WHERE [IsActive] IS NULL;

-- Second deployment: Change to NOT NULL
[IsActive] BIT NOT NULL
```

---

### Scenario 3: Add Column with Foreign Key

**Example**: Add CategoryId to Products table

**Step 1: Add column**:
```sql
CREATE TABLE [dbo].[Products] (
    [ProductId] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [ProductName] NVARCHAR(100) NOT NULL,
    [Price] DECIMAL(10,2) NOT NULL,
    [CategoryId] INT NOT NULL  -- ← New FK column
);
```

**Step 2: Add foreign key constraint** (after table definition):
```sql
GO  -- Separator required!

ALTER TABLE [dbo].[Products]
ADD CONSTRAINT [FK_Products_Categories]
    FOREIGN KEY ([CategoryId])
    REFERENCES [dbo].[Categories]([CategoryId]);
GO
```

**Step 3: ⚠️ CRITICAL - Add supporting index**:
```sql
-- Every FK column needs an index!
CREATE NONCLUSTERED INDEX [IX_Products_CategoryId]
    ON [dbo].[Products]([CategoryId]);
GO
```

**Why index is required**:
- Queries filter by CategoryId (WHERE CategoryId = @id)
- OutSystems generates JOIN queries using this FK
- Without index = table scan = slow performance
- Updates to Categories table lock Products table without index

**Full example**:
```sql
CREATE TABLE [dbo].[Products] (
    [ProductId] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [ProductName] NVARCHAR(100) NOT NULL,
    [Price] DECIMAL(10,2) NOT NULL,
    [CategoryId] INT NOT NULL
);
GO

ALTER TABLE [dbo].[Products]
ADD CONSTRAINT [FK_Products_Categories]
    FOREIGN KEY ([CategoryId])
    REFERENCES [dbo].[Categories]([CategoryId]);
GO

CREATE NONCLUSTERED INDEX [IX_Products_CategoryId]
    ON [dbo].[Products]([CategoryId]);
GO
```

---

### Common Mistakes (Anti-Patterns)

#### ❌ Anti-Pattern 1: VARCHAR for Names

**Don't**:
```sql
[FirstName] VARCHAR(50)  -- ❌ ASCII only
```

**Problem**: Can't store international names (José, François, 李明)

**Do**:
```sql
[FirstName] NVARCHAR(50)  -- ✅ Unicode support
```

**Rule**: User-entered text = NVARCHAR. System codes = VARCHAR.

---

#### ❌ Anti-Pattern 2: NOT NULL Without DEFAULT on Existing Table

**Don't**:
```sql
-- Table has 1M rows
[IsActive] BIT NOT NULL  -- ❌ Deployment will fail!
```

**Problem**: Existing rows have NULL, can't become NOT NULL

**Do**:
```sql
-- Option 1: Add DEFAULT
[IsActive] BIT NOT NULL DEFAULT 1

-- Option 2: Add as NULL first, migrate data, then NOT NULL later
```

---

#### ❌ Anti-Pattern 3: Foreign Key Without Index

**Don't**:
```sql
-- Add FK but forget index
ALTER TABLE Products
ADD CONSTRAINT FK_Products_Categories
    FOREIGN KEY (CategoryId)
    REFERENCES Categories(CategoryId);
-- ❌ Missing: CREATE INDEX IX_Products_CategoryId
```

**Problem**: Slow queries, locking issues

**Do**: ALWAYS add index on FK column

---

### Data Type Quick Reference

| Type | When to Use | Example |
|------|-------------|---------|
| **NVARCHAR(n)** | User-entered text, Unicode | Names, addresses, descriptions |
| **VARCHAR(n)** | System codes, ASCII-only | Status codes, email addresses |
| **INT** | IDs, counts, quantities | CustomerId, OrderQuantity |
| **BIGINT** | High-volume tables, large numbers | OrderId (millions expected) |
| **DECIMAL(p,s)** | Money, precise decimals | Price DECIMAL(10,2) |
| **BIT** | True/false flags | IsActive, IsDeleted |
| **DATETIME2** | Dates and times | CreatedDate, OrderDate |
| **DATE** | Date only, no time | BirthDate, StartDate |

**Common sizes**:
- Names: NVARCHAR(50) or NVARCHAR(100)
- Emails: NVARCHAR(100) or VARCHAR(100)
- Descriptions: NVARCHAR(500) or NVARCHAR(2000)
- Large text: NVARCHAR(MAX) (avoid if possible - performance)

---

### Troubleshooting

**Problem**: Build fails with "SQL71501: Unresolved reference to [CategoryId]"

**Solution**: CategoryId column doesn't exist, or:
- Check spelling
- Check if Categories table exists in project
- If external table, add database reference

---

**Problem**: Deployment blocked: "Possible data loss"

**Solution**: You're adding NOT NULL to table with data
- Add DEFAULT value, OR
- Add as NULL first, migrate data in post-deployment script, then NOT NULL

---

**Problem**: OutSystems doesn't see new column after deployment

**Solution**: You forgot to refresh Integration Studio
- Complete Step 7 above

---

**Problem**: PR rejected: "Missing index on FK column"

**Solution**: Add index (see Scenario 3 above)

---

### Checklist

Before committing:
- [ ] Build passes (Ctrl+Shift+B = green)
- [ ] Data type appropriate (NVARCHAR for text?)
- [ ] NULL vs NOT NULL correct
- [ ] DEFAULT provided if NOT NULL on existing table
- [ ] Foreign key has supporting index
- [ ] Generated script reviewed (looks correct?)
- [ ] Commit message describes change

After deployment:
- [ ] OutSystems Integration Studio refreshed
- [ ] Extension published
- [ ] Service Studio app updated
- [ ] Tested in browser

---

**Time tracking**:
- Total time: ~15 minutes for first time
- After practice: ~5 minutes

---

## 📘 Playbook 2: Add New Table

**Time**: 15-20 minutes  
**Difficulty**: Entry Level  
**When to use**: Creating new entities for new features

---

### Quick Decision Tree

```
Need to add a new table?
│
├─ Standalone table? → Create with primary key only
│
├─ Related to existing table? → Add foreign keys + indexes
│
└─ Will OutSystems use it?
   ├─ YES → Must import entity in Integration Studio
   └─ NO → Database-only, skip Integration Studio
```

---

### Scenario 1: Simple Standalone Table

**Example**: Create OrderStatus lookup table

#### Step 1: Create Table File (2 min)

```
Solution Explorer → Tables folder
Right-click → Add → Table
Name: OrderStatus.sql
```

**Use template** (copy this):
```sql
CREATE TABLE [dbo].[OrderStatus] (
    [OrderStatusId] INT NOT NULL PRIMARY KEY,
    [StatusName] NVARCHAR(50) NOT NULL,
    [DisplayOrder] INT NOT NULL DEFAULT 0,
    [IsActive] BIT NOT NULL DEFAULT 1,
    
    -- Audit columns (team standard)
    [CreatedDate] DATETIME2 NOT NULL DEFAULT GETDATE(),
    [CreatedBy] NVARCHAR(100) NOT NULL DEFAULT SUSER_NAME(),
    [ModifiedDate] DATETIME2 NULL,
    [ModifiedBy] NVARCHAR(100) NULL
);
GO

-- Always add this index for audit queries
CREATE NONCLUSTERED INDEX [IX_OrderStatus_CreatedDate]
    ON [dbo].[OrderStatus]([CreatedDate]);
GO
```

**Key elements**:
- **Primary Key**: `INT NOT NULL PRIMARY KEY`
- **Naming**: Singular (OrderStatus, not OrderStatuses)
- **Audit columns**: CreatedDate, CreatedBy, ModifiedDate, ModifiedBy
- **GO separators**: Between CREATE TABLE and CREATE INDEX

#### Step 2: Populate Reference Data (3 min)

**Add post-deployment script**:

Open: `Script.PostDeployment.sql`

Add this code:
```sql
-- Populate OrderStatus lookup table
PRINT 'Populating OrderStatus reference data...';

MERGE INTO [dbo].[OrderStatus] AS Target
USING (VALUES
    (1, 'Pending', 1, 1),
    (2, 'Processing', 2, 1),
    (3, 'Shipped', 3, 1),
    (4, 'Delivered', 4, 1),
    (5, 'Cancelled', 5, 0)
) AS Source ([OrderStatusId], [StatusName], [DisplayOrder], [IsActive])
ON Target.[OrderStatusId] = Source.[OrderStatusId]
WHEN NOT MATCHED BY TARGET THEN
    INSERT ([OrderStatusId], [StatusName], [DisplayOrder], [IsActive])
    VALUES (Source.[OrderStatusId], Source.[StatusName], Source.[DisplayOrder], Source.[IsActive])
WHEN MATCHED AND (
    Target.[StatusName] <> Source.[StatusName] OR
    Target.[DisplayOrder] <> Source.[DisplayOrder] OR
    Target.[IsActive] <> Source.[IsActive]
) THEN
    UPDATE SET 
        [StatusName] = Source.[StatusName],
        [DisplayOrder] = Source.[DisplayOrder],
        [IsActive] = Source.[IsActive],
        [ModifiedDate] = GETDATE(),
        [ModifiedBy] = SUSER_NAME();

PRINT 'OrderStatus reference data updated.';
GO
```

**Why MERGE?**:
- Runs every deployment (post-deployment scripts always run)
- Idempotent (safe to run multiple times)
- Won't duplicate data
- Updates changed values
- Doesn't touch data not in script (preserves manual additions)

#### Step 3: Build, Commit, PR (same as Playbook 1)

---

### Scenario 2: Table with Foreign Keys

**Example**: Create Orders table related to Customers

```sql
CREATE TABLE [dbo].[Orders] (
    [OrderId] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [CustomerId] INT NOT NULL,  -- FK to Customers
    [OrderStatusId] INT NOT NULL,  -- FK to OrderStatus
    [OrderDate] DATETIME2 NOT NULL DEFAULT GETDATE(),
    [TotalAmount] DECIMAL(10,2) NOT NULL,
    
    -- Audit columns
    [CreatedDate] DATETIME2 NOT NULL DEFAULT GETDATE(),
    [CreatedBy] NVARCHAR(100) NOT NULL DEFAULT SUSER_NAME(),
    [ModifiedDate] DATETIME2 NULL,
    [ModifiedBy] NVARCHAR(100) NULL
);
GO

-- Foreign key constraints
ALTER TABLE [dbo].[Orders]
ADD CONSTRAINT [FK_Orders_Customers]
    FOREIGN KEY ([CustomerId])
    REFERENCES [dbo].[Customers]([CustomerId]);
GO

ALTER TABLE [dbo].[Orders]
ADD CONSTRAINT [FK_Orders_OrderStatus]
    FOREIGN KEY ([OrderStatusId])
    REFERENCES [dbo].[OrderStatus]([OrderStatusId]);
GO

-- ⚠️ CRITICAL: Index every FK column!
CREATE NONCLUSTERED INDEX [IX_Orders_CustomerId]
    ON [dbo].[Orders]([CustomerId]);
GO

CREATE NONCLUSTERED INDEX [IX_Orders_OrderStatusId]
    ON [dbo].[Orders]([OrderStatusId]);
GO

-- Additional useful indexes
CREATE NONCLUSTERED INDEX [IX_Orders_OrderDate]
    ON [dbo].[Orders]([OrderDate] DESC);  -- DESC for "recent orders" queries
GO

-- Covering index for common query: Orders by Customer with Status
CREATE NONCLUSTERED INDEX [IX_Orders_CustomerId_OrderDate]
    ON [dbo].[Orders]([CustomerId], [OrderDate] DESC)
    INCLUDE ([OrderStatusId], [TotalAmount]);  -- INCLUDE = covering
GO
```

**Index strategy explained**:

1. **IX_Orders_CustomerId**: Required for FK, used in JOINs
2. **IX_Orders_OrderStatusId**: Required for FK, used in filters
3. **IX_Orders_OrderDate**: For "recent orders" queries (DESC = newest first)
4. **IX_Orders_CustomerId_OrderDate**: Covering index for common query pattern

**Covering index** (with INCLUDE):
```sql
-- Covers this query completely (no table lookup needed):
SELECT OrderId, OrderDate, OrderStatusId, TotalAmount
FROM Orders
WHERE CustomerId = 123
ORDER BY OrderDate DESC;

-- Index has: CustomerId (key) + OrderDate (key) + OrderStatusId, TotalAmount (included)
-- Query needs: CustomerId (WHERE) + OrderDate (ORDER BY) + those columns (SELECT)
-- Result: Index seek, no key lookup = fastest possible
```

---

### Scenario 3: Junction Table (Many-to-Many)

**Example**: Products can have multiple Categories, Categories can have multiple Products

```sql
CREATE TABLE [dbo].[ProductCategories] (
    -- Composite primary key (both columns together)
    [ProductId] INT NOT NULL,
    [CategoryId] INT NOT NULL,
    
    -- Optional additional data
    [DisplayOrder] INT NOT NULL DEFAULT 0,
    [IsPrimary] BIT NOT NULL DEFAULT 0,
    
    -- Audit columns
    [CreatedDate] DATETIME2 NOT NULL DEFAULT GETDATE(),
    [CreatedBy] NVARCHAR(100) NOT NULL DEFAULT SUSER_NAME(),
    
    -- Composite PK
    CONSTRAINT [PK_ProductCategories] 
        PRIMARY KEY CLUSTERED ([ProductId], [CategoryId])
);
GO

-- Foreign keys
ALTER TABLE [dbo].[ProductCategories]
ADD CONSTRAINT [FK_ProductCategories_Products]
    FOREIGN KEY ([ProductId])
    REFERENCES [dbo].[Products]([ProductId])
    ON DELETE CASCADE;  -- Delete product → delete its category links
GO

ALTER TABLE [dbo].[ProductCategories]
ADD CONSTRAINT [FK_ProductCategories_Categories]
    FOREIGN KEY ([CategoryId])
    REFERENCES [dbo].[Categories]([CategoryId])
    ON DELETE CASCADE;  -- Delete category → delete its product links
GO

-- Additional index for reverse lookup (Categories → Products)
-- Forward lookup (Products → Categories) covered by PK
CREATE NONCLUSTERED INDEX [IX_ProductCategories_CategoryId]
    ON [dbo].[ProductCategories]([CategoryId]);
GO
```

**Key points**:
- **Composite PK**: Both columns together form unique key
- **ON DELETE CASCADE**: Automatic cleanup (use carefully!)
- **One index needed**: PK covers ProductId lookups, add index for CategoryId lookups

---

### Integration Studio: Import New Table (5 min)

**After deploying to Dev**:

1. **Open Integration Studio**
2. **Open your database extension**
3. **Right-click Entities folder** → Add Entity from Database
4. **Select your new table** from list (e.g., OrderStatus, Orders)
5. **Click OK**

**Configure entity** (usually defaults are fine):
```
Entity Name: OrderStatus  ✅ Matches table name
Identifier: OrderStatusId ✅ Auto-detected PK
Delete Rule: Ignore        ✅ Don't delete from OutSystems
Show Tenant Identifier: No ✅ Not multi-tenant
```

**Attributes review**:
```
OrderStatusId: Integer, Is AutoNumber ✅
StatusName: Text (50) ✅
DisplayOrder: Integer ✅
IsActive: Boolean ✅
CreatedDate: DateTime ✅
CreatedBy: Text (100) ✅
ModifiedDate: DateTime ✅
ModifiedBy: Text (100) ✅
```

**Common issues**:
- Attribute name has spaces → Right-click → Properties → Remove spaces
- Wrong data type → Right-click → Properties → Change type
- Missing identifier → Right-click → Set as Identifier

6. **File → Publish** (F5)
7. **Wait for**: "Extension published successfully"

**Use in Service Studio**:
```
1. Open your app
2. Manage Dependencies (Ctrl+Q)
3. Find your extension
4. Check OrderStatus entity
5. Click Apply
6. Now available in aggregates, entities list
```

---

### Table Design Best Practices

#### Required Elements (Team Standard)

Every table should have:

```sql
-- 1. Primary Key (always!)
[TableNameId] INT IDENTITY(1,1) NOT NULL PRIMARY KEY

-- 2. Audit columns (who, when)
[CreatedDate] DATETIME2 NOT NULL DEFAULT GETDATE(),
[CreatedBy] NVARCHAR(100) NOT NULL DEFAULT SUSER_NAME(),
[ModifiedDate] DATETIME2 NULL,
[ModifiedBy] NVARCHAR(100) NULL

-- 3. Soft delete (optional but recommended)
[IsDeleted] BIT NOT NULL DEFAULT 0
```

#### Primary Key Patterns

**Auto-increment INT** (most common):
```sql
[OrderId] INT IDENTITY(1,1) NOT NULL PRIMARY KEY
```
- Pros: Fast, sequential, small size
- Cons: Predictable, exposes volume
- Use: Most tables

**Auto-increment BIGINT** (high volume):
```sql
[LogEntryId] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY
```
- Pros: Handles billions of rows
- Cons: Slightly larger
- Use: Logs, events, high-volume tables

**Natural key** (lookup tables):
```sql
[OrderStatusId] INT NOT NULL PRIMARY KEY  -- No IDENTITY
```
- Pros: Meaningful IDs (1=Pending, 2=Processing)
- Cons: Manual management
- Use: Lookup/reference tables with fixed values

**Composite key** (junction tables):
```sql
CONSTRAINT [PK_ProductCategories] 
    PRIMARY KEY ([ProductId], [CategoryId])
```
- Pros: Enforces uniqueness of combination
- Cons: More complex
- Use: Many-to-many relationship tables

**⚠️ Avoid GUID primary keys unless necessary**:
```sql
[CustomerId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID()
```
- Pros: Globally unique, merge-friendly
- Cons: Non-sequential = index fragmentation, 4x larger than INT
- Use: Only if distributed generation needed

---

### Common Mistakes (Anti-Patterns)

#### ❌ Anti-Pattern 1: No Primary Key

**Don't**:
```sql
CREATE TABLE Orders (
    OrderId INT,  -- ❌ Not a primary key
    CustomerId INT
    -- Missing: PRIMARY KEY constraint
);
```

**Problem**: Duplicates allowed, poor performance, OutSystems issues

**Do**: ALWAYS have primary key

---

#### ❌ Anti-Pattern 2: Plural Table Names

**Don't**:
```sql
CREATE TABLE Customers (  -- ❌ Plural
```

**Do**:
```sql
CREATE TABLE Customer (  -- ✅ Singular
```

**Team standard**: Singular noun table names

---

#### ❌ Anti-Pattern 3: Missing Audit Columns

**Don't**:
```sql
CREATE TABLE Order (
    OrderId INT PRIMARY KEY,
    CustomerId INT
    -- ❌ Missing: CreatedDate, CreatedBy, etc.
);
```

**Problem**: Can't track who created/modified records

**Do**: Always include audit columns (team standard)

---

### Troubleshooting

**Problem**: Build fails "FK_Orders_Customers references non-existent table"

**Solution**: Customers table doesn't exist in project
- Add Customers table first
- Or add database reference if in different database

---

**Problem**: Integration Studio can't find new table

**Solution**: Deployment didn't succeed, or wrong environment
- Verify in SSMS that table exists in target database
- Check Integration Studio connection string points to correct DB

---

**Problem**: Post-deployment script fails "Violation of PRIMARY KEY constraint"

**Solution**: MERGE didn't work as expected, or data already exists
- Check MERGE ON condition matches correctly
- Verify no duplicate StatusIds in VALUES list

---

### Checklist

Before committing:
- [ ] Primary key defined
- [ ] Audit columns included (CreatedDate, etc.)
- [ ] Foreign keys have supporting indexes
- [ ] Table name singular (Order not Orders)
- [ ] Naming conventions followed ([TableName]Id pattern)
- [ ] Build passes
- [ ] Post-deployment script for reference data (if lookup table)
- [ ] MERGE statement is idempotent

After deployment:
- [ ] Verify table exists in SSMS
- [ ] Integration Studio: Import entity
- [ ] Extension published
- [ ] Service Studio: Add dependency
- [ ] Test creating/reading records

---

## 📘 Playbook 3: Add Index for Performance

**Time**: 10-15 minutes  
**Difficulty**: Intermediate  
**When to use**: Query is slow, execution plan shows table scan, missing index suggestion

---

### When Do You Need an Index?

```
Slow query?
│
├─ Check execution plan (Ctrl+M in SSMS)
│  │
│  ├─ See "Table Scan" or "Clustered Index Scan"?
│  │  └─ YES → Probably need index
│  │
│  ├─ See "Index Seek" but "Key Lookup"?
│  │  └─ YES → Need covering index
│  │
│  └─ See "Missing Index" warning?
│     └─ YES → SQL Server suggesting index
│
└─ Frequent query using WHERE/JOIN/ORDER BY?
   └─ YES → Candidate for index
```

---

### Scenario 1: Index for WHERE Clause

**Problem**: Slow query filtering Orders by CustomerId

```sql
-- This query is slow (takes 5 seconds on 10M rows)
SELECT * 
FROM Orders 
WHERE CustomerId = 123;
```

**Execution plan shows**: Table Scan (bad!)

**Solution**: Add index on CustomerId

#### Step 1: Analyze the Query (2 min)

**Run in SSMS with execution plan**:
```sql
-- Enable execution plan
SET STATISTICS TIME ON;
GO

-- Run query
SELECT OrderId, OrderDate, TotalAmount
FROM Orders 
WHERE CustomerId = 123;
GO

-- Check results:
-- "SQL Server Execution Times: CPU time = 1200 ms"
-- Execution plan shows: Table Scan (cost 95%)
```

**Look for**:
- High CPU time (> 100ms for simple query)
- Table Scan or Clustered Index Scan
- High estimated rows vs actual rows
- Warning triangle icon (missing index hint)

#### Step 2: Add the Index (3 min)

**Open table file**: `Orders.sql`

**Add after table definition**:
```sql
CREATE TABLE [dbo].[Orders] (
    [OrderId] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [CustomerId] INT NOT NULL,
    [OrderDate] DATETIME2 NOT NULL,
    [TotalAmount] DECIMAL(10,2) NOT NULL
);
GO

-- Add this index
CREATE NONCLUSTERED INDEX [IX_Orders_CustomerId]
    ON [dbo].[Orders]([CustomerId]);
GO
```

**Naming convention**:
```
IX_TableName_Column1_Column2
IX = Index
TableName = Orders
Columns = CustomerId

Example: IX_Orders_CustomerId
```

#### Step 3: Test the Index (2 min)

**Build and deploy to Dev**

**Re-run query in SSMS**:
```sql
SET STATISTICS TIME ON;
GO

SELECT OrderId, OrderDate, TotalAmount
FROM Orders 
WHERE CustomerId = 123;
GO

-- New results:
-- "SQL Server Execution Times: CPU time = 10 ms"  ← 120x faster!
-- Execution plan shows: Index Seek (cost 5%)  ← Good!
```

**Verify improvement**:
- CPU time reduced dramatically
- Execution plan shows "Index Seek" (not Scan)
- Cost % much lower
- ✅ Success!

#### Step 4: ⚠️ No OutSystems Refresh Needed

**Indexes are invisible to OutSystems**
- Don't change schema
- Don't need Integration Studio refresh
- Just make queries faster

---

### Scenario 2: Covering Index (Best Performance)

**Problem**: Query has Index Seek but also expensive Key Lookup

```sql
-- This query uses CustomerId index but then does Key Lookup
SELECT OrderId, OrderDate, TotalAmount, OrderStatusId
FROM Orders 
WHERE CustomerId = 123
ORDER BY OrderDate DESC;
```

**Execution plan shows**:
1. Index Seek on IX_Orders_CustomerId (good) - 20%
2. Key Lookup (bad) - 60%
3. Nested Loops - 20%

**Key Lookup = extra trip to table to get columns not in index**

**Solution**: Covering index with INCLUDE

```sql
-- Covering index includes all columns the query needs
CREATE NONCLUSTERED INDEX [IX_Orders_CustomerId_OrderDate_Covering]
    ON [dbo].[Orders]([CustomerId], [OrderDate] DESC)
    INCLUDE ([OrderId], [TotalAmount], [OrderStatusId]);
GO
```

**How it works**:
- **Key columns** ([CustomerId], [OrderDate]): Used for filtering and sorting
- **INCLUDE columns**: Extra columns stored in index leaf pages
- Query can be answered entirely from index (no key lookup needed)

**After adding covering index**:
```sql
-- Re-run query
SELECT OrderId, OrderDate, TotalAmount, OrderStatusId
FROM Orders 
WHERE CustomerId = 123
ORDER BY OrderDate DESC;

-- Execution plan shows:
-- 1. Index Seek on IX_Orders_CustomerId_OrderDate_Covering - 100%
-- 2. No Key Lookup! ← Eliminated
-- CPU time: 5 ms (was 50ms before)
```

---

### Scenario 3: Composite Index (Multiple Columns)

**Problem**: Query filters on two columns

```sql
SELECT * 
FROM Orders 
WHERE CustomerId = 123 
  AND OrderStatusId = 2;  -- Only "Processing" orders
```

**Solution**: Multi-column index

```sql
CREATE NONCLUSTERED INDEX [IX_Orders_CustomerId_OrderStatusId]
    ON [dbo].[Orders]([CustomerId], [OrderStatusId]);
GO
```

**Column order matters!**

**Rule**: Most selective column first

```
If query has:
- CustomerId = 123 (matches 100 rows)
- OrderStatusId = 2 (matches 1000 rows)

Then:
✅ [CustomerId], [OrderStatusId]  -- Correct order
❌ [OrderStatusId], [CustomerId]  -- Wrong order

Why: Index can seek on CustomerId (100 rows) then filter OrderStatusId
Wrong way: Index seeks on OrderStatusId (1000 rows) then filters CustomerId
```

**Advanced example**:
```sql
-- Query uses: WHERE CustomerId, AND OrderStatusId, ORDER BY OrderDate
-- Index should have: CustomerId (most selective), OrderStatusId, OrderDate (for ORDER BY)

CREATE NONCLUSTERED INDEX [IX_Orders_CustomerId_OrderStatusId_OrderDate]
    ON [dbo].[Orders]([CustomerId], [OrderStatusId], [OrderDate] DESC);
GO
```

---

### Scenario 4: Filtered Index (Subset of Data)

**Problem**: Queries only care about active/undeleted records

```sql
-- 90% of queries filter on IsDeleted = 0
SELECT * 
FROM Orders 
WHERE IsDeleted = 0 
  AND CustomerId = 123;
```

**Solution**: Filtered index (SQL Server 2008+)

```sql
-- Only index non-deleted orders
CREATE NONCLUSTERED INDEX [IX_Orders_Active_CustomerId]
    ON [dbo].[Orders]([CustomerId])
    WHERE [IsDeleted] = 0;  -- ← Filter condition
GO
```

**Benefits**:
- Smaller index (only includes rows where IsDeleted = 0)
- Faster updates (doesn't update index when deleted rows change)
- More selective (better for common queries)

**When to use**:
- Queries always filter on same condition (IsActive = 1, IsDeleted = 0)
- Subset is small compared to full table (< 10-20% of rows)
- Column is NOT NULL or has small number of distinct values

**Example with dates**:
```sql
-- Only index recent orders (last 90 days)
CREATE NONCLUSTERED INDEX [IX_Orders_Recent_CustomerId]
    ON [dbo].[Orders]([CustomerId], [OrderDate] DESC)
    WHERE [OrderDate] >= DATEADD(DAY, -90, GETDATE());
GO
```

---

### Index Design Guidelines

#### When to Add Index

**✅ Add index when**:
- Column(s) in WHERE clause frequently
- Column(s) in JOIN conditions
- Column(s) in ORDER BY frequently
- Foreign key columns (always!)
- Queries are slow (> 100ms for simple query)
- Execution plan shows table scan

**❌ Don't add index when**:
- Table is very small (< 1000 rows)
- Column has very few distinct values (IsActive with only 0/1)
- Table is write-heavy, read-rarely
- Column is rarely queried

#### Index Types

**Clustered Index** (only 1 per table):
```sql
-- Usually on Primary Key (created automatically)
[OrderId] INT IDENTITY(1,1) NOT NULL PRIMARY KEY CLUSTERED
```
- Defines physical order of table
- Table data IS the index (leaf pages contain actual rows)
- Best on: Primary key, sequential values (INT IDENTITY)

**Non-Clustered Index** (up to 999 per table):
```sql
CREATE NONCLUSTERED INDEX [IX_Orders_CustomerId]
    ON [dbo].[Orders]([CustomerId]);
```
- Separate structure pointing to table rows
- Multiple non-clustered indexes possible
- Best on: WHERE, JOIN, ORDER BY columns

**Covering Index** (non-clustered with INCLUDE):
```sql
CREATE NONCLUSTERED INDEX [IX_Orders_CustomerId_Covering]
    ON [dbo].[Orders]([CustomerId])
    INCLUDE ([OrderDate], [TotalAmount]);
```
- Includes extra columns in leaf pages
- No key lookup needed
- Best for: Frequently-run queries needing specific columns

**Filtered Index** (with WHERE):
```sql
CREATE NONCLUSTERED INDEX [IX_Orders_Active]
    ON [dbo].[Orders]([CustomerId])
    WHERE [IsDeleted] = 0;
```
- Only indexes subset of rows
- Smaller, faster
- Best for: Queries always filtering on same condition

---

### Common Mistakes (Anti-Patterns)

#### ❌ Anti-Pattern 1: Too Many Indexes

**Don't**:
```sql
-- 20 indexes on one table
CREATE INDEX IX1...
CREATE INDEX IX2...
... (18 more)
```

**Problem**: 
- Every INSERT/UPDATE/DELETE updates ALL indexes
- Slow writes
- Wasted storage
- Marginal benefit

**Do**:
- Max 5-10 indexes per table (unless special cases)
- Remove unused indexes (check DMVs)
- Combine similar indexes (consolidate)

---

#### ❌ Anti-Pattern 2: Wide Indexes

**Don't**:
```sql
-- Index with 8 columns
CREATE INDEX [IX_Orders_Everything]
    ON Orders(Col1, Col2, Col3, Col4, Col5, Col6, Col7, Col8);
```

**Problem**:
- Large index = slow
- Unlikely all columns used together
- Hard to maintain

**Do**:
- Max 3-4 key columns usually
- Use INCLUDE for additional columns
- Create multiple focused indexes instead

---

#### ❌ Anti-Pattern 3: Duplicate Indexes

**Don't**:
```sql
-- These are duplicates (IX2 is redundant)
CREATE INDEX IX1 ON Orders(CustomerId, OrderDate);
CREATE INDEX IX2 ON Orders(CustomerId);  -- ❌ Duplicate!
```

**Why**: IX1 can handle queries on (CustomerId) alone

**Do**: Remove IX2, IX1 covers both cases

---

#### ❌ Anti-Pattern 4: Function on Indexed Column

**Don't**:
```sql
-- Index exists on OrderDate but query uses function
SELECT * 
FROM Orders 
WHERE YEAR(OrderDate) = 2024;  -- ❌ Index can't be used!
```

**Problem**: Function on column = index useless (non-SARGable)

**Do**:
```sql
-- Rewrite to avoid function
SELECT * 
FROM Orders 
WHERE OrderDate >= '2024-01-01' 
  AND OrderDate < '2025-01-01';  -- ✅ Index can be used!
```

---

### Checking Index Usage

**Are your indexes being used?**

```sql
-- Find unused indexes (never used, wasting space)
SELECT 
    OBJECT_NAME(i.object_id) AS TableName,
    i.name AS IndexName,
    i.type_desc,
    s.user_seeks,
    s.user_scans,
    s.user_lookups,
    s.user_updates
FROM sys.indexes i
LEFT JOIN sys.dm_db_index_usage_stats s 
    ON i.object_id = s.object_id 
    AND i.index_id = s.index_id
WHERE OBJECTPROPERTY(i.object_id, 'IsUserTable') = 1
  AND i.index_id > 0  -- Exclude heaps
  AND s.user_seeks + s.user_scans + s.user_lookups = 0  -- Never used for reads
ORDER BY s.user_updates DESC;

-- If user_updates > 0 but user_seeks = 0:
-- Index is being maintained on writes but never used for reads = DROP IT
```

**Find missing indexes** (SQL Server suggestions):
```sql
SELECT 
    d.statement AS TableName,
    d.equality_columns,
    d.inequality_columns,
    d.included_columns,
    s.user_seeks,
    s.avg_user_impact,
    'CREATE INDEX IX_' + REPLACE(REPLACE(d.statement, '[', ''), ']', '') + 
        '_' + ISNULL(d.equality_columns, '') AS SuggestedIndexName
FROM sys.dm_db_missing_index_details d
INNER JOIN sys.dm_db_missing_index_stats s 
    ON d.index_handle = s.index_handle
WHERE d.database_id = DB_ID()
ORDER BY s.user_seeks * s.avg_user_impact DESC;
```

---

### Troubleshooting

**Problem**: Added index but query still slow

**Solution**: 
- Check execution plan - is index being used?
- Maybe wrong column order in composite index
- Maybe need covering index (INCLUDE)
- Maybe statistics out of date: `UPDATE STATISTICS Orders;`

---

**Problem**: Build fails "There is already an object named 'IX_Orders_CustomerId'"

**Solution**: Index already exists in database
- Check if someone else already added it
- Or drop old index first: `DROP INDEX IF EXISTS [IX_Orders_CustomerId] ON [Orders];`

---

**Problem**: Index makes queries slower, not faster!

**Solution**: Wrong index for query pattern
- Review execution plan with and without index
- Consider removing index
- Or redesign index (different columns, different order)

---

### Checklist

Before adding index:
- [ ] Query is actually slow (measured with execution plan)
- [ ] Analyzed WHERE/JOIN/ORDER BY columns
- [ ] Checked for existing similar indexes (avoid duplicates)
- [ ] Determined best index type (covering, filtered, etc.)
- [ ] Named index correctly (IX_TableName_Columns)

After adding index:
- [ ] Build passes
- [ ] Deployed to Dev
- [ ] Re-run query with execution plan
- [ ] Verify performance improved
- [ ] Verify Index Seek appears (not Scan)
- [ ] Commit with explanation in commit message

Remember:
- ⚠️ NO OutSystems refresh needed for indexes
- Start simple (single column), optimize later if needed
- Monitor index usage over time
- Remove unused indexes

---

## 📘 Playbook 4: Post-Deployment Scripts for Reference Data

**Time**: 15-20 minutes  
**Difficulty**: Intermediate  
**When to use**: Populating lookup tables, inserting seed data, one-time migrations

---

### What Are Post-Deployment Scripts?

**Definition**: T-SQL scripts that run AFTER schema changes deploy

**Key characteristics**:
- Run EVERY deployment (not just first time)
- Must be idempotent (safe to run multiple times)
- Execute AFTER all CREATE/ALTER statements
- Can access new schema
- Perfect for reference/lookup data

**vs Pre-Deployment**:
- **Pre-deployment**: Runs BEFORE schema changes (for data backup, etc.)
- **Post-deployment**: Runs AFTER schema changes (for data population)

---

### Basic Pattern: Reference Data Population

**Scenario**: OrderStatus lookup table needs values

#### Step 1: Locate Post-Deployment Script (1 min)

```
Solution Explorer → Look for: Script.PostDeployment.sql

If doesn't exist:
Right-click project → Add → Script → Post-Deployment Script
Name: Script.PostDeployment.sql
```

**⚠️ Important**: Only ONE post-deployment script per project
- But can reference other scripts using `:r` (covered later)

#### Step 2: Write MERGE Statement (5 min)

**Add to Script.PostDeployment.sql**:

```sql
/*
Post-Deployment Script
Runs after every deployment
Must be idempotent (safe to re-run)
*/

-- Header comment
PRINT '========================================';
PRINT 'Running Post-Deployment Script';
PRINT 'Database: $(DatabaseName)';
PRINT 'User: ' + SUSER_NAME();
PRINT 'Timestamp: ' + CONVERT(VARCHAR, GETDATE(), 120);
PRINT '========================================';
GO

-- Populate OrderStatus lookup table
PRINT 'Populating OrderStatus reference data...';

MERGE INTO [dbo].[OrderStatus] AS Target
USING (
    VALUES
        (1, 'Pending', 'Order received, awaiting processing', 1, 1),
        (2, 'Processing', 'Order being prepared', 2, 1),
        (3, 'Shipped', 'Order shipped to customer', 3, 1),
        (4, 'Delivered', 'Order delivered successfully', 4, 1),
        (5, 'Cancelled', 'Order cancelled by customer', 5, 0)
) AS Source (
    [OrderStatusId], 
    [StatusName], 
    [Description],
    [DisplayOrder], 
    [IsActive]
)
ON Target.[OrderStatusId] = Source.[OrderStatusId]

-- Row doesn't exist: INSERT it
WHEN NOT MATCHED BY TARGET THEN
    INSERT (
        [OrderStatusId], 
        [StatusName], 
        [Description],
        [DisplayOrder], 
        [IsActive]
    )
    VALUES (
        Source.[OrderStatusId], 
        Source.[StatusName], 
        Source.[Description],
        Source.[DisplayOrder], 
        Source.[IsActive]
    )

-- Row exists but values changed: UPDATE it
WHEN MATCHED AND (
    Target.[StatusName] <> Source.[StatusName] OR
    Target.[Description] <> Source.[Description] OR
    Target.[DisplayOrder] <> Source.[DisplayOrder] OR
    Target.[IsActive] <> Source.[IsActive]
) THEN
    UPDATE SET
        [StatusName] = Source.[StatusName],
        [Description] = Source.[Description],
        [DisplayOrder] = Source.[DisplayOrder],
        [IsActive] = Source.[IsActive],
        [ModifiedDate] = GETDATE(),
        [ModifiedBy] = SUSER_NAME()

-- Row exists in target but not in source: Optionally DELETE it
-- WHEN NOT MATCHED BY SOURCE THEN DELETE
-- ↑ Commented out: Don't auto-delete (manual additions preserved)

OUTPUT $action AS [Action],
       inserted.[OrderStatusId],
       inserted.[StatusName];

PRINT 'OrderStatus reference data updated.';
PRINT '';
GO
```

**MERGE explained**:

1. **Source**: VALUES list (your reference data)
2. **Target**: Database table
3. **ON clause**: How to match (usually by ID)
4. **WHEN NOT MATCHED**: Row missing → INSERT
5. **WHEN MATCHED**: Row exists but changed → UPDATE
6. **OUTPUT**: Shows what happened (logged)

**Why MERGE?**
- ✅ Idempotent (safe to run multiple times)
- ✅ Won't duplicate rows
- ✅ Updates changed values automatically
- ✅ Preserves manually-added data (by omitting WHEN NOT MATCHED BY SOURCE)

---

### Advanced Pattern: Organized Multiple Scripts

**Problem**: One big post-deployment script gets messy

**Solution**: Organize into multiple files, include with `:r`

#### File Structure:

```
DatabaseProject/
├── Script.PostDeployment.sql  ← Main entry point
└── Scripts/
    └── PostDeployment/
        ├── ReferenceData/
        │   ├── _ReferenceData.sql  ← Index file
        │   ├── OrderStatus.sql
        │   ├── ShippingMethod.sql
        │   └── Country.sql
        └── Migrations/
            └── 001_BackfillCustomerPhone.sql
```

**Script.PostDeployment.sql** (main entry):
```sql
/*
Post-Deployment Script - Main Entry Point
This script includes all other post-deployment scripts
*/

PRINT '========================================';
PRINT 'Starting Post-Deployment Scripts';
PRINT '========================================';
GO

-- Include reference data scripts
:r .\Scripts\PostDeployment\ReferenceData\_ReferenceData.sql

-- Include migration scripts
:r .\Scripts\PostDeployment\Migrations\001_BackfillCustomerPhone.sql

PRINT '========================================';
PRINT 'Post-Deployment Scripts Completed';
PRINT '========================================';
GO
```

**_ReferenceData.sql** (index file):
```sql
-- Reference Data Index
-- Includes all reference data population scripts

PRINT 'Loading reference data...';
GO

-- Order matters! Tables without FKs first
:r .\OrderStatus.sql
:r .\ShippingMethod.sql
:r .\Country.sql

PRINT 'Reference data loaded.';
GO
```

**OrderStatus.sql**:
```sql
-- OrderStatus Reference Data
PRINT '  - OrderStatus';

MERGE INTO [dbo].[OrderStatus] AS Target
USING (VALUES
    (1, 'Pending', 1, 1),
    (2, 'Processing', 2, 1),
    (3, 'Shipped', 3, 1),
    (4, 'Delivered', 4, 1),
    (5, 'Cancelled', 5, 0)
) AS Source ([OrderStatusId], [StatusName], [DisplayOrder], [IsActive])
ON Target.[OrderStatusId] = Source.[OrderStatusId]
WHEN NOT MATCHED THEN
    INSERT VALUES (Source.[OrderStatusId], Source.[StatusName], Source.[DisplayOrder], Source.[IsActive])
WHEN MATCHED AND (
    Target.[StatusName] <> Source.[StatusName] OR
    Target.[DisplayOrder] <> Source.[DisplayOrder] OR
    Target.[IsActive] <> Source.[IsActive]
) THEN
    UPDATE SET 
        [StatusName] = Source.[StatusName],
        [DisplayOrder] = Source.[DisplayOrder],
        [IsActive] = Source.[IsActive];
GO
```

**Benefits**:
- ✅ Each table in separate file (easier to find/edit)
- ✅ Clear organization
- ✅ Team can work on different files without conflicts
- ✅ Main script stays clean

**⚠️ Important**: All referenced files must have **Build Action = None**
```
Right-click file → Properties → Build Action: None
```

---

### Environment-Specific Scripts (SQLCMD Variables)

**Problem**: Different data for Dev vs Test vs Prod

**Solution**: Use SQLCMD variables

**Database.sqlcmdvars** (in project root):
```xml
<SqlCmdVariable Include="EnvironmentName">
  <Value>$(EnvironmentName)</Value>
</SqlCmdVariable>
```

**In post-deployment script**:
```sql
PRINT 'Deploying to environment: $(EnvironmentName)';
GO

-- Dev-only: Sample data
IF '$(EnvironmentName)' = 'DEV'
BEGIN
    PRINT 'Loading DEV sample data...';
    
    -- Insert sample customers for testing
    IF NOT EXISTS (SELECT 1 FROM Customers WHERE CustomerId = 1)
    BEGIN
        INSERT INTO Customers (FirstName, LastName, Email)
        VALUES 
            ('Test', 'Customer1', 'test1@example.com'),
            ('Test', 'Customer2', 'test2@example.com'),
            ('Test', 'Customer3', 'test3@example.com');
    END
END
GO

-- Test-only: QA test data
IF '$(EnvironmentName)' = 'TEST'
BEGIN
    PRINT 'Loading TEST data...';
    -- QA-specific setup
END
GO

-- Prod: No sample data, only reference data
IF '$(EnvironmentName)' = 'PROD'
BEGIN
    PRINT 'Production deployment - reference data only';
    -- No test data in production!
END
GO
```

**Deploy with variable**:
```powershell
SqlPackage.exe /Action:Publish `
    /SourceFile:"MyDatabase.dacpac" `
    /TargetServerName:"DevServer" `
    /Variables:EnvironmentName=DEV
```

---

### One-Time Migrations (Run Once Pattern)

**Problem**: Need to backfill data, but only once (not every deployment)

**Solution 1: Version Tracking Table**

**Create tracking table** (in project):
```sql
CREATE TABLE [dbo].[SchemaVersion] (
    [VersionNumber] INT NOT NULL PRIMARY KEY,
    [Description] NVARCHAR(200) NOT NULL,
    [AppliedDate] DATETIME2 NOT NULL DEFAULT GETDATE(),
    [AppliedBy] NVARCHAR(100) NOT NULL DEFAULT SUSER_NAME()
);
GO
```

**In post-deployment script**:
```sql
-- Migration 001: Backfill Customer Phone Numbers
IF NOT EXISTS (SELECT 1 FROM SchemaVersion WHERE VersionNumber = 1)
BEGIN
    PRINT 'Running Migration 001: Backfill Customer Phone Numbers';
    
    -- Do the one-time work
    UPDATE Customers
    SET PhoneNumber = '555-0000'
    WHERE PhoneNumber IS NULL;
    
    -- Mark as complete
    INSERT INTO SchemaVersion (VersionNumber, Description)
    VALUES (1, 'Backfilled PhoneNumber with default for existing customers');
    
    PRINT 'Migration 001 completed.';
END
ELSE
BEGIN
    PRINT 'Migration 001: Already applied, skipping.';
END
GO

-- Migration 002: Another one-time change
IF NOT EXISTS (SELECT 1 FROM SchemaVersion WHERE VersionNumber = 2)
BEGIN
    PRINT 'Running Migration 002: ...';
    -- Work here
    INSERT INTO SchemaVersion (VersionNumber, Description)
    VALUES (2, 'Description of migration 002');
END
GO
```

**Benefits**:
- Runs once per database
- Tracked in database
- Can see history
- Safe for multiple deployments

**Solution 2: Date-Based Guard**

```sql
-- Only run if data doesn't exist (simple check)
IF NOT EXISTS (SELECT 1 FROM Customers WHERE PhoneNumber IS NOT NULL)
BEGIN
    PRINT 'Backfilling phone numbers...';
    UPDATE Customers SET PhoneNumber = '555-0000';
END
GO
```

---

### Large Data Operations

**Problem**: Migration affects millions of rows, takes minutes

**Solution**: Batch processing

```sql
-- Bad: One massive UPDATE (locks table for minutes)
UPDATE Orders 
SET ProcessedFlag = 1;  -- ❌ 10M rows, 5 minute lock

-- Good: Batch processing
DECLARE @BatchSize INT = 10000;
DECLARE @RowsAffected INT = @BatchSize;

PRINT 'Starting batch processing...';

WHILE @RowsAffected = @BatchSize
BEGIN
    UPDATE TOP (@BatchSize) Orders
    SET ProcessedFlag = 1
    WHERE ProcessedFlag = 0;
    
    SET @RowsAffected = @@ROWCOUNT;
    
    PRINT 'Processed ' + CAST(@RowsAffected AS VARCHAR) + ' rows';
    
    -- Small delay to let other queries through
    WAITFOR DELAY '00:00:01';  -- 1 second pause
END

PRINT 'Batch processing completed.';
GO
```

**Benefits**:
- Smaller transactions (less locking)
- Other queries can run between batches
- Progress visible in logs
- Can be stopped/resumed

---

### Common Mistakes (Anti-Patterns)

#### ❌ Anti-Pattern 1: Non-Idempotent INSERT

**Don't**:
```sql
-- This duplicates data on every deployment!
INSERT INTO OrderStatus VALUES (1, 'Pending');  -- ❌
```

**Do**:
```sql
-- Use MERGE or IF NOT EXISTS
MERGE INTO OrderStatus ...  -- ✅ Idempotent
```

---

#### ❌ Anti-Pattern 2: Hard-Coded Connection Strings

**Don't**:
```sql
-- Never do this!
INSERT INTO OtherServer.OtherDB.dbo.Table ...  -- ❌ Hard-coded
```

**Do**: Use linked servers or SQLCMD variables for cross-DB

---

#### ❌ Anti-Pattern 3: No Transaction for Related Changes

**Don't**:
```sql
-- Multiple related inserts without transaction
INSERT INTO Customers ...
INSERT INTO Orders ...  -- ❌ If this fails, customer orphaned
```

**Do**:
```sql
BEGIN TRY
    BEGIN TRANSACTION;
    
    INSERT INTO Customers ...
    INSERT INTO Orders ...
    
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT 'Error: ' + ERROR_MESSAGE();
    THROW;
END CATCH
```

---

### Troubleshooting

**Problem**: Post-deployment script doesn't run

**Solution**: 
- Check file is named `Script.PostDeployment.sql`
- Check it's at project root (or properly referenced)
- Check Build Action is set correctly

---

**Problem**: MERGE fails with "Violation of PRIMARY KEY constraint"

**Solution**: VALUES list has duplicate IDs
```sql
-- Check for duplicates
VALUES
    (1, 'Pending'),
    (1, 'Active')  -- ❌ Duplicate ID 1!
```

---

**Problem**: Changes to reference data don't appear after deployment

**Solution**: MERGE matched but values didn't change
- Check WHEN MATCHED condition includes all columns you changed
- Or data already has those values (already updated)

---

### Checklist

Before committing:
- [ ] Script is idempotent (MERGE or IF NOT EXISTS)
- [ ] Tested locally (deploy twice, no errors)
- [ ] Large operations use batching
- [ ] One-time migrations use version tracking
- [ ] PRINT statements for visibility
- [ ] Error handling (TRY/CATCH) for critical operations

After deployment:
- [ ] Check deployment logs (PRINT output)
- [ ] Query table to verify data populated
- [ ] No duplicate rows
- [ ] Values correct in all environments

Remember:
- Post-deployment runs EVERY time
- Must be safe to run multiple times
- MERGE is your friend for reference data
- Organize into multiple files for maintainability

---

[Due to length limits, I'll continue with the remaining playbooks in the next artifact. Would you like me to continue with:
- Playbook 5: Troubleshooting Flowchart
- Playbook 6: Code Review Checklist?]
