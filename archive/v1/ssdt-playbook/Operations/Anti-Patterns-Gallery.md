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

Under the production posture (`BlockOnPossibleDataLoss=true`) this drop-and-add is **refused**: the row-presence guard fires (`Msg 50000`), the deploy rolls back, and `FirstName` and its data survive. The column is only lost if the guard is relaxed — but either way the rename didn't happen. (A bare *table* rename is worse in a quieter way: under `DropObjectsNotInSource=false` it doesn't block at all — it phantoms to an empty new table while the populated original is stranded under the old name.)

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
2. Add as NULL, backfill, then tighten to NOT NULL in a later release — knowing the tightening step is itself blocked on a populated table (the data-loss guard checks row presence, not NULL content; see §17.2) and needs a logged `BlockOnPossibleDataLoss` relaxation for that deployment, after proving zero NULLs remain

**Rule of thumb:** NOT NULL on existing table = think about existing rows first.

---

## 19.3 The Forgotten FK Check

**What it looks like:**

Developer adds FK without checking for orphan data:

```sql
CONSTRAINT [FK_Order_Customer_CustomerId] FOREIGN KEY ([CustomerId]) 
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

## 19.5 The Refactorlog Cleanup

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

