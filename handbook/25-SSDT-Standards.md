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
| Primary key | `PK_TableName` | `PK_Customer` |
| Foreign key | `FK_ChildTable_ParentTable` | `FK_Order_Customer` |
| Unique | `UQ_TableName_Column(s)` | `UQ_Customer_Email` |
| Check | `CK_TableName_Description` | `CK_Order_PositiveQuantity` |
| Default | `DF_TableName_Column` | `DF_Customer_CreatedAt` |

### Indexes

| Type | Convention | Example |
|------|------------|---------|
| Non-clustered | `IX_TableName_Column(s)` | `IX_Order_CustomerId` |
| Clustered (non-PK) | `CX_TableName_Column(s)` | `CX_Order_OrderDate` |
| Unique | `UX_TableName_Column(s)` | `UX_Customer_Email` |
| Filtered | `IX_TableName_Column(s)_Description` | `IX_Order_Status_Active` |
| Covering | Mention key columns only | `IX_Order_CustomerId` (INCLUDE is implementation) |

### Views

| Rule | Convention | Example |
|------|------------|---------|
| Prefix | `vw_` | `vw_ActiveCustomer` |
| Description | Clear purpose | `vw_OrderSummary`, `vw_CustomerWithAddress` |

### Stored Procedures

| Rule | Convention | Example |
|------|------------|---------|
| Prefix | `usp_` | `usp_GetCustomerOrders` |
| Verb first | Action-oriented | `usp_CreateOrder`, `usp_UpdateCustomerStatus` |
| Avoid `sp_` | Reserved for system | Never `sp_GetCustomer` (conflicts with system procs) |

### Functions

| Rule | Convention | Example |
|------|------------|---------|
| Scalar | `fn_` prefix | `fn_CalculateTax` |
| Table-valued | `fn_` or `tvf_` | `fn_GetCustomerOrders` |

### Synonyms

| Rule | Convention | Example |
|------|------------|---------|
| Match target name when possible | Same as underlying object | `dbo.Customer` → `archive.Customer` |
| Or describe purpose | When bridging systems | `Legacy_Customer` |

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
| Currency/financial | `DECIMAL(18,2)` | Exact precision; never use MONEY |
| Percentages | `DECIMAL(5,2)` | 0.00 to 100.00 |
| High-precision | `DECIMAL(p,s)` | Specify precision and scale |
| Floating point | `FLOAT` | Only for scientific data; imprecise |

**Never use MONEY.** It has hidden rounding behavior and limited precision. Use DECIMAL.

### Dates and Times

| Use Case | Type | Notes |
|----------|------|-------|
| Date and time | `DATETIME2(7)` | Preferred over DATETIME |
| Date only | `DATE` | When time doesn't matter |
| Time only | `TIME` | Rare |
| Legacy compatibility | `DATETIME` | Only for existing systems |

**Use DATETIME2.** Higher precision, larger range, 6-8 bytes (same as DATETIME).

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

/Security/
    Schemas.sql
    Roles.sql

/Tables/
    /dbo/
        dbo.Customer.sql
        dbo.Order.sql
        dbo.OrderLine.sql
    /audit/
        audit.ChangeLog.sql
    /archive/
        archive.OrderHistory.sql

/Views/
    /dbo/
        dbo.vw_ActiveCustomer.sql
        dbo.vw_OrderSummary.sql

/Stored Procedures/
    /dbo/
        dbo.usp_GetCustomerOrders.sql
        dbo.usp_CreateOrder.sql

/Functions/
    /dbo/
        dbo.fn_CalculateOrderTotal.sql

/Indexes/
    IX_Order_CustomerId.sql
    IX_Order_OrderDate.sql

/Synonyms/
    dbo.LegacyCustomer.sql

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

/Snapshots/
    DatabaseProject_v1.0.dacpac
```

### Rules

| Rule | Rationale |
|------|-----------|
| One object per file | Easy to find, clear git history |
| File name matches object name | `dbo.Customer.sql` contains `dbo.Customer` table |
| Schema folders under each type | `/Tables/dbo/`, `/Tables/audit/` |
| Indexes can be inline or separate | Team choice; be consistent |
| Pre/Post scripts organized | `/Migrations/`, `/ReferenceData/`, `/OneTime/` |

---

## 27.4 Readability Standards

### Formatting

```sql
-- Table definition formatting
CREATE TABLE [dbo].[Customer]
(
    [CustomerId] INT IDENTITY(1,1) NOT NULL,
    [FirstName] NVARCHAR(100) NOT NULL,
    [LastName] NVARCHAR(100) NOT NULL,
    [Email] NVARCHAR(200) NOT NULL,
    [PhoneNumber] NVARCHAR(20) NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_Customer_IsActive] DEFAULT (1),
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Customer_CreatedAt] DEFAULT (SYSUTCDATETIME()),
    [UpdatedAt] DATETIME2(7) NULL,
    
    CONSTRAINT [PK_Customer] PRIMARY KEY CLUSTERED ([CustomerId]),
    CONSTRAINT [UQ_Customer_Email] UNIQUE ([Email])
)
```

| Element | Standard |
|---------|----------|
| Keywords | UPPERCASE (`CREATE TABLE`, `NOT NULL`) |
| Object names | Bracket-quoted (`[dbo].[Customer]`) |
| Indentation | 4 spaces (not tabs) |
| Columns | One per line |
| Constraints | After columns, separated by blank line or at end |
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

### Views

Always enumerate columns:

```sql
-- Good
CREATE VIEW [dbo].[vw_ActiveCustomer]
AS
SELECT 
    CustomerId,
    FirstName,
    LastName,
    Email,
    CreatedAt
FROM dbo.Customer
WHERE IsActive = 1

-- Bad
CREATE VIEW [dbo].[vw_ActiveCustomer]
AS
SELECT *
FROM dbo.Customer
WHERE IsActive = 1
```

---

