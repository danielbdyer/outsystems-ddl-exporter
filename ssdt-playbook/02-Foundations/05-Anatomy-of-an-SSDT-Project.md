# 5. Anatomy of an SSDT Project

---

## What You're Looking At

When you open the SSDT project in Visual Studio, you'll see a folder structure that represents your database schema. Every object in the database has a corresponding file.

```
/DatabaseProject.sqlproj          ← Project file (MSBuild, settings)
/DatabaseProject.refactorlog      ← Rename tracking
/DatabaseProject.publish.xml       ← Publish profile(s)

/Security/
    Schemas.sql                   ← Schema definitions (dbo, audit, etc.)
    Roles.sql                     ← Database roles
    Users.sql                     ← Database users

/Tables/
    /dbo/
        dbo.Customer.sql          ← Each table is one file
        dbo.Order.sql
        dbo.OrderLine.sql
        dbo.Product.sql
    /audit/
        audit.ChangeLog.sql

/Views/
    /dbo/
        dbo.vw_ActiveCustomer.sql
        dbo.vw_OrderSummary.sql

/Stored Procedures/
    /dbo/
        dbo.usp_GetCustomerOrders.sql

/Functions/
    /dbo/
        dbo.fn_CalculateTotal.sql

/Indexes/                          ← Optional: can be inline or separate
    IX_Order_CustomerId.sql

/Synonyms/
    dbo.LegacyCustomer.sql

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

/Snapshots/                        ← Optional: dacpac versions for comparison
    DatabaseProject_v1.0.dacpac
```

---

## File-to-Object Mapping

Every database object gets its own file. One file, one object.

### Tables

```sql
-- /Tables/dbo/dbo.Customer.sql

CREATE TABLE [dbo].[Customer]
(
    [CustomerId] INT IDENTITY(1,1) NOT NULL,
    [FirstName] NVARCHAR(100) NOT NULL,
    [LastName] NVARCHAR(100) NOT NULL,
    [Email] NVARCHAR(200) NOT NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Customer_CreatedAt] DEFAULT (SYSUTCDATETIME()),
    [IsActive] BIT NOT NULL CONSTRAINT [DF_Customer_IsActive] DEFAULT (1),
    
    CONSTRAINT [PK_Customer] PRIMARY KEY CLUSTERED ([CustomerId]),
    CONSTRAINT [UQ_Customer_Email] UNIQUE ([Email])
)
```

**Note:** Constraints, defaults, and the primary key are defined inline. This keeps everything about the table in one place.

### Foreign Keys

Can be inline or separate. We prefer inline for clarity:

```sql
-- /Tables/dbo/dbo.Order.sql

CREATE TABLE [dbo].[Order]
(
    [OrderId] INT IDENTITY(1,1) NOT NULL,
    [CustomerId] INT NOT NULL,
    [OrderDate] DATE NOT NULL,
    [TotalAmount] DECIMAL(18,2) NOT NULL,
    
    CONSTRAINT [PK_Order] PRIMARY KEY CLUSTERED ([OrderId]),
    CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) 
        REFERENCES [dbo].[Customer]([CustomerId])
)
```

### Indexes

Can be inline (in table file) or separate files. Separate is cleaner for complex indexes:

```sql
-- /Indexes/IX_Order_CustomerId.sql

CREATE NONCLUSTERED INDEX [IX_Order_CustomerId]
ON [dbo].[Order]([CustomerId])
INCLUDE ([OrderDate], [TotalAmount])
```

### Views

```sql
-- /Views/dbo/dbo.vw_ActiveCustomer.sql

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
```

### Stored Procedures

```sql
-- /Stored Procedures/dbo/dbo.usp_GetCustomerOrders.sql

CREATE PROCEDURE [dbo].[usp_GetCustomerOrders]
    @CustomerId INT
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        o.OrderId,
        o.OrderDate,
        o.TotalAmount
    FROM dbo.[Order] o
    WHERE o.CustomerId = @CustomerId
    ORDER BY o.OrderDate DESC;
END
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
| Add an index | Create in `/Indexes/` or add inline to table file |
| Add a view | Create file in `/Views/{schema}/` |
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

