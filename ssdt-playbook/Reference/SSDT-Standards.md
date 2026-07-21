# 27. SSDT Standards

---

## 27.1 Naming Conventions

Consistent naming makes the schema self-documenting and simplifies tooling.

### Tables

| Rule | Convention | Example |
|------|------------|---------|
| Case | PascalCase | `Customer`, `OrderLine` |
| Plurality | Singular | `Customer` (not `Customers`) |
| Prefixes | None for regular tables | `Customer` (not `tblCustomer`) |
| Schema | Always specify | `dbo.Customer`, `audit.ChangeLog` |
| Junction tables | Both entities + relationship | `CustomerProductFavorite` |

### Columns

| Rule | Convention | Example |
|------|------------|---------|
| Case | PascalCase | `FirstName`, `OrderDate` |
| ID columns | TableName + `Id` | `CustomerId`, `OrderId` |
| FK columns | Match parent PK name | `CustomerId` in Order references `CustomerId` in Customer |
| Booleans | Is/Has/Can prefix | `IsActive`, `HasAccess`, `CanEdit` |
| Dates | Descriptive suffix | `CreatedAt`, `UpdatedAt`, `OrderDate` |
| Status fields | Noun | `Status`, `OrderStatus` (not `StatusId` unless FK) |

### Constraints

| Type | Convention | Example |
|------|------------|---------|
| Primary key | `PK_TableName_Column(s)` | `PK_Customer_CustomerId` |
| Foreign key | `FK_ChildTable_ParentTable_Column` | `FK_Order_Customer_CustomerId` |
| Check | `CK_TableName_Description` | `CK_Order_PositiveQuantity` |
| Default | `DF_TableName_Column` | `DF_Customer_CreatedAt` |

The generator synthesizes primary-key, foreign-key, and default names on this scheme; check-constraint names are carried through from the source model. Uniqueness is not a constraint here — it is a unique index (`UIX_…`; see Indexes below).

### Indexes

| Type | Convention | Example |
|------|------------|---------|
| Non-unique | `IX_TableName_Column(s)` | `IX_Order_CustomerId` |
| Unique | `UIX_TableName_Column(s)` | `UIX_Customer_Email` |
| Filtered | `IX_TableName_Column(s)` + `WHERE` predicate | `IX_Order_Status` |
| Covering | Mention key columns only | `IX_Order_CustomerId` (INCLUDE is implementation) |

Indexes are emitted as `CREATE INDEX` / `CREATE UNIQUE INDEX` — the `CLUSTERED` / `NONCLUSTERED` keyword is left implicit.

---

## 27.2 Preferred Data Types

These are our standards. Deviate only with good reason.

### Strings

| Use Case | Type | Notes |
|----------|------|-------|
| General text | `NVARCHAR(n)` | Unicode support; specify length |
| Very long text | `NVARCHAR(MAX)` | Only when needed (notes, descriptions) |
| Fixed-length codes | `NCHAR(n)` | Rare; e.g., `NCHAR(2)` for country codes |
| ASCII-only, high volume | `VARCHAR(n)` | Exception case; document why |

**Default to NVARCHAR.** Storage is cheap; character encoding bugs are expensive.

### Numbers

| Use Case | Type | Notes |
|----------|------|-------|
| Integer identifiers | `INT` | 2.1 billion max; sufficient for most cases |
| Large identifiers | `BIGINT` | When INT is insufficient |
| Small integers | `TINYINT` or `SMALLINT` | Status codes, flags (save space) |
| Currency/financial | `DECIMAL(37,8)` | Exact precision; never use MONEY |
| Percentages | `DECIMAL(5,2)` | 0.00 to 100.00 |
| High-precision | `DECIMAL(p,s)` | Specify precision and scale |
| Floating point | `FLOAT` | Only for scientific data; imprecise |

**Never use MONEY.** It has hidden rounding behavior and limited precision. Use DECIMAL.

### Dates and Times

| Use Case | Type | Notes |
|----------|------|-------|
| Date and time | `DATETIME` | OutSystems `rtDateTime`; `rtDateTime2` maps to `DATETIME2(7)` |
| Date only | `DATE` | When time doesn't matter |
| Time only | `TIME` | Rare |
| Legacy compatibility | `DATETIME` | Only for existing systems |

**OutSystems `DateTime` maps to `DATETIME`; `rtDateTime2` maps to `DATETIME2(7)`. OutSystems `Date` and `Time` attributes also store as `DATETIME` — the platform mapping.**

### Other

| Use Case | Type | Notes |
|----------|------|-------|
| Boolean | `BIT` | 0/1; SQL Server has no true boolean |
| Unique identifiers | `UNIQUEIDENTIFIER` | GUIDs; use for external-facing IDs or replication |
| Binary data | `VARBINARY(n)` or `VARBINARY(MAX)` | Files, images |

---

## 27.3 File Structure

### Standard Project Layout

```
/DatabaseProject.sqlproj
/DatabaseProject.refactorlog
/DatabaseProject.publish.xml
/Local.publish.xml
/Dev.publish.xml
/Test.publish.xml
/Prod.publish.xml

/Tables/
    /dbo/
        dbo.Customer.sql
        dbo.Order.sql
        dbo.OrderLine.sql
    /audit/
        audit.ChangeLog.sql

/Scripts/
    /PreDeployment/
        PreDeployment.sql
    /PostDeployment/
        PostDeployment.sql
        /Migrations/
            001_InitialSeed.sql
            002_BackfillCreatedAt.sql
        /ReferenceData/
            SeedCountries.sql
            SeedStatusCodes.sql
        /OneTime/
            Release_2025.02_Fixes.sql
```

The generated project has only **`Tables/`** and **`Scripts/`** — no folders for views, stored procedures, functions, synonyms, or indexes. The publish profiles and `.sqlproj`/`.refactorlog` sit at the root.

### Rules

| Rule | Rationale |
|------|-----------|
| One object per file | Easy to find, clear git history |
| File name matches object name | `dbo.Customer.sql` contains `dbo.Customer` table |
| Schema folders under Tables/ | `/Tables/dbo/`, `/Tables/audit/` |
| Indexes live with their table | Emitted in the same file, after the `CREATE TABLE` |
| Pre/Post scripts organized | `/Migrations/`, `/ReferenceData/`, `/OneTime/` |

---

## 27.4 Readability Standards

### Formatting

```sql
-- Table definition formatting
CREATE TABLE [dbo].[Customer] (
    [CustomerId]  INT            IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_Customer_CustomerId]
            PRIMARY KEY CLUSTERED,
    [CreatedAt]   DATETIME2 (7)  NOT NULL
        CONSTRAINT [DF_Customer_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [Email]       NVARCHAR (200) NOT NULL,
    [FirstName]   NVARCHAR (100) NOT NULL,
    [IsActive]    BIT            NOT NULL
        CONSTRAINT [DF_Customer_IsActive] DEFAULT 1,
    [LastName]    NVARCHAR (100) NOT NULL,
    [PhoneNumber] NVARCHAR (20)  NULL,
    [UpdatedAt]   DATETIME2 (7)  NULL
)

GO

CREATE UNIQUE INDEX [UIX_Customer_Email]
    ON [dbo].[Customer]([Email])
```

| Element | Standard |
|---------|----------|
| Keywords | UPPERCASE (`CREATE TABLE`, `NOT NULL`) |
| Object names | Bracket-quoted (`[dbo].[Customer]`) |
| Indentation | 4 spaces (not tabs) |
| Columns | One per line |
| Constraints | Inline, laddered beneath their column; composite keys and multi-column checks at table level |
| Commas | Trailing (at end of line, not beginning) |

### Comments

```sql
-- Single-line comment for brief notes

/*
Multi-line comment for:
- Complex business logic
- Non-obvious constraints
- Historical context
*/

-- For computed columns, explain the logic:
[TotalWithTax] AS ([Subtotal] * (1 + [TaxRate])) PERSISTED,  -- Tax calculated at order time
```

**Comment when:**
- Business logic isn't obvious
- Constraint exists for non-obvious reason
- Historical context helps future maintainers
- Workaround or special case

**Don't comment:**
- The obvious (`-- This is the customer ID`)
- What the code already says

---

