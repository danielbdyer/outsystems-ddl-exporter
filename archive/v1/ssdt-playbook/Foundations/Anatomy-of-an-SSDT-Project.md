# 5. Anatomy of an SSDT Project

---

## What You're Looking At

When you open the SSDT project in Visual Studio, you'll see a folder structure that represents your database schema. Every table has a corresponding file.

```
/DatabaseProject.sqlproj          ← Project file (MSBuild, settings)
/DatabaseProject.refactorlog      ← Rename tracking
/DatabaseProject.publish.xml       ← Publish profile(s)

/Tables/
    /dbo/
        dbo.Customer.sql          ← Each table is one file
        dbo.Order.sql
        dbo.OrderLine.sql
        dbo.Product.sql
    /audit/
        audit.ChangeLog.sql

/Scripts/
    /PreDeployment/
        PreDeployment.sql         ← Master pre-deployment script
    /PostDeployment/
        PostDeployment.sql        ← Master post-deployment script
        /Migrations/
            001_BackfillMiddleName.sql
            002_SeedStatusCodes.sql
        /ReferenceData/
            SeedCountries.sql
            SeedProductTypes.sql
        /OneTime/
            Release_2025.02_Fixes.sql
```

The generated project has exactly two folders: **`Tables/`** (one file per table, grouped by schema) and **`Scripts/`** (the pre- and post-deployment scripts and everything they `:r`-include). There are no separate folders for views, stored procedures, functions, synonyms, or indexes — the generator does not emit those objects, and indexes live in the same file as the table they belong to.

---

## File-to-Object Mapping

Every database object gets its own file. One file, one object.

### Tables

```sql
-- /Tables/dbo/dbo.Customer.sql

CREATE TABLE [dbo].[Customer] (
    [CustomerId] INT            IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_Customer_CustomerId]
            PRIMARY KEY CLUSTERED,
    [CreatedAt]  DATETIME2 (7)  NOT NULL
        CONSTRAINT [DF_Customer_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [Email]      NVARCHAR (200) NOT NULL,
    [FirstName]  NVARCHAR (100) NOT NULL,
    [IsActive]   BIT            NOT NULL
        CONSTRAINT [DF_Customer_IsActive] DEFAULT 1,
    [LastName]   NVARCHAR (100) NOT NULL
)

GO

CREATE UNIQUE INDEX [UIX_Customer_Email]
    ON [dbo].[Customer]([Email])
```

**How the generator lays a table out — and why it looks like this:**

- **The primary key comes first; the remaining columns follow the order they have in the source model.** The file is regenerated on every export, so change column order in the model, not in the emitted `.sql`.
- **The primary key, foreign keys, defaults, and single-column checks are written *inline*, laddered beneath the column they belong to** — everything about a column sits with the column. Composite keys and multi-column checks are the exception: they go at the end as table-level constraints, because they span more than one column.
- **The generator names the primary-key, foreign-key, and default constraints** on a fixed scheme: `PK_<Table>_<Column>`, `FK_<Table>_<Target>_<Column>`, `DF_<Table>_<Column>`. Check-constraint names are carried through from the source model (conventionally `CK_<Table>_<Rule>`).
- **Uniqueness is a unique index, not a `UNIQUE` constraint** — emitted as `CREATE UNIQUE INDEX [UIX_…]` after the table, in the same file.

### Foreign Keys

Foreign keys are written **inline, beneath the column that carries them** — not collected at the bottom of the table:

```sql
-- /Tables/dbo/dbo.Order.sql

CREATE TABLE [dbo].[Order] (
    [OrderId]     INT             IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_Order_OrderId]
            PRIMARY KEY CLUSTERED,
    [CustomerId]  INT             NOT NULL
        CONSTRAINT [FK_Order_Customer_CustomerId]
            FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([CustomerId]),
    [OrderDate]   DATE            NOT NULL,
    [TotalAmount] DECIMAL (18, 2) NOT NULL
)
```

The constraint name carries both ends of the relationship and the column: `FK_<ChildTable>_<ParentTable>_<Column>`. When cascade behaviour is set, it ladders underneath (`ON DELETE …` / `ON UPDATE …`). An untrusted foreign key is still written inline, then re-checked after the table with an `ALTER TABLE … NOCHECK CONSTRAINT` / `WITH NOCHECK CHECK CONSTRAINT` pair.

### Indexes

Indexes live in the **same file as their table**, after the `CREATE TABLE` (separated by `GO`) — there is no separate `Indexes/` folder. They are emitted as `CREATE INDEX` / `CREATE UNIQUE INDEX`; the `NONCLUSTERED` keyword is left implicit:

```sql
-- /Tables/dbo/dbo.Order.sql  (below the CREATE TABLE)

CREATE INDEX [IX_Order_CustomerId]
    ON [dbo].[Order]([CustomerId])
    INCLUDE([OrderDate], [TotalAmount])
```

---

## The .sqlproj File

This is the MSBuild project file. It defines:

- What files are included in the project
- Target SQL Server version
- Build settings
- Database references

**Key settings you'll see:**

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project DefaultTargets="Build" xmlns="...">
  <PropertyGroup>
    <DSP>Microsoft.Data.Tools.Schema.Sql.Sql150DatabaseSchemaProvider</DSP>  <!-- SQL 2019 -->
    <TargetDatabaseSet>True</TargetDatabaseSet>
    <DefaultCollation>SQL_Latin1_General_CP1_CI_AS</DefaultCollation>
    <DefaultFilegroup>PRIMARY</DefaultFilegroup>
    
    <!-- Build behavior -->
    <TreatTSqlWarningsAsErrors>True</TreatTSqlWarningsAsErrors>
  </PropertyGroup>
  
  <!-- File includes -->
  <ItemGroup>
    <Build Include="Tables\dbo\dbo.Customer.sql" />
    <Build Include="Tables\dbo\dbo.Order.sql" />
    <!-- etc. -->
  </ItemGroup>
  
  <!-- Pre/Post deployment scripts -->
  <ItemGroup>
    <PreDeploy Include="Scripts\PreDeployment\PreDeployment.sql" />
    <PostDeploy Include="Scripts\PostDeployment\PostDeployment.sql" />
  </ItemGroup>
</Project>
```

**You usually don't edit this directly.** Visual Studio maintains it when you add/remove files.

---

## The Publish Profile (.publish.xml)

This defines *how* to deploy and *what settings* to use. You'll have different profiles for different environments.

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="Current" xmlns="...">
  <PropertyGroup>
    <TargetConnectionString>Data Source=localhost;Initial Catalog=MyDatabase;Integrated Security=True</TargetConnectionString>
    
    <!-- Critical safety settings -->
    <BlockOnPossibleDataLoss>True</BlockOnPossibleDataLoss>
    <DropObjectsNotInSource>False</DropObjectsNotInSource>
    
    <!-- Behavior settings -->
    <IgnoreColumnOrder>True</IgnoreColumnOrder>
    <GenerateSmartDefaults>False</GenerateSmartDefaults>
    <AllowIncompatiblePlatform>False</AllowIncompatiblePlatform>
    <IgnorePermissions>True</IgnorePermissions>
    
    <!-- What to include -->
    <IncludeCompositeObjects>True</IncludeCompositeObjects>
    <IncludeTransactionalScripts>True</IncludeTransactionalScripts>
  </PropertyGroup>
</Project>
```

**Environment-specific profiles:**

| Profile | BlockOnPossibleDataLoss | DropObjectsNotInSource | GenerateSmartDefaults |
|---------|-------------------------|------------------------|-----------------------|
| Local.publish.xml | True | True | True |
| Dev.publish.xml | True | True | True |
| Test.publish.xml | True | True | False |
| UAT.publish.xml | True | False | False |
| Prod.publish.xml | True | False | False |

---

## The Refactorlog

The `.refactorlog` file tracks renames so SSDT knows when you're renaming vs. dropping-and-creating.

```xml
<?xml version="1.0" encoding="utf-8"?>
<Operations Version="1.0" xmlns="...">
  <Operation Name="Rename Refactor" Key="abc123..." ChangeDateTime="2025-01-15T10:30:00">
    <Property Name="ElementName" Value="[dbo].[Customer].[FirstName]" />
    <Property Name="ElementType" Value="SqlSimpleColumn" />
    <Property Name="ParentElementName" Value="[dbo].[Customer]" />
    <Property Name="ParentElementType" Value="SqlTable" />
    <Property Name="NewName" Value="GivenName" />
  </Operation>
</Operations>
```

**Critical rules:**
- Never delete refactorlog entries
- Always use GUI rename (or manually add entries)
- Protect during merges — refactorlog conflicts can cause data loss if resolved incorrectly

---

## Pre-Deployment and Post-Deployment Scripts

These are SQL scripts that run before and after SSDT applies the schema changes.

**PreDeployment.sql:** Runs first. Use for:
- Dropping dependencies that block schema changes
- Preparing data for transformations
- Anything that must happen *before* the schema changes

**PostDeployment.sql:** Runs last. Use for:
- Data migrations
- Seeding reference data
- Backfilling new columns
- Any data work that depends on the new schema existing

**Structure:**

```sql
-- /Scripts/PostDeployment/PostDeployment.sql

/*
Post-Deployment Script
This script runs after schema changes are applied.
Use SQLCMD :r to include other scripts.
*/

PRINT 'Starting post-deployment scripts...'

-- Permanent migrations (idempotent)
:r .\Migrations\001_BackfillMiddleName.sql
:r .\Migrations\002_SeedStatusCodes.sql

-- Reference data (idempotent)
:r .\ReferenceData\SeedCountries.sql
:r .\ReferenceData\SeedProductTypes.sql

-- One-time scripts for this release (remove after prod deploy)
:r .\OneTime\Release_2025.02_Fixes.sql

PRINT 'Post-deployment complete.'
```

The `:r` syntax is SQLCMD — it includes another file inline.

---

## Database References

If your project references objects in other databases (or linked servers), you need database references.

**Same-server reference:**

```xml
<ArtifactReference Include="..\OtherDatabase\OtherDatabase.dacpac">
  <DatabaseVariableLiteralValue>OtherDatabase</DatabaseVariableLiteralValue>
</ArtifactReference>
```

**Linked server / external reference:**

```xml
<ArtifactReference Include="ExternalDB.dacpac">
  <DatabaseVariableLiteralValue>LinkedServer.ExternalDB</DatabaseVariableLiteralValue>
  <SuppressMissingDependenciesErrors>True</SuppressMissingDependenciesErrors>
</ArtifactReference>
```

Without references, SSDT will fail to build if you reference objects it can't find.

---

## Build vs. Deploy

These are different operations:

### Build

**What happens:**
- Compiles the project
- Validates syntax
- Checks referential integrity (do FKs point to real tables?)
- Produces a `.dacpac` file

**When it runs:**
- Every time you build in Visual Studio
- In CI pipeline on every commit

**What it catches:**
- Syntax errors
- Missing references (FK to non-existent table)
- Type mismatches
- Duplicate object names

**What it doesn't catch:**
- Data issues (NULLs in a column you're making NOT NULL)
- Runtime performance
- Blocking behavior

### Deploy (Publish)

**What happens:**
- Takes the `.dacpac` (desired state)
- Connects to target database (current state)
- Computes the delta
- Generates deployment script
- Executes the script (or just generates, depending on settings)

**When it runs:**
- Manually from Visual Studio (Publish)
- In CD pipeline on merge to main
- Via SqlPackage command line

**What it catches:**
- Data violations (constraint failures)
- Permission issues
- Timeout/blocking (if it takes too long)

---

## Navigating the Project: Quick Reference

| I need to... | Go to... |
|--------------|----------|
| See/edit a table structure | `/Tables/{schema}/{schema}.{TableName}.sql` |
| Add a new table | Create file in `/Tables/{schema}/`, add to project |
| Add a column | Edit the table's `.sql` file, add the column |
| Add an index | Add it to the table's `.sql` file, below the `CREATE TABLE` |
| Add seed data | Add to post-deployment script in `/Scripts/PostDeployment/Migrations/` |
| See deployment settings | Open `.publish.xml` files |
| Find rename history | Check `.refactorlog` |
| See what's in the project | Open `.sqlproj` in text editor, or view in Solution Explorer |

---

## Your First Navigation Exercise

Before making any changes, orient yourself:

1. **Open the project in Visual Studio**
2. **Expand the Tables folder** — browse a few table definitions
3. **Find a table with foreign keys** — see how they're defined
4. **Open PostDeployment.sql** — see how it's structured
5. **Open a publish profile** — review the settings
6. **Build the project** — verify it compiles
7. **Do a Schema Compare** — compare your project to your local database

This gives you a mental map before you start making changes.

---

