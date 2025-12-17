# 19. Anti-Patterns Gallery

---

## 19.1 The Naked Rename

**What it looks like:**

Developer opens `dbo.Person.sql`, manually changes column name:

```sql
-- Before
[FirstName] NVARCHAR(100) NOT NULL

-- After (just edited the text)
[GivenName] NVARCHAR(100) NOT NULL
```

**What happens:**

SSDT sees: "FirstName is gone. GivenName is new."

Generated script:
```sql
ALTER TABLE dbo.Person DROP COLUMN FirstName
ALTER TABLE dbo.Person ADD GivenName NVARCHAR(100) NULL
```

All data in FirstName is lost.

**The fix:**

Always use SSDT's GUI rename (right-click → Rename). This creates a refactorlog entry that tells SSDT "these are the same column."

**Visual cue:** If your `.refactorlog` file didn't change but a column name did, something is wrong.

---

## 19.2 The Optimistic NOT NULL

**What it looks like:**

Developer adds NOT NULL column without considering existing data:

```sql
-- Adding to table definition
[MiddleName] NVARCHAR(50) NOT NULL,  -- No default!
```

**What happens:**

- Build succeeds (SSDT doesn't know about your data)
- Deploy fails: "Cannot insert NULL into column 'MiddleName'"
- Or, with `GenerateSmartDefaults=True`, SSDT silently backfills empty strings

**The fix:**

Either:
1. Add with a default: `NOT NULL CONSTRAINT DF_Person_MiddleName DEFAULT ('')`
2. Add as NULL, backfill in post-deployment, then alter to NOT NULL in next release

**Rule of thumb:** NOT NULL on existing table = think about existing rows first.

---

## 19.3 The Forgotten FK Check

**What it looks like:**

Developer adds FK without checking for orphan data:

```sql
CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) 
    REFERENCES [dbo].[Customer]([CustomerId])
```

**What happens:**

If any `Order.CustomerId` value doesn't exist in `Customer.CustomerId`:
- Deploy fails with constraint violation
- Or, if using `WITH NOCHECK`, constraint is untrusted (optimizer ignores it)

**The fix:**

Before adding FK, run:
```sql
SELECT o.OrderId, o.CustomerId
FROM dbo.[Order] o
LEFT JOIN dbo.Customer c ON o.CustomerId = c.CustomerId
WHERE c.CustomerId IS NULL
```

Clean up orphans first, or use the WITH NOCHECK → clean → trust pattern.

---

## 19.4 The Ambitious Narrowing

**What it looks like:**

Developer narrows a column without checking data:

```sql
-- Before
[Email] NVARCHAR(200)

-- After
[Email] NVARCHAR(100)  -- Narrowed!
```

**What happens:**

- If any email is > 100 characters: Deploy fails (BlockOnPossibleDataLoss)
- If BlockOnPossibleDataLoss is off: Data truncation, silent corruption

**The fix:**

Before narrowing, verify:
```sql
SELECT MAX(LEN(Email)) AS MaxLength, COUNT(*) AS OverLimit
FROM dbo.Person
WHERE LEN(Email) > 100  -- New limit
```

If data exceeds new limit, clean it first or reconsider the change.

---

## 19.5 The CDC Surprise

**What it looks like:**

Developer changes schema on CDC-enabled table without considering capture instance:

```sql
-- Just adds a column like normal
[NewColumn] NVARCHAR(50) NULL,
```

**What happens:**

- Column added to table
- Existing capture instance doesn't include it
- Change History won't show changes to NewColumn
- Stale capture instance causes confusion

**The fix:**

Check if table is CDC-enabled first. If yes, follow CDC change protocol:
- Development: Disable/re-enable CDC (accepting gap)
- Production: Create new capture instance, manage dual-instance transition

---

## 19.6 The Refactorlog Cleanup

**What it looks like:**

Developer sees old refactorlog entries:
"We renamed that column two years ago. Why is this still here? Cleaning it up."

Deletes the entry.

**What happens:**

- Existing environments: Fine (column already renamed there)
- Fresh environment deployment: SSDT treats the old rename as drop+create
- Data loss in fresh environments

**The fix:**

Never delete refactorlog entries. They're needed for fresh environment deployments. They're small. Leave them.

---

## 19.7 The SELECT * View

**What it looks like:**

Developer creates view with SELECT *:

```sql
CREATE VIEW dbo.vw_AllCustomers
AS
SELECT * FROM dbo.Customer
```

**What happens:**

- View created with columns that exist *at creation time*
- Later: Column added to Customer
- View doesn't automatically include new column
- Queries against view miss data
- Confusion: "I added the column, why isn't it showing?"

**The fix:**

Always enumerate columns explicitly:
```sql
CREATE VIEW dbo.vw_AllCustomers
AS
SELECT 
    CustomerId,
    FirstName,
    LastName,
    Email,
    CreatedAt
FROM dbo.Customer
```

When you add a column, you must update the view too. This is a feature — it forces you to consider whether the view should expose the new column.

---

