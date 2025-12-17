# 21. Local Development Setup

---

## What You Need

Before you can work with the SSDT project, you need:

| Component | Purpose | Where to get it |
|-----------|---------|-----------------|
| SQL Server (local instance) | Target database for local deployment | SQL Server Developer Edition (free) or LocalDB |
| Visual Studio | IDE for editing SSDT projects | Visual Studio 2019/2022 |
| SSDT workload | SQL Server tooling for Visual Studio | Visual Studio Installer → Data storage and processing |
| Git | Version control | git-scm.com |
| Repository access | Clone the SSDT project | Azure DevOps (request access if needed) |

---

## Installing SQL Server Locally

### Option A: SQL Server Developer Edition (Recommended)

Full-featured SQL Server, free for development.

1. Download from Microsoft
2. Run installer, choose "Basic" installation
3. Note the instance name (default: `MSSQLSERVER`, connection: `localhost`)
4. Install SQL Server Management Studio (SSMS) for database inspection

### Option B: SQL Server LocalDB

Lightweight, minimal installation.

1. Included with Visual Studio's data workload
2. Instance name typically: `(localdb)\MSSQLLocalDB`
3. Less full-featured but sufficient for basic development

### Verify Installation

Open SSMS, connect to your local instance:
- Server name: `localhost` (or `(localdb)\MSSQLLocalDB` for LocalDB)
- Authentication: Windows Authentication

If you can connect and see system databases, you're ready.

---

## Installing SSDT Tooling

1. Open **Visual Studio Installer**
2. Modify your Visual Studio installation
3. Select the **Data storage and processing** workload
4. Ensure **SQL Server Data Tools** is checked
5. Install

After installation, you should see SQL Server project templates in Visual Studio.

---

## Cloning the Repository

```bash
# Navigate to your projects directory
cd C:\Projects

# Clone the repository
git clone https://your-org@dev.azure.com/your-org/your-project/_git/SSDT-Database

# Navigate into the project
cd SSDT-Database
```

Open the `.sln` file in Visual Studio.

---

## Building the Project

### First Build

1. Open the solution in Visual Studio
2. In Solution Explorer, right-click the database project
3. Select **Build**

**Expected outcome:** Build succeeds, producing a `.dacpac` file in the `bin\Debug` (or `bin\Release`) folder.

### If Build Fails

| Error type | Likely cause | Resolution |
|------------|--------------|------------|
| Missing references | Database references not resolved | Check that referenced `.dacpac` files exist |
| Syntax errors | Invalid SQL in a file | Check the error list, fix the SQL |
| Unresolved objects | FK to table that doesn't exist | Ensure all dependent tables are in the project |
| Target version mismatch | Project targets newer SQL version | Update project properties or local SQL Server |

---

## Creating Your Local Database

### Option A: Publish from Visual Studio

1. Right-click the database project
2. Select **Publish**
3. In the Publish dialog:
   - Click **Edit** next to Target database connection
   - Server name: `localhost` (or your instance)
   - Database name: `YourProject_Local` (or similar)
   - Authentication: Windows Authentication
   - Click **OK**
4. Click **Generate Script** first to review what will run
5. If satisfied, click **Publish**

### Option B: Use a Publish Profile

1. Open your local publish profile (e.g., `Local.publish.xml`)
2. Update connection string if needed
3. Right-click project → **Publish**
4. Select the profile
5. Publish

### Verify the Database

1. Open SSMS
2. Connect to your local instance
3. Expand Databases — you should see your new database
4. Expand Tables — verify tables were created
5. Run a simple query to confirm structure

---

## Making Changes Locally

### The Local Development Cycle

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│  1. Edit        │────►│  2. Build       │────►│  3. Publish     │
│  .sql files     │     │  (verify syntax)│     │  to local DB    │
└─────────────────┘     └─────────────────┘     └────────┬────────┘
                                                         │
                                                         ▼
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│  6. PR          │◄────│  5. Commit      │◄────│  4. Verify      │
│  when ready     │     │  to branch      │     │  in SSMS        │
└─────────────────┘     └─────────────────┘     └─────────────────┘
```

### Reviewing Generated Scripts

Before committing, always review what SSDT will generate:

1. Right-click project → **Schema Compare**
2. Set source: Your project
3. Set target: Your local database
4. Click **Compare**
5. Review the differences
6. Click **View Script** (top toolbar) to see the generated SQL

This is your preview of what deployment will do. If anything looks wrong, stop and investigate.

---

## Useful Local Commands

### Schema Compare (GUI)

Right-click project → Schema Compare

Shows differences between your project and any database. Useful for:
- Verifying your local DB matches the project
- Seeing what a deployment will change
- Debugging "why isn't my change showing up"

### Generate Script Without Deploying

In the Publish dialog, click **Generate Script** instead of **Publish**. This creates the deployment script without running it. Useful for review.

### SqlPackage Command Line

For advanced scenarios:

```bash
# Generate deployment script
SqlPackage /Action:Script /SourceFile:bin\Debug\YourProject.dacpac /TargetConnectionString:"Server=localhost;Database=YourDb;Integrated Security=True" /OutputPath:deploy.sql

# Deploy
SqlPackage /Action:Publish /SourceFile:bin\Debug\YourProject.dacpac /TargetConnectionString:"Server=localhost;Database=YourDb;Integrated Security=True"
```

---

## Checklist: Before Opening a PR

- [ ] Project builds successfully
- [ ] Deployed to local database without errors
- [ ] Verified changes in SSMS (tables, columns, constraints exist as expected)
- [ ] Reviewed generated deployment script (Schema Compare → View Script)
- [ ] If rename: Refactorlog entry exists
- [ ] If NOT NULL on existing table: Default provided or pre-deployment backfill
- [ ] If FK: Checked for orphan data
- [ ] Pre/post deployment scripts are idempotent
- [ ] Change classified correctly (tier, mechanism)

---

