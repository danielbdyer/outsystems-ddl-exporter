# Crisis-Aware Knowledge Base Planning: External DB Cutover This Weekend

## Situational Analysis

### Current Reality
- **Timeline**: External entities cutover **this weekend**
- **Team**: SQL-experienced developers, NEW to SSDT/DACPAC workflow
- **Work Type**: Normal schema changes (columns, tables, indexes) - NOT massive refactors
- **Critical Integration**: OutSystems Integration Studio refresh workflow CANNOT break
- **Dev Leads**: Will absorb extra load during transition period
- **Risk**: Team grinds to a halt Monday if knowledge gaps aren't filled

### Success Criteria by Timeline
- **Monday Morning**: No one blocked on basic tasks
- **Week 1**: No production incidents from schema changes
- **Month 1**: Team velocity back to normal, fewer dev lead escalations
- **Month 3**: Team self-sufficient, dev leads focus on architecture

---

## Phased Documentation Strategy

### 🔴 CRITICAL PATH (This Weekend → Monday)
**Goal**: Prevent grinding to a halt. Enable basic operations.

**Artifacts Needed NOW**:
1. START-HERE.md (2 pages)
2. Monday-Morning-Survival-Guide.md (5 pages)
3. OutSystems-DB-Change-Workflow.md (3 pages)
4. Dev-Lead-Weekend-Briefing.md (5 pages)
5. Top-10-Anti-Patterns.md (3 pages)

**Total**: 18 pages to write this weekend

---

### 🟡 WEEK 1 STABILIZATION (Days 1-5)
**Goal**: Reduce dev lead bottleneck. Handle common scenarios independently.

**Artifacts Needed Week 1**:
6. Playbook-Add-Column.md (2 pages)
7. Playbook-Add-Table.md (2 pages)
8. Playbook-Add-Index.md (2 pages)
9. Playbook-Post-Deployment-Script.md (3 pages)
10. Troubleshooting-Flowchart.md (2 pages)
11. Code-Review-Checklist.md (3 pages)

**Total**: 14 pages (can iterate during Week 1)

---

### 🟢 MONTH 1 OPTIMIZATION (Weeks 2-4)
**Goal**: Build independence. Reduce friction. Handle complex scenarios.

**Artifacts Needed Month 1**:
12. Playbook-Merge-Conflicts.md (3 pages)
13. Playbook-Refactor-Table.md (3 pages)
14. Playbook-Failed-Deployment.md (3 pages)
15. Performance-Quick-Checks.md (2 pages)
16. Learning-Path-Condensed.md (15 pages)

**Total**: 26 pages

---

### 🔵 MONTH 3+ EXCELLENCE (Long-term)
**Goal**: Expert-level capability. Self-improving team.

**Artifacts Needed Later**:
17. Advanced-Patterns.md (10 pages)
18. Resources-Annotated.md (10 pages)
19. Team-Standards-Guide.md (8 pages)

**Total**: 28 pages

---

## Detailed Artifact Planning

---

## 🔴 CRITICAL PATH ARTIFACTS (THIS WEEKEND)

---

### 1. START-HERE.md (2 pages)

**Purpose**: First thing anyone sees Monday morning. Orients them instantly.

**Structure**:
```markdown
# Database Schema Changes - Start Here

## ⚡ URGENT: Need to do something RIGHT NOW?

### I need to...
- **Deploy my changes** → [Monday Morning Survival Guide](./Monday-Morning-Survival-Guide.md#deploying-changes)
- **Refresh OutSystems entities** → [OutSystems Workflow](./OutSystems-DB-Change-Workflow.md#step-4-refresh-integration-studio)
- **Review a database PR** → [Dev Lead Checklist](./Dev-Lead-Weekend-Briefing.md#pr-review-checklist)
- **Fix a build error** → [Troubleshooting](./Monday-Morning-Survival-Guide.md#my-build-failed)
- **Something broke in production** → Slack #database-emergency, ping @db-leads

---

## 📋 Common Tasks (Click to Jump)

| Task | Audience | Time | Link |
|------|----------|------|------|
| Add a column to existing table | Developer | 5 min | [Quick Ref](./Monday-Morning-Survival-Guide.md#add-column) |
| Create new table | Developer | 10 min | [Quick Ref](./Monday-Morning-Survival-Guide.md#add-table) |
| Add reference data | Developer | 15 min | [Quick Ref](./Monday-Morning-Survival-Guide.md#reference-data) |
| Review database PR | Dev Lead | 10 min | [Checklist](./Dev-Lead-Weekend-Briefing.md#pr-review) |
| Deploy to Dev environment | Developer | 5 min | [Workflow](./OutSystems-DB-Change-Workflow.md#deploy-dev) |
| Sync OutSystems after DB change | Developer | 5 min | [Workflow](./OutSystems-DB-Change-Workflow.md#sync-outsystems) |

---

## 🚨 DON'T DO THESE (Will cause outages)

Read [Top 10 Anti-Patterns](./Top-10-Anti-Patterns.md) - **ESPECIALLY**:
- ❌ DON'T manually ALTER tables in SSMS (use project)
- ❌ DON'T deploy without refreshing Integration Studio
- ❌ DON'T use SELECT * in OutSystems queries
- ❌ DON'T drop columns without data migration plan
- ❌ DON'T commit without building first

[See all 10 →](./Top-10-Anti-Patterns.md)

---

## 🎯 This Weekend's Context

**What Changed**: 
External databases now managed through SSDT projects and DACPAC deployments (not manual SQL scripts).

**What This Means For You**:
- Schema changes go through Visual Studio database project
- Changes deploy via DACPAC (automated)
- **CRITICAL**: Must sync OutSystems Integration Studio after schema changes
- Dev leads review all database PRs before merge

**Why We Did This**:
- Version control for database schema (rollback, history, blame)
- Automated deployments (no more manual scripts)
- Consistent across Dev/Test/Prod
- Prevents drift and manual errors

---

## 📚 Learning Path (When You Have Time)

**This Week**: Just survive. Use guides above as needed.

**Week 2+**: Start [Learning Path](./Learning-Path-Condensed.md) to build understanding.

**Questions?**: 
- Slack #database-help
- Tag @dev-leads for urgent issues
- Office hours: Tuesday/Thursday 2-3 PM with DB team

---

## 🤝 Who to Ask

| Question Type | Ask | Where |
|--------------|-----|-------|
| How do I...? | #database-help | Slack |
| Urgent/Blocking | @dev-leads | Slack DM or #database-help |
| Production Issue | @db-on-call | #database-emergency |
| OutSystems Integration | @outsystems-leads | #outsystems-dev |
| PR Review | Your dev lead | GitHub PR |

---

## 📊 Monday Morning Standup Topics

Dev leads will cover:
1. New workflow overview (5 min)
2. Demo: Make a change, deploy it (5 min)
3. Demo: Sync OutSystems (3 min)
4. Q&A (5 min)

Come with questions!
```

**Why This Works**:
- Immediate orientation ("I need to...")
- Emergency contacts visible
- Anti-patterns at top (prevent disasters)
- Context without overwhelming
- Clear escalation paths

---

### 2. Monday-Morning-Survival-Guide.md (5 pages)

**Purpose**: Get through Monday without escalating everything to dev leads.

**Structure**:

```markdown
# Monday Morning Survival Guide

> **Goal**: Make it through your first day without getting blocked.  
> **Assumption**: You know SQL. You're new to SSDT/DACPAC workflow.

---

## Part 1: Your First Schema Change (15 Minutes)

### Scenario: Add "PhoneNumber" column to Customers table

**Step-by-step**:

#### 1. Open Database Project (2 min)
```
Visual Studio 2022
→ File > Open > Project/Solution
→ Navigate to: [your repo]\DatabaseProject\MyDatabase.sqlproj
→ Click Open
```

**Verify it opened**:
- Solution Explorer shows "MyDatabase" project
- See folders: Tables, Views, Stored Procedures
- If not, Slack #database-help with screenshot

#### 2. Find the Table (1 min)
```
Solution Explorer
→ Expand "Tables" folder  
→ Find "Customers.sql"
→ Double-click to open
```

**You'll see**:
```sql
CREATE TABLE [dbo].[Customers] (
    [CustomerId] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [FirstName] NVARCHAR(50) NOT NULL,
    [LastName] NVARCHAR(50) NOT NULL,
    [Email] NVARCHAR(100) NOT NULL
);
```

#### 3. Add Column (2 min)
**Add this line** before the closing `);`:
```sql
[PhoneNumber] VARCHAR(20) NULL,
```

**Result**:
```sql
CREATE TABLE [dbo].[Customers] (
    [CustomerId] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [FirstName] NVARCHAR(50) NOT NULL,
    [LastName] NVARCHAR(50) NOT NULL,
    [Email] NVARCHAR(100) NOT NULL,
    [PhoneNumber] VARCHAR(20) NULL,  -- ← New line
);
```

**⚠️ CRITICAL**: Notice it's still `CREATE TABLE`, not `ALTER TABLE`
- **This is correct!** DACPAC will figure out it needs to ALTER.
- **Don't change to ALTER TABLE** - that will break the project.

#### 4. Build Project (1 min)
```
Menu: Build > Build Solution
Or: Ctrl+Shift+B
```

**Watch Output window** (bottom of Visual Studio):
- ✅ Success: "Build succeeded"  
- ❌ Fail: Red text with errors

**If Build Failed**:
- Check syntax (missing comma, typo?)
- Error says "SQL71501: Unresolved reference"? → You referenced something not in project
- Still stuck? Copy error to #database-help

#### 5. Commit to Git (3 min)
```
Visual Studio Team Explorer
→ Changes tab
→ See "Customers.sql" listed
→ Write commit message: "Add PhoneNumber column to Customers table"
→ Click "Commit All"
```

**⚠️ CRITICAL**: Always build BEFORE committing
- Broken build = blocked colleagues

#### 6. Push and Create PR (3 min)
```
Team Explorer > Sync
→ Click "Push"

Then:
→ Go to GitHub/Azure DevOps
→ Create Pull Request
→ Target branch: main
→ Title: "Add PhoneNumber to Customers"
→ Add description: "For feature XYZ, storing customer phone numbers"
→ Request review from your dev lead
```

#### 7. After PR Approved: Deploy (3 min)

**Dev environment** (you can do this):
```
Right-click database project in Solution Explorer
→ Publish
→ Select target: Dev database connection
→ Click "Generate Script" (preview what will happen)
→ Review the ALTER TABLE statement
→ Click "Publish"
→ Wait for "Publish succeeded"
```

**Test environment** (dev lead does this, or CI/CD pipeline)

**Production** (requires approval, after business hours)

---

## Part 2: Critical OutSystems Integration

### 🚨 NEVER FORGET THIS STEP

**After deploying database changes**, you MUST refresh OutSystems:

#### Why?
OutSystems Integration Studio caches the database schema. If you add a column but don't refresh:
- OutSystems doesn't know column exists
- Your app can't use the new column
- Queries fail at runtime

#### How? (5 minutes)

**See**: [OutSystems-DB-Change-Workflow.md](./OutSystems-DB-Change-Workflow.md#step-4-refresh-integration-studio)

**Quick version**:
1. Open Integration Studio
2. Open your database extension module
3. Right-click table → Refresh Table
4. Verify new column appears
5. Publish extension
6. Test in Service Studio that new attribute exists

**⚠️ CRITICAL**: Do this in SAME environment you just deployed to
- Deployed to Dev DB? → Refresh in Dev OutSystems
- Deployed to Test DB? → Refresh in Test OutSystems

---

## Part 3: Common Tasks Reference

### Add a Table

**1. Right-click Tables folder → Add → Table**
```sql
CREATE TABLE [dbo].[NewTableName] (
    [NewTableNameId] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Column1] NVARCHAR(100) NOT NULL,
    [Column2] INT NULL,
    -- Add required audit columns (team standard):
    [CreatedDate] DATETIME2 NOT NULL DEFAULT GETDATE(),
    [CreatedBy] NVARCHAR(100) NOT NULL DEFAULT SUSER_NAME()
);
```

**2. Build, commit, PR, deploy, refresh OutSystems**

---

### Add an Index

**Scenario**: Orders table queries slow when filtering by CustomerId

**1. Open Orders.sql**

**2. Add after table definition**:
```sql
GO  -- Separator between CREATE TABLE and CREATE INDEX

CREATE NONCLUSTERED INDEX [IX_Orders_CustomerId]
    ON [dbo].[Orders]([CustomerId]);
GO
```

**Full example**:
```sql
CREATE TABLE [dbo].[Orders] (
    [OrderId] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [CustomerId] INT NOT NULL,
    [OrderDate] DATETIME2 NOT NULL,
    CONSTRAINT [FK_Orders_Customers] 
        FOREIGN KEY ([CustomerId]) 
        REFERENCES [dbo].[Customers]([CustomerId])
);
GO

CREATE NONCLUSTERED INDEX [IX_Orders_CustomerId]
    ON [dbo].[Orders]([CustomerId]);
GO
```

**3. Build, commit, PR** (same as above)

**Note**: Indexes DON'T require OutSystems refresh (no schema change)

---

### Add Reference Data (Post-Deployment Script)

**Scenario**: New OrderStatus table needs to be populated with statuses

**File**: `Script.PostDeployment.sql` (already exists in project)

**Add**:
```sql
-- Populate OrderStatus lookup table
PRINT 'Populating OrderStatus reference data...';

MERGE INTO [dbo].[OrderStatus] AS Target
USING (VALUES
    (1, 'Pending'),
    (2, 'Processing'),
    (3, 'Shipped'),
    (4, 'Delivered'),
    (5, 'Cancelled')
) AS Source ([StatusId], [StatusName])
ON Target.[StatusId] = Source.[StatusId]
WHEN NOT MATCHED BY TARGET THEN
    INSERT ([StatusId], [StatusName])
    VALUES (Source.[StatusId], Source.[StatusName])
WHEN MATCHED AND Target.[StatusName] <> Source.[StatusName] THEN
    UPDATE SET [StatusName] = Source.[StatusName];

PRINT 'OrderStatus reference data updated.';
GO
```

**Why MERGE?**
- Runs every deployment (post-deployment scripts ALWAYS run)
- MERGE = idempotent (safe to run multiple times)
- Won't duplicate data

**Pattern**: Use MERGE for all reference data population.

---

## Part 4: Troubleshooting

### My Build Failed

**Error**: `SQL71501: Procedure [GetCustomers] has an unresolved reference to object [Customers]`

**Meaning**: You referenced a table/column that doesn't exist in project

**Fix**:
1. Check spelling of table/column name
2. Verify table exists in project (Solution Explorer)
3. If referencing external database, need to add database reference (ask dev lead)

---

**Error**: `SQL71006: Only one statement is allowed per batch. Use GO to separate statements.`

**Meaning**: You have multiple CREATE statements without GO separators

**Fix**:
```sql
-- Wrong:
CREATE TABLE Table1 (...);
CREATE TABLE Table2 (...);  -- Error!

-- Correct:
CREATE TABLE Table1 (...);
GO
CREATE TABLE Table2 (...);
GO
```

---

### My PR Was Rejected

**Common reasons**:

1. **"Build failed"** → You didn't build before committing
   - Fix: Build locally, fix errors, commit again

2. **"Missing foreign key index"** → You added FK but no supporting index
   - Fix: Add index on FK column (see above)

3. **"No description"** → PR description is empty
   - Fix: Explain what changed and why

4. **"Violates naming standards"** → Table/column names don't match conventions
   - Fix: Use Refactor → Rename in Visual Studio (see dev lead)

---

### Deployment Failed

**Error**: `Timeout expired`

**Meaning**: Operation took too long (large table, data migration)

**Fix**: Talk to dev lead - may need special handling for large operations

---

**Error**: `Deployment blocked: possible data loss`

**Meaning**: Your change would drop data (e.g., dropping column, changing data type)

**What to do**:
1. Is data loss intended? (Dropping unused column?) → Tell dev lead, they'll approve
2. Need to preserve data? → Need data migration script (dev lead will help)

**⚠️ NEVER override this yourself** - you'll cause data loss

---

## Part 5: When to Escalate

### Handle Yourself ✅
- Adding columns (NULL or with DEFAULT)
- Adding new tables
- Adding indexes
- Simple reference data in post-deployment scripts
- Build errors (try to debug for 15 min first)

### Ask Dev Lead 🟡
- Dropping columns (need data migration)
- Changing data types (need data migration)
- Renaming tables/columns (need refactorlog)
- Foreign key changes
- Merge conflicts
- Deployment errors after trying once

### Escalate Immediately 🔴
- Production deployment failed
- Data loss occurred
- Multiple people blocked
- Security concerns (permissions, sensitive data)
- Performance disaster (table locks, timeouts)

---

## Part 6: Quick Command Reference

### SqlPackage (Command Line Deployment)

**Generate script** (preview changes):
```powershell
SqlPackage.exe /Action:Script `
    /SourceFile:"MyDatabase.dacpac" `
    /TargetServerName:"DevServer" `
    /TargetDatabaseName:"MyDatabase" `
    /OutputPath:"deploy-script.sql"
```

**Deploy**:
```powershell
SqlPackage.exe /Action:Publish `
    /SourceFile:"MyDatabase.dacpac" `
    /TargetServerName:"DevServer" `
    /TargetDatabaseName:"MyDatabase"
```

**Location**: `C:\Program Files\Microsoft SQL Server\160\DAC\bin\SqlPackage.exe`

---

### Git Commands (if using command line)

```bash
# See what changed
git status

# Add all changes
git add .

# Commit
git commit -m "Add PhoneNumber to Customers"

# Push
git push origin feature/add-phone-number

# Pull latest from main
git checkout main
git pull origin main
```

---

## Part 7: First Week Checklist

### Day 1 (Monday) ✅
- [ ] Read this guide
- [ ] Attend standup (DB workflow demo)
- [ ] Make one simple change (add column or table)
- [ ] Successfully build and commit
- [ ] Create PR and get it reviewed

### Day 2-3 ✅
- [ ] Deploy your change to Dev
- [ ] Refresh OutSystems Integration Studio
- [ ] Test your change in OutSystems app
- [ ] Help a colleague with their first change

### Day 4-5 ✅
- [ ] Review someone else's database PR
- [ ] Add reference data via post-deployment script
- [ ] Read [Top 10 Anti-Patterns](./Top-10-Anti-Patterns.md)

### Week 2+ ✅
- [ ] Start [Learning Path](./Learning-Path-Condensed.md)
- [ ] Handle merge conflict (if happens)
- [ ] Optimize a slow query

---

## Resources

- **Slack**: #database-help (questions), #database-emergency (outages)
- **Office Hours**: Tuesday/Thursday 2-3 PM with DB team
- **Full Docs**: [All guides and playbooks](./START-HERE.md)
- **OutSystems Workflow**: [Detailed integration steps](./OutSystems-DB-Change-Workflow.md)
- **Dev Lead Guide**: [For leads doing reviews](./Dev-Lead-Weekend-Briefing.md)

---

**Last Updated**: [This Weekend]  
**Maintained By**: Database Platform Team
```

**Why This Works**:
- Walks through COMPLETE first task (not theoretical)
- Anticipates every "what if" and "what next"
- Clear escalation guidance (don't waste time OR bother leads unnecessarily)
- OutSystems integration emphasized (critical for this team)
- Troubleshooting for real errors they'll hit
- Checklist for first week (clear goals)

---

### 3. OutSystems-DB-Change-Workflow.md (3 pages)

**Purpose**: The critical integration piece. Database changes are only half the story.

**Structure**:

```markdown
# OutSystems + Database Change Workflow

> **CRITICAL**: Database changes are incomplete until OutSystems is updated.  
> **This workflow is mandatory** for all schema changes.

---

## The Problem This Solves

**Without this workflow**:
1. Developer adds `PhoneNumber` column to Customers table ✅
2. Deploys to Dev database ✅
3. Tries to use `Customer.PhoneNumber` in OutSystems ❌
4. **Error**: Attribute doesn't exist
5. **Why**: Integration Studio doesn't know about the column yet

**With this workflow**:
1. Database change → Deploy → **Refresh Integration Studio** → Publish extension → Use in app ✅

---

## Complete Workflow (6 Steps)

### Step 1: Make Database Change (15 min)
**See**: [Monday Morning Survival Guide](./Monday-Morning-Survival-Guide.md#part-1)

**Quick recap**:
1. Edit .sql file in Visual Studio database project
2. Build project
3. Commit and create PR
4. Get PR approved
5. Deploy to target environment (Dev/Test/Prod)

**Verify deployment**:
```sql
-- In SSMS, connect to database, run:
SELECT COLUMN_NAME 
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_NAME = 'Customers';

-- Should see your new column listed
```

---

### Step 2: Open Integration Studio (2 min)

**Location**: 
- Start Menu → OutSystems → Integration Studio
- Or: Service Center → Factory → Extensions → Your Extension → "Edit in Integration Studio"

**Open your database extension**:
```
File → Open
→ Navigate to your extension (.xif file)
→ Or: Recently used extensions on splash screen
```

**Which extension?**
- Usually named: `YourApp_Database` or `ExternalDB_Integration`
- Ask in #outsystems-dev if unsure
- **One extension per external database connection**

---

### Step 3: Connect to Database (1 min)

**If connection string stored in extension**:
- Integration Studio remembers connection
- Should auto-connect when you open extension

**If not connected**:
```
Menu: Connect → Database
→ Server: [Your DB server]
→ Database: [Your database name]
→ Authentication: Use stored credentials or Windows Auth
→ Click "Connect"
```

**Verify connection**:
- Left sidebar shows "Tables" folder
- Can expand to see table list
- If not, check connection settings or Slack #outsystems-dev

---

### Step 4: Refresh Entity (3 min)

**This is THE critical step.**

#### For Modified Table (column added/removed/changed):

**1. Find the table**:
```
Left sidebar → Entities → Database folder
→ Find your table (e.g., "Customers")
```

**2. Right-click table → "Refresh Table"**

**3. Review changes dialog**:
```
Integration Studio shows:
✅ Green = New attributes (columns added)
🟡 Yellow = Modified attributes (data type changed)
🔴 Red = Removed attributes (columns dropped)
```

**4. Verify changes match what you deployed**:
- Added `PhoneNumber` column? → Should show as green new attribute
- **Mismatch?** → You deployed to wrong environment, or didn't deploy yet

**5. Click "Apply"**

**Result**: Entity now has new attribute(s)

---

#### For New Table:

**1. Import the table**:
```
Left sidebar → Right-click "Entities" folder
→ "Add Entity from Database"
→ Select your new table from list
→ Click "OK"
```

**2. Configure entity** (usually defaults are fine):
- Primary key: Auto-detected
- Attributes: All columns imported
- Name: Matches table name

**3. Click "Finish"**

---

### Step 5: Verify Extension (2 min)

**Check**:
1. New/changed attributes visible in entity
2. Data types correct (NVARCHAR(50) → Text, INT → Integer, etc.)
3. **Identifier** (primary key) attribute marked with key icon

**Common issues**:

❌ **Attribute shows as "Text" but should be "Integer"**
- Fix: Right-click attribute → Properties → Data Type → Integer

❌ **Attribute name has spaces**: `Phone Number` instead of `PhoneNumber`
- Fix: Right-click attribute → Properties → Name → Remove spaces
- **Why**: OutSystems doesn't allow spaces in attribute names

❌ **Attribute missing**
- Reason: Didn't refresh, or table not connected
- Fix: Redo Step 4

---

### Step 6: Publish Extension (2 min)

**1. Save changes**:
```
File → Save (Ctrl+S)
```

**2. Verify extension builds**:
```
Menu: File → Verify (F7)
```

**Watch Output window**:
- ✅ "Extension is valid" → Good to go
- ❌ Errors → Fix before publishing (usually data type or naming issues)

**3. Publish**:
```
Menu: File → Publish (F5)

Or:

Menu: File → 1-Click Publish
→ Select environment (Dev/Test/Prod - match where you deployed DB)
→ Click "Publish"
```

**Wait for**:
- "Publishing extension..." 
- "Extension published successfully"
- **Takes 10-30 seconds**

**⚠️ CRITICAL**: Publish to SAME environment as database deployment
- Deployed DB to Dev? → Publish extension to Dev
- Deployed DB to Test? → Publish extension to Test

---

### Step 7: Update Service Studio Apps (5 min)

**Now** your OutSystems apps can use the new schema.

**1. Open Service Studio**

**2. Open app that uses this database**

**3. Manage Dependencies**:
```
Menu: Manage Dependencies (Ctrl+Q)
→ Find your database extension (e.g., "YourApp_Database")
→ Expand to see entities
→ Find your entity (e.g., "Customers")
→ Check that new attribute visible (e.g., "PhoneNumber")
```

**If attribute NOT visible**:
- Close Manage Dependencies
- Menu: Module → Refresh Server Data
- Wait 30 seconds
- Retry Manage Dependencies

**4. If using new table**, add dependency:
```
Manage Dependencies
→ Find your database extension
→ Check the box next to new table
→ Click "Apply"
```

**5. Use the new attribute**:

Example - Display in form:
```
1. Open your screen
2. Add Input widget
3. Variable: Customer.PhoneNumber
4. Label: "Phone Number"
```

Example - Use in aggregate:
```
1. Open your aggregate
2. New attribute appears automatically in Output
3. Can now use in filters, sorting, etc.
```

**6. Publish module**:
```
Menu: 1-Click Publish (F5)
```

**7. Test**:
```
Open app in browser
→ Navigate to screen with new field
→ Verify it displays/saves correctly
```

---

## Complete Workflow Checklist

Use this for EVERY database schema change:

### Database Side
- [ ] 1. Change .sql file in database project
- [ ] 2. Build project (no errors)
- [ ] 3. Commit to Git
- [ ] 4. Create PR
- [ ] 5. PR approved by dev lead
- [ ] 6. Deploy to target environment
- [ ] 7. Verify in SSMS that change applied

### OutSystems Side  
- [ ] 8. Open Integration Studio
- [ ] 9. Open database extension
- [ ] 10. Refresh table (or add new table)
- [ ] 11. Verify changes look correct
- [ ] 12. Publish extension to same environment
- [ ] 13. Open Service Studio
- [ ] 14. Manage Dependencies to verify new attributes
- [ ] 15. Update app to use new schema
- [ ] 16. Publish app
- [ ] 17. Test in browser

**⏱️ Total Time**: ~30-45 minutes for first time, ~15-20 minutes after practice

---

## Common Mistakes (Anti-Patterns)

### ❌ Forgetting to Refresh Integration Studio

**Symptom**: 
```
OutSystems app breaks with error:
"Invalid attribute 'PhoneNumber' in entity 'Customer'"
```

**Why**: Database has column, but OutSystems doesn't know about it yet

**Fix**: Complete Step 4 (Refresh Entity) and Step 5 (Publish Extension)

---

### ❌ Wrong Environment Mismatch

**Scenario**:
- Deployed database change to **Dev** database
- Refreshed Integration Studio against **Test** database
- Extension doesn't show new column

**Why**: You're looking at wrong database

**Fix**: 
1. In Integration Studio: Menu → Connect → Database
2. Verify connection string points to **Dev** database
3. Refresh table again

---

### ❌ Publishing Extension Before Deploying Database

**Symptom**:
- Refresh table in Integration Studio
- New column doesn't appear
- "Table is up to date" message

**Why**: Database doesn't have column yet (you didn't deploy)

**Fix**: 
1. Deploy database change first (Step 6 of main workflow)
2. THEN refresh Integration Studio

**Order matters**: Database → Integration Studio, not reverse

---

### ❌ Not Refreshing ALL Affected Extensions

**Scenario**:
- You have multiple OutSystems apps using same database
- Each app has its own extension module
- You only refreshed one extension

**Result**: 
- App A works (you refreshed its extension)
- App B breaks (you didn't refresh its extension)

**Fix**: 
- List all extensions using the changed table
- Refresh and publish EACH ONE
- Ask in #outsystems-dev: "Which extensions use Customers table?"

---

## Environment-Specific Notes

### Dev Environment
- **Fast iteration**: Deploy DB → Refresh Integration Studio immediately
- **Testing**: Use sample data, can break things
- **No approvals needed**: Deploy freely

### Test Environment
- **Coordination**: Check if QA testing before you deploy
- **Timing**: Coordinate DB + OutSystems deployment (both or neither)
- **Communication**: Post in #qa-team when deploying

### Production Environment
- **After hours**: Deploy during maintenance window
- **Approval required**: Change Advisory Board (CAB) approval
- **Rollback plan**: Document how to undo if issues
- **Monitoring**: Watch for errors after deployment
- **Communication**: 
  - Pre-deployment: Announce in #general
  - Post-deployment: Confirm success in #general

---

## Rollback Procedure

**If OutSystems breaks after DB deployment**:

### Quick Fix (Revert Extension)
```
1. Integration Studio → File → Publish Previous Version
2. Select version before your change
3. Publish
4. OutSystems app should work again (old schema)
5. Database still has new schema (mismatch, but non-breaking)
```

### Full Rollback (Revert Database)
**Requires dev lead**:
1. Use SSDT to revert database change
2. Deploy previous database version
3. Refresh Integration Studio (remove new attributes)
4. Publish extension
5. OutSystems apps back to previous state

**Prevention**: Always deploy to Dev/Test first, test thoroughly before Prod

---

## Troubleshooting

### Integration Studio Won't Connect to Database

**Check**:
1. VPN connected (if using VPN)?
2. Firewall allows connection?
3. Credentials correct?
4. Database server name spelled correctly?

**Test connection**:
```
Open SSMS (SQL Server Management Studio)
→ Connect with same credentials
→ Can you see tables?
  - Yes: Issue is Integration Studio config
  - No: Issue is network/permissions
```

**Fix**: Slack #database-help with error message screenshot

---

### Refresh Table Shows No Changes

**Reasons**:
1. Database change not deployed yet → Deploy first
2. Connected to wrong database → Verify connection string
3. Wrong table selected → Check table name matches

**Debug**:
```sql
-- In SSMS, run this against the database:
SELECT COLUMN_NAME, DATA_TYPE 
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_NAME = 'Customers'
ORDER BY ORDINAL_POSITION;

-- Do you see your new column?
  - Yes: Integration Studio connection issue
  - No: Database not deployed
```

---

### Extension Won't Publish

**Error**: "Invalid data type mapping"

**Cause**: SQL data type doesn't map to OutSystems type

**Common mismatches**:
```
SQL: VARCHAR(MAX) → OutSystems: ❌ No direct mapping
Fix: Use NVARCHAR(2000) or split into smaller fields

SQL: DECIMAL(20,4) → OutSystems: Decimal (works, but...)
Fix: Consider if you need that precision

SQL: GEOGRAPHY → OutSystems: ❌ Not supported
Fix: Store as Text (WKT format) or separate Lat/Long columns
```

**Fix**: Ask dev lead for data type guidance

---

### App Shows Old Schema After Publishing

**Symptom**: Published extension, but Service Studio still shows old attributes

**Fix**: 
```
Service Studio
→ Menu: Module → Refresh Server Data
→ Wait 30-60 seconds
→ Restart Service Studio (if still not showing)
→ Manage Dependencies → Should now see new attributes
```

**If still not working**: Clear Service Studio cache
```
Close Service Studio
→ Navigate to: %LOCALAPPDATA%\OutSystems\ServiceStudio
→ Delete "Cache" folder
→ Restart Service Studio
```

---

## Quick Reference

### Integration Studio Shortcuts
```
F7  - Verify extension
F5  - Publish extension
F9  - Connect to database
Ctrl+R - Refresh current table
```

### When to Refresh Integration Studio

| Database Change | Refresh Needed? | Type |
|----------------|----------------|------|
| Add column | ✅ Yes | Refresh Table |
| Remove column | ✅ Yes | Refresh Table |
| Rename column | ✅ Yes | Refresh Table |
| Change data type | ✅ Yes | Refresh Table |
| Add index | ❌ No | N/A |
| Add stored proc | ✅ Yes | Add Action |
| Add table | ✅ Yes | Add Entity |
| Drop table | ✅ Yes | Remove Entity |
| Reference data (post-deployment) | ❌ No | N/A |

---

## Communication Template

**When deploying to Test/Prod**, post in relevant Slack channel:

```
🗄️ Database Deployment - [Environment]

Change: [Brief description, e.g., "Added PhoneNumber column to Customers"]
Environment: [Dev/Test/Prod]
Deployed: [Time]
OutSystems Extensions Updated: [Extension names]

Impact:
- [List apps affected]
- [Expected behavior changes]

Tested in Dev: ✅
Rollback plan: [How to undo if needed]

Questions? Reply here or DM me.
```

---

**Last Updated**: [This Weekend]  
**Maintained By**: Database Platform Team & OutSystems Team
```

**Why This Works**:
- Shows COMPLETE workflow (DB + OutSystems as one process)
- Anticipates every mistake (wrong environment, wrong order, etc.)
- Specific to OutSystems Integration Studio (not generic)
- Environment-specific guidance (Dev vs Test vs Prod)
- Rollback procedures (things will go wrong)
- Communication templates (team coordination)

---

### 4. Dev-Lead-Weekend-Briefing.md (5 pages)

**Purpose**: Dev leads carry the load this week. Give them everything they need.

**Structure**:

```markdown
# Dev Lead Weekend Briefing: External DB Cutover

> **Status**: We're live Monday. Here's what you need to know.  
> **Your Role**: Review PRs, unblock developers, escalate production issues.

---

## Part 1: What Changed This Weekend

### Before (Manual SQL Scripts)
```
Developer needs schema change
→ Writes ALTER TABLE script manually
→ Tests in Dev (maybe)
→ Emails DBA
→ DBA runs script in Test/Prod
→ Hopes it works
→ If breaks, manual debugging
```

**Problems**:
- No version control
- Environment drift
- Manual errors
- Can't rollback easily
- No audit trail

### After (SSDT + DACPAC + Git)
```
Developer needs schema change
→ Edits CREATE TABLE in Visual Studio project
→ Builds (validates syntax)
→ Commits to Git
→ Creates PR
→ Dev lead reviews PR ← YOU ARE HERE
→ Merge to main
→ CI/CD pipeline deploys DACPAC to Dev/Test
→ Manual approval for Prod
→ Refresh OutSystems Integration Studio
→ Done
```

**Benefits**:
- Full version control (Git blame, history, rollback)
- Consistent across environments
- Automated validation (build catches errors)
- Audit trail (who changed what when)
- Safer deployments (preview script before applying)

---

## Part 2: Your Responsibilities This Week

### Code Review (10-15 min per PR)
- **Volume**: Expect 3-5 database PRs per day initially
- **Goal**: Catch issues before they reach Dev environment
- **SLA**: Review within 2 hours during business hours
- **After Hours**: Not expected (unless production emergency)

### Unblocking Developers (5-10 min per question)
- **Channel**: #database-help Slack channel
- **Volume**: Expect 10-20 questions per day Week 1
- **Goal**: Answer quickly so they don't stay blocked
- **Escalate**: To @db-platform-team if you don't know answer

### Production Oversight
- **Approvals**: All Prod deployments need your sign-off
- **Timing**: After hours (7pm-7am) or weekends only
- **Presence**: Be available during deployment window
- **Rollback**: Prepared to execute if issues

### Monday Morning Standup (15 min)
- **When**: Monday 9:30 AM (after standup)
- **What**: Demo workflow to team
- **Goal**: Everyone sees it once before trying
- **You'll Cover**:
  1. Open database project (2 min)
  2. Make a change (add column) (3 min)
  3. Build, commit, PR (3 min)
  4. Show PR review checklist (2 min)
  5. Deploy to Dev (2 min)
  6. Refresh Integration Studio (3 min)

---

## Part 3: PR Review Checklist

### Every Database PR Must Have:

#### ✅ 1. Build Succeeds
```
Check CI/CD status:
- Green checkmark = good
- Red X = failed, don't merge

If failed:
→ Comment: "Build failed, please fix errors and repush"
→ Don't review further until build passes
```

#### ✅ 2. Descriptive Title & Description
```
❌ Bad: "Update table"
✅ Good: "Add PhoneNumber column to Customers for feature ABC-123"

PR should answer:
- What changed?
- Why?
- Which OutSystems feature needs this?
- Any special deployment considerations?
```

#### ✅ 3. Appropriate Change Type

**Check if change matches intent**:
```
PR says "Add column" → File shows CREATE TABLE with new column ✅
PR says "Add column" → File shows ALTER TABLE ❌ Wrong approach!
```

**Schema changes should be**:
- In CREATE statements (not ALTER)
- DACPAC figures out how to apply change

**Exception**: Pre/post deployment scripts CAN have ALTER/INSERT/UPDATE

#### ✅ 4. Naming Conventions

**Tables**:
```
✅ PascalCase: Customer, OrderItem, ProductCategory
❌ lowercase: customer
❌ snake_case: order_item
❌ Plural: Customers (we use singular)
```

**Columns**:
```
✅ PascalCase: FirstName, OrderDate, TotalAmount
❌ camelCase: firstName
❌ Abbreviations: FName, Qty (spell out)
```

**Primary Keys**:
```
✅ [TableName]Id: CustomerId, OrderId
❌ Id (alone)
❌ CustomerID (all caps)
```

**Foreign Keys**:
```
✅ FK_ChildTable_ParentTable: FK_Orders_Customers
✅ FK_OrderItems_Orders
❌ FK_Customers (missing child table)
```

**Indexes**:
```
✅ IX_TableName_Column1_Column2: IX_Orders_CustomerId_OrderDate
❌ IX_Orders (what columns?)
❌ idx_orders_customer (wrong case)
```

#### ✅ 5. Data Types Appropriate

**Common Issues**:
```
❌ VARCHAR vs NVARCHAR
   - Use NVARCHAR for user-facing text (supports Unicode)
   - Use VARCHAR only for internal codes (order status, etc.)

❌ DATETIME vs DATETIME2
   - Use DATETIME2 (more precise, SQL Server 2008+ standard)
   - Don't use old DATETIME

❌ DECIMAL without precision
   - DECIMAL needs (precision, scale): DECIMAL(10,2)
   - Common: Money = DECIMAL(10,2), Percentage = DECIMAL(5,2)

❌ NVARCHAR(MAX)
   - Avoid unless truly need unlimited length
   - Causes performance issues
   - Use NVARCHAR(500), NVARCHAR(2000), etc.

❌ INT when BIGINT needed
   - INT max: 2.1 billion
   - BIGINT max: 9 quintillion
   - Use BIGINT for high-volume tables (Orders, Events, Logs)
```

**Guide developers**:
```
Comment: "Consider NVARCHAR instead of VARCHAR for user-entered 
names (supports international characters)"
```

#### ✅ 6. NULL vs NOT NULL Correct

**Rules**:
```
NOT NULL when:
- Required for business logic (can't have order without customer)
- Always has a value (CreatedDate, IsActive)
- Primary keys (always NOT NULL)
- Foreign keys (usually NOT NULL)

NULL when:
- Optional fields (PhoneNumber, MiddleName)
- Unknown values (EndDate for ongoing records)
```

**Check for**:
```
❌ NOT NULL without DEFAULT on existing table with data
   → Will fail deployment (existing rows have NULL)
   → Need DEFAULT or nullable first, then data migration

✅ NOT NULL WITH DEFAULT on new column
   [PhoneNumber] VARCHAR(20) NOT NULL DEFAULT ''

✅ NULL on new column
   [PhoneNumber] VARCHAR(20) NULL
```

#### ✅ 7. Foreign Keys Have Supporting Indexes

**Why**:
- Foreign keys without indexes = slow queries
- Updates to parent table lock child table
- OutSystems generates lots of FK queries

**Check**:
```sql
-- If PR adds this:
CONSTRAINT [FK_Orders_Customers] 
    FOREIGN KEY ([CustomerId]) 
    REFERENCES [dbo].[Customers]([CustomerId])

-- Must also add this:
CREATE NONCLUSTERED INDEX [IX_Orders_CustomerId]
    ON [dbo].[Orders]([CustomerId]);
```

**If missing**:
```
Comment: "Please add index on CustomerId to support the 
foreign key (prevents locking issues)"
```

#### ✅ 8. Pre/Post Deployment Scripts Are Idempotent

**Check for**:
```
❌ INSERT VALUES (will duplicate on re-run)
✅ MERGE statement (safe to re-run)

❌ ALTER TABLE ADD column (fails if column exists)
✅ IF NOT EXISTS (safe to re-run)

❌ UPDATE without WHERE (affects all rows every time)
✅ UPDATE with specific WHERE (only missing data)
```

**Example**:
```sql
-- ❌ Bad (not idempotent):
INSERT INTO OrderStatus (StatusId, StatusName)
VALUES (1, 'Pending');

-- ✅ Good (idempotent):
IF NOT EXISTS (SELECT 1 FROM OrderStatus WHERE StatusId = 1)
BEGIN
    INSERT INTO OrderStatus (StatusId, StatusName)
    VALUES (1, 'Pending');
END

-- ✅ Better (idempotent + updates):
MERGE INTO OrderStatus AS Target
USING (VALUES (1, 'Pending')) AS Source (StatusId, StatusName)
ON Target.StatusId = Source.StatusId
WHEN NOT MATCHED THEN INSERT VALUES (Source.StatusId, Source.StatusName);
```

#### ✅ 9. No Sensitive Data or Passwords

**Check for**:
```
❌ Connection strings with passwords
❌ API keys
❌ Hardcoded user credentials
❌ Customer PII in comments or scripts
```

**If found**:
```
DO NOT MERGE
Comment: "Security issue: Remove sensitive data. Use SQLCMD 
variables or environment config instead."
Escalate to security team
```

#### ✅ 10. Breaking Changes Documented

**Breaking change = affects existing OutSystems apps**

**Examples**:
- Dropping columns
- Renaming columns (even with refactorlog)
- Changing data types (e.g., VARCHAR(50) → VARCHAR(20))
- Dropping tables
- Changing NOT NULL constraints

**PR must include**:
```
"Breaking Change: Dropping 'MiddleName' column from Customers

Impact:
- CustomerDetailScreen still references MiddleName
- Need to update before deploying this change

Plan:
1. Deploy this PR to Dev only
2. Update CustomerDetailScreen to remove MiddleName reference
3. Test in Dev
4. Then deploy to Test/Prod
```

**If missing**:
```
Comment: "This is a breaking change. Please document:
- Which OutSystems apps are affected?
- What's the deployment order?
- Who's updating the affected apps?"
```

---

### Quick Review Decision Tree

```
PR submitted
├─ Build passed? ─NO→ Request fix, don't review further
├─ Has description? ─NO→ Request description
├─ Naming conventions? ─NO→ Request rename (refactor tool)
├─ Data types OK? ─NO→ Suggest corrections
├─ FKs have indexes? ─NO→ Request indexes
├─ Idempotent scripts? ─NO→ Request MERGE or IF NOT EXISTS
├─ Breaking changes documented? ─NO→ Request documentation
└─ All good? ─YES→ Approve + Comment "LGTM, nice work!"
```

---

## Part 4: Common Developer Questions & Answers

### "My build failed with 'unresolved reference' error"

**Answer**:
```
You're referencing a table/column that's not in the project.

Check:
1. Spelling of table/column name
2. Schema name ([dbo]. prefix)
3. If it's in another database, need database reference

Send me the full error message if still stuck.
```

---

### "How do I rename a column?"

**Answer**:
```
DON'T manually change the CREATE TABLE script!

Use Refactor tool:
1. Open table .sql file
2. Right-click column name
3. Refactor → Rename
4. Enter new name
5. Preview changes (shows all affected objects)
6. Apply

This creates a refactorlog entry = data preserved during deployment.

Manual rename = DACPAC thinks you dropped old column and added new one = DATA LOSS.
```

---

### "Can I deploy straight to production?"

**Answer**:
```
NO. Always:
1. Deploy to Dev
2. Test with OutSystems app
3. Deploy to Test
4. QA tests
5. Get approval for Prod deployment
6. Deploy to Prod after hours

Emergency hotfix exception: With VP approval only.
```

---

### "Do I need to refresh Integration Studio for an index?"

**Answer**:
```
NO. Indexes are internal to database.

Refresh Integration Studio only for:
- New columns
- Removed columns
- Changed data types
- New tables
- Dropped tables
- Renamed columns

NOT for:
- Indexes
- Check constraints
- Reference data (post-deployment inserts)
```

---

### "My PR was auto-rejected by CI/CD"

**Answer**:
```
Common reasons:
1. Build failed (syntax error)
2. Code analysis warnings (SELECT *, missing indexes)
3. Schema compare detected drift

Check CI/CD logs for specific error.
Fix and repush.
```

---

### "Can I skip the PR and commit directly to main?"

**Answer**:
```
NO. Even for tiny changes.

Why:
- Another dev lead needs to review
- Catches mistakes before deployment
- Creates audit trail
- Teaches good habits

Emergency exception: Production is down, WITH phone/Slack coordination.
```

---

## Part 5: Deployment Approval Process

### Dev Environment
```
Approval: NOT NEEDED
Who Can Deploy: Any developer
When: Anytime
Communication: None required (optional Slack post)
Rollback: Developer handles
```

### Test Environment
```
Approval: Dev Lead (you)
Who Can Deploy: Developers with your OK
When: Business hours preferred
Communication: Post in #qa-team before deploying
Rollback: Dev Lead coordinates

Before approving:
☐ Tested in Dev successfully
☐ QA not running critical test cycle
☐ OutSystems extension updated (if schema change)
```

### Production Environment
```
Approval: Dev Lead + VP Engineering
Who Can Deploy: Dev Leads only (or DBA)
When: After hours only (7pm-7am) or weekends
Communication: Announce in #general 24 hours before

Approval checklist:
☐ Deployed and tested in Dev ✅
☐ Deployed and tested in Test ✅
☐ QA sign-off ✅
☐ Breaking changes communicated to teams ✅
☐ OutSystems apps updated and tested ✅
☐ Rollback plan documented ✅
☐ Dev lead available during deployment window ✅
☐ CAB (Change Advisory Board) approval ✅
```

---

## Part 6: Escalation Protocols

### When to Escalate to @db-platform-team

**You should escalate**:
```
🔴 Production database deployment failed
🔴 Data loss occurred
🔴 Multiple developers blocked (> 2 hours)
🔴 Security concern (credentials exposed, SQL injection)
🔴 Performance disaster (table locks, query timeouts)
🟡 Complex refactoring needed (table split, etc.)
🟡 Merge conflict involving refactorlog
🟡 SqlPackage deployment error you can't debug
🟡 Schema drift between environments
```

**You can handle**:
```
✅ PR reviews (your core responsibility)
✅ Simple build errors (missing comma, typo)
✅ Naming convention guidance
✅ Data type recommendations
✅ Index recommendations
✅ Pre/post script guidance (if you're familiar)
✅ Git merge conflicts (if comfortable)
```

### How to Escalate

**Slack**:
```
@db-platform-team - URGENT

Issue: [Brief description]
Environment: [Dev/Test/Prod]
Affected: [Number of developers blocked OR production impact]
Tried: [What you attempted]
Need: [Specific help needed]

[Attach error messages, screenshots]
```

**For Production Issues**:
```
Call the on-call phone: [NUMBER]
Post in #database-emergency
Page @db-on-call in Slack

Don't wait for response - call AND Slack
```

---

## Part 7: First Week Expectations

### Monday (Today)
```
7:00 AM  - Review this briefing ✅
9:00 AM  - Team standup
9:30 AM  - YOU present: DB workflow demo (15 min) ← CRITICAL
10:00 AM - Answer questions in #database-help
All Day  - Watch for PRs, review quickly
5:00 PM  - Debrief with other dev leads: What issues came up?
```

### Tuesday-Friday
```
Morning  - Check overnight deployments (if any)
All Day  - PR reviews (expect 3-5/day, decreasing over week)
All Day  - #database-help monitoring (expect 10-20 questions/day)
3:00 PM  - Quick sync with @db-platform-team (daily this week)
5:00 PM  - Hand off any open issues to next dev lead
```

### Week 2+
```
- PR reviews continue (now routine, 15 min/day)
- Questions decrease (developers more self-sufficient)
- Focus shifts to architecture and optimization
- Start reviewing Learning Path for skill building
```

---

## Part 8: Communication Templates

### PR Approval Comment
```
LGTM! ✅

Reviewed:
- Naming conventions ✅
- Data types appropriate ✅
- FK indexes present ✅
- Post-deployment script idempotent ✅

Notes:
- [Any specific feedback or suggestions]

Good work!
```

---

### PR Rejection Comment
```
Requesting changes before approval:

1. [Issue 1 - e.g., Missing index on CustomerId FK]
   - Add: CREATE INDEX IX_Orders_CustomerId...

2. [Issue 2 - e.g., Not idempotent]
   - Change INSERT to MERGE statement

3. [Issue 3]

Please update and re-request review. Happy to pair on this if helpful!
```

---

### Production Deployment Announcement (24 hours before)
```
📅 Scheduled Production Database Deployment

What: [Description, e.g., "Add PaymentMethod column to Orders"]
When: Saturday, [Date] at 11:00 PM PST
Duration: ~15 minutes
Downtime: None expected
Rollback: Available if issues

Impact:
- [OutSystems apps affected]
- [Expected changes to app behavior]

Testing:
- Deployed to Dev: ✅ [Date]
- Deployed to Test: ✅ [Date]
- QA Sign-off: ✅

Contact: @your-name (phone: [NUMBER])

Questions or concerns? Reply here by EOD Friday.
```

---

### Post-Deployment Confirmation
```
✅ Production Deployment Complete

Deployed: [Timestamp]
Status: Success
Duration: [Actual time]
Issues: None

Verification:
- Database schema updated ✅
- OutSystems extensions refreshed ✅
- Apps tested ✅
- Performance normal ✅

Monitoring for next 24 hours. Report any issues to #database-help.
```

---

## Part 9: Your Monday Morning Demo Script

**Goal**: Show the workflow once so everyone sees it.

**Time**: 15 minutes

**Script**:

```
1. Intro (1 min)
"Starting Monday, all database schema changes go through this new workflow. 
I'm going to demo it end-to-end. Then we'll do Q&A. You'll also have written 
guides, but seeing it once helps."

2. Make a Change (3 min)
[Screen share Visual Studio]
"I need to add a PhoneNumber column to Customers table."

- Open database project
- Navigate to Customers.sql
- Add: [PhoneNumber] VARCHAR(20) NULL,
- Build project (Ctrl+Shift+B)
- Show: "Build succeeded"

"Notice I didn't write ALTER TABLE - I edited the CREATE TABLE. 
DACPAC figures out the ALTER."

3. Commit and PR (3 min)
[Show Team Explorer]
- Commit changes
- Push to GitHub
- Create PR
- Show PR in browser

"Now I wait for dev lead review. If approved, I can deploy."

4. Deploy to Dev (2 min)
[Back to Visual Studio]
- Right-click project → Publish
- Select Dev database
- Click "Generate Script"
- Show ALTER TABLE statement
- Click "Publish"
- Show "Publish succeeded"

"Now the database has the column. But OutSystems doesn't know yet."

5. Refresh Integration Studio (3 min)
[Open Integration Studio]
- Open database extension
- Right-click Customers table → Refresh Table
- Show new PhoneNumber attribute
- Publish extension

"Now OutSystems knows about the column."

6. Use in Service Studio (2 min)
[Open Service Studio]
- Manage Dependencies
- Show PhoneNumber attribute now available
- Add to a form widget
- Publish

"And now the app can use it."

7. Q&A (1 min)
"Questions? Also see [link to docs] for step-by-step guides. 
Try your first change today - we're here to help!"
```

---

## Part 10: Mental Model for This Week

### Your Goal
```
Not: Become database expert
IS:  Keep team unblocked and prevent disasters

Think of yourself as:
- Traffic cop (directing, not driving)
- Safety net (catch mistakes before production)
- Escalation point (know when to call for help)
```

### What Good Looks Like
```
End of Week 1:
✅ No production incidents
✅ Developers made schema changes successfully
✅ PR reviews average < 2 hours
✅ No one blocked > 4 hours
✅ Team confidence increasing

NOT expecting:
- Perfect execution (there will be bumps)
- Zero questions (questions are good!)
- Expert-level knowledge (you'll learn over time)
```

### Self-Care
```
- You will get more questions than usual this week
- That's expected and temporary
- Timebox responses (don't let one question take 2 hours)
- Escalate when stuck (don't struggle alone)
- Other dev leads are doing the same thing (peer support!)
```

---

## Resources for You

### Immediate Reference
- [START-HERE.md](./START-HERE.md) - Team orientation
- [Monday Morning Survival Guide](./Monday-Morning-Survival-Guide.md) - What you'll point developers to
- [OutSystems Workflow](./OutSystems-DB-Change-Workflow.md) - Integration Studio steps
- [Top 10 Anti-Patterns](./Top-10-Anti-Patterns.md) - What to watch for in PRs

### Your Deep Dives (When You Have Time)
- [Learning Path](./Learning-Path-Condensed.md) - Build your own expertise
- [Playbooks](./Playbooks/) - Specific scenarios

### Support
- **Slack**: #database-help (general), #dev-leads (peer support)
- **Daily Sync**: 3 PM with @db-platform-team (this week only)
- **On-Call**: @db-on-call for production emergencies
- **Phone**: [ON-CALL NUMBER] for urgent issues

---

## Final Checklist for This Weekend

Before Monday 9 AM:
- [ ] Read this entire briefing ✅
- [ ] Review PR checklist (Part 3)
- [ ] Skim Monday Morning Survival Guide
- [ ] Test: Create a simple database change in your own branch
- [ ] Test: Review your own PR (practice the checklist)
- [ ] Test: Deploy to Dev (so you've done it once)
- [ ] Prepare your Monday 9:30 AM demo
- [ ] Verify you have access to all needed tools (VS, SSMS, Integration Studio)
- [ ] Verify you can see PRs in GitHub/Azure DevOps
- [ ] Join #database-help Slack channel
- [ ] Add @db-platform-team and @db-on-call to your Slack favorites
- [ ] Have on-call phone number saved

Optional but helpful:
- [ ] Pair with another dev lead Sunday to walk through it together
- [ ] Do a practice demo for yourself (record it?)

---

**You've got this.** 

This is a big change, but it's a good one. Your team is experienced and capable. 
They'll adapt quickly. Your job is just to keep things moving smoothly this week.

We're all in this together.

Questions this weekend? Slack @db-platform-team or call [NUMBER].

---

**Last Updated**: [This Weekend]  
**Maintained By**: Database Platform Team
```

**Why This Works**:
- Specific to dev lead role (not developer)
- PR review checklist is actionable (not theoretical)
- Escalation protocols clear (when to handle vs escalate)
- Communication templates ready to copy-paste
- Monday demo script (so they can prepare)
- Realistic expectations (it's OK to not know everything)
- Support systems emphasized (they're not alone)

---

### 5. Top-10-Anti-Patterns.md (3 pages)

**Purpose**: Critical "don't do this" list. Prevent outages Monday morning.

**Structure**:

```markdown
# Top 10 Database Anti-Patterns (Don't Do These!)

> **Goal**: Prevent common mistakes that cause outages, data loss, or team pain.  
> **Read this first** before making any database changes.

---

## ❌ Anti-Pattern #1: Manual ALTER Statements in Project

**Don't Do This**:
```sql
-- In your .sql file:
ALTER TABLE [dbo].[Customers]
ADD [PhoneNumber] VARCHAR(20) NULL;
```

**Why It's Bad**:
- SSDT projects use CREATE statements, not ALTER
- ALTER in project file = build errors
- Confuses DACPAC deployment logic

**Do This Instead**:
```sql
-- Edit the CREATE TABLE statement:
CREATE TABLE [dbo].[Customers] (
    [CustomerId] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [FirstName] NVARCHAR(50) NOT NULL,
    [LastName] NVARCHAR(50) NOT NULL,
    [PhoneNumber] VARCHAR(20) NULL  -- ← Just add it here
);
```

**Why It Works**:
- DACPAC compares CREATE (desired state) vs actual database
- Generates ALTER automatically
- Always works, regardless of whether table exists

---

## ❌ Anti-Pattern #2: Forgetting Integration Studio Refresh

**Don't Do This**:
```
1. Add column to database ✅
2. Deploy to Dev ✅
3. Try to use column in OutSystems ❌ Error!
```

**Why It's Bad**:
- OutSystems caches database schema
- Doesn't auto-detect changes
- App breaks with "Invalid attribute" error

**Do This Instead**:
```
1. Add column to database ✅
2. Deploy to Dev ✅
3. Open Integration Studio
4. Refresh table
5. Publish extension ✅
6. NOW use in OutSystems ✅
```

**Impact if Forgotten**:
- Dev: Blocks you
- Test: Blocks QA
- Prod: **OUTAGE** (app crashes)

**See**: [OutSystems Workflow Guide](./OutSystems-DB-Change-Workflow.md)

---

## ❌ Anti-Pattern #3: Non-Idempotent Post-Deployment Scripts

**Don't Do This**:
```sql
-- Post-deployment script:
INSERT INTO OrderStatus (StatusId, StatusName)
VALUES (1, 'Pending'), (2, 'Processing');
```

**Why It's Bad**:
- Post-deployment scripts run EVERY deployment
- Second deployment = duplicate rows
- Violates primary key = deployment fails

**Do This Instead**:
```sql
-- Use MERGE (idempotent):
MERGE INTO OrderStatus AS Target
USING (VALUES
    (1, 'Pending'),
    (2, 'Processing')
) AS Source (StatusId, StatusName)
ON Target.StatusId = Source.StatusId
WHEN NOT MATCHED THEN
    INSERT VALUES (Source.StatusId, Source.StatusName);
```

**Or**:
```sql
-- Use IF NOT EXISTS:
IF NOT EXISTS (SELECT 1 FROM OrderStatus WHERE StatusId = 1)
BEGIN
    INSERT INTO OrderStatus VALUES (1, 'Pending');
END
```

**Why It Works**:
- Safe to run multiple times
- Won't duplicate data
- Won't fail on re-deployment

---

## ❌ Anti-Pattern #4: SELECT * in Views/Procedures

**Don't Do This**:
```sql
CREATE VIEW vw_CustomerOrders AS
SELECT * FROM Customers c
INNER JOIN Orders o ON c.CustomerId = o.CustomerId;
```

**Why It's Bad**:
- Breaks when columns added (app expects specific columns)
- Security risk (exposes all columns, even sensitive ones)
- Performance impact (returns unneeded data)
- OutSystems generates SELECT * queries that break

**Do This Instead**:
```sql
CREATE VIEW vw_CustomerOrders AS
SELECT 
    c.CustomerId,
    c.FirstName,
    c.LastName,
    o.OrderId,
    o.OrderDate,
    o.TotalAmount
FROM Customers c
INNER JOIN Orders o ON c.CustomerId = o.CustomerId;
```

**Why It Works**:
- Explicit column list = predictable schema
- Add new columns without breaking consumers
- Optimize by only returning needed data

---

## ❌ Anti-Pattern #5: Foreign Keys Without Indexes

**Don't Do This**:
```sql
CREATE TABLE [dbo].[Orders] (
    [OrderId] INT PRIMARY KEY,
    [CustomerId] INT NOT NULL,
    CONSTRAINT FK_Orders_Customers 
        FOREIGN KEY (CustomerId) 
        REFERENCES Customers(CustomerId)
);
-- ← Missing index on CustomerId!
```

**Why It's Bad**:
- Queries on FK are slow (table scan)
- Updates to parent table lock child table
- OutSystems generates lots of FK-based queries
- Production performance degrades over time

**Do This Instead**:
```sql
CREATE TABLE [dbo].[Orders] (
    [OrderId] INT PRIMARY KEY,
    [CustomerId] INT NOT NULL,
    CONSTRAINT FK_Orders_Customers 
        FOREIGN KEY (CustomerId) 
        REFERENCES Customers(CustomerId)
);
GO

CREATE NONCLUSTERED INDEX IX_Orders_CustomerId
    ON Orders(CustomerId);  -- ← Add this!
GO
```

**Rule**: Every FK column needs an index (with rare exceptions)

---

## ❌ Anti-Pattern #6: VARCHAR for User-Facing Text

**Don't Do This**:
```sql
CREATE TABLE Customers (
    [FirstName] VARCHAR(50),  -- ❌
    [LastName] VARCHAR(50),   -- ❌
    [City] VARCHAR(50)        -- ❌
);
```

**Why It's Bad**:
- VARCHAR = ASCII only
- Breaks with international names (José, François, 李明)
- Data corruption or rejection
- User complaints

**Do This Instead**:
```sql
CREATE TABLE Customers (
    [FirstName] NVARCHAR(50),  -- ✅ Supports Unicode
    [LastName] NVARCHAR(50),   -- ✅
    [City] NVARCHAR(50)        -- ✅
);
```

**When VARCHAR is OK**:
- Internal codes: OrderStatus VARCHAR(20) = 'Pending', 'Shipped'
- Email addresses (ASCII-only)
- URLs

**Rule**: User-entered text = NVARCHAR. System codes = VARCHAR.

---

## ❌ Anti-Pattern #7: Deploying Without Building First

**Don't Do This**:
```
1. Edit .sql file
2. Commit to Git
3. Push
4. PR merged
5. CI/CD deploys ❌ FAILS!
```

**Why It's Bad**:
- Syntax errors go undetected
- Breaks CI/CD pipeline
- Blocks other developers
- Wastes time debugging on server

**Do This Instead**:
```
1. Edit .sql file
2. Build project (Ctrl+Shift+B) ← Critical!
3. Fix any errors
4. Build again until clean
5. THEN commit
```

**Why It Works**:
- Catches 90% of errors locally
- CI/CD becomes safety net, not first test
- Faster feedback loop

**Rule**: Green build = can commit. Red build = don't commit.

---

## ❌ Anti-Pattern #8: Renaming Without Refactorlog

**Don't Do This**:
```sql
-- Old: [Products]
-- Manual change in .sql file:
CREATE TABLE [dbo].[Product] (  -- Just renamed it
    ...
);
```

**Why It's Bad**:
- DACPAC sees: Drop [Products], Create [Product]
- **DATA LOST** when deployed
- No warning (they're different tables to DACPAC)

**Do This Instead**:
```
1. Right-click [Products] in .sql file
2. Refactor → Rename
3. New name: "Product"
4. Preview changes (shows all affected objects)
5. Apply
```

**Why It Works**:
- Creates refactorlog entry
- DACPAC uses sp_rename (preserves data)
- Updates all references automatically

**Rule**: NEVER manually rename. Always use Refactor menu.

---

## ❌ Anti-Pattern #9: Deploying to Prod Without Testing

**Don't Do This**:
```
1. Make change
2. Deploy to Dev
3. "Looks good!"
4. Deploy straight to Prod ❌
```

**Why It's Bad**:
- Dev has sample data, Prod has real data (different behavior)
- Missed edge cases
- No QA validation
- Production outage

**Do This Instead**:
```
1. Make change
2. Deploy to Dev
3. Test with OutSystems app
4. Deploy to Test
5. QA tests
6. Get approval
7. Deploy to Prod after hours
```

**Why It Works**:
- Each environment catches different issues
- QA validates business logic
- Timing reduces user impact

**Rule**: Dev → Test → Prod. No skipping.

---

## ❌ Anti-Pattern #10: NOT NULL on Existing Table Without DEFAULT

**Don't Do This**:
```sql
-- Existing table with 1M rows
CREATE TABLE [dbo].[Customers] (
    [CustomerId] INT PRIMARY KEY,
    [FirstName] NVARCHAR(50),
    [PhoneNumber] VARCHAR(20) NOT NULL  -- ← New column, NOT NULL
);
```

**Why It's Bad**:
- Existing rows have NULL for PhoneNumber (doesn't exist yet)
- ALTER TABLE ADD ... NOT NULL fails
- Deployment fails
- Database unchanged, app broken

**Do This Instead**:

**Option 1**: Nullable first
```sql
[PhoneNumber] VARCHAR(20) NULL  -- Add as nullable
```
Then later (after data populated):
```sql
ALTER TABLE Customers 
ALTER COLUMN PhoneNumber VARCHAR(20) NOT NULL;
```

**Option 2**: Provide DEFAULT
```sql
[PhoneNumber] VARCHAR(20) NOT NULL DEFAULT ''  -- Empty string default
```

**Option 3**: Data migration script
```sql
-- Pre-deployment: Set default value
UPDATE Customers SET PhoneNumber = '' WHERE PhoneNumber IS NULL;

-- Then change to NOT NULL
```

**Rule**: Adding NOT NULL column to existing table = needs DEFAULT or migration.

---

## Quick Checklist: Before Committing

Use this to avoid all 10 anti-patterns:

- [ ] Project builds successfully (Ctrl+Shift+B)
- [ ] All FK columns have indexes
- [ ] User-facing text uses NVARCHAR (not VARCHAR)
- [ ] Post-deployment scripts use MERGE or IF NOT EXISTS
- [ ] No SELECT * in views/procedures
- [ ] Renames used Refactor menu (not manual edit)
- [ ] NOT NULL columns have DEFAULT (if table has data)
- [ ] Tested deployment script (Generate Script, review ALTER statements)
- [ ] Will refresh Integration Studio after deploying
- [ ] Planned to test in Dev before Test/Prod

---

## Dev Lead: Watch For These in PRs

**Priority Anti-Patterns** (block the PR):
- ❌ #2: Forgetting Integration Studio refresh (ask if they know to do this)
- ❌ #3: Non-idempotent scripts (request MERGE)
- ❌ #5: FKs without indexes (request index)
- ❌ #8: Manual renames (potential data loss)
- ❌ #10: NOT NULL without DEFAULT (deployment will fail)

**Secondary Anti-Patterns** (comment for improvement):
- #1: ALTER statements (explain CREATE approach)
- #4: SELECT * (request explicit columns)
- #6: VARCHAR for names (suggest NVARCHAR)

---

## Recovery: If You Already Did One of These

### Did #1 (ALTER statement in project)?
**Fix**: Delete the ALTER, edit the CREATE TABLE instead. Build again.

### Did #2 (Deployed without refreshing Integration Studio)?
**Fix**: Refresh now. Publish extension. Test app.

### Did #3 (Non-idempotent script ran twice)?
**Fix**: Clean up duplicate data manually (DELETE duplicates). Change script to MERGE for next time.

### Did #8 (Manual rename, deployed, lost data)?
**Fix**: URGENT - Restore from backup. Then use Refactor menu for rename.

### Did #10 (NOT NULL without DEFAULT, deployment failed)?
**Fix**: Change to NULL or add DEFAULT. Redeploy.

---

**Questions?**
- Slack: #database-help
- Dev Lead: Your dev lead for guidance
- Emergency: @db-on-call

**Remember**: Mistakes happen. The goal is to learn and prevent them from reaching production.

---

**Last Updated**: [This Weekend]  
**Maintained By**: Database Platform Team
```

**Why This Works**:
- Immediately actionable ("don't do this, do this instead")
- Shows impact (why it matters)
- Real examples (not abstract)
- Recovery procedures (when mistakes happen)
- Prioritized for dev leads (what to watch in PRs)

---

## Summary of Critical Path

**These 5 documents (18 pages total) are what you need to write THIS WEEKEND**:

1. **START-HERE.md** (2 pages) - Orientation, navigation
2. **Monday-Morning-Survival-Guide.md** (5 pages) - Get through Monday
3. **OutSystems-DB-Change-Workflow.md** (3 pages) - Integration Studio flow
4. **Dev-Lead-Weekend-Briefing.md** (5 pages) - Dev lead responsibilities
5. **Top-10-Anti-Patterns.md** (3 pages) - Common mistakes

**Goal**: No one grinds to a halt Monday morning. 

**Next message**: I'll plan out the remaining artifacts (Week 1, Month 1, Long-term) in full detail, including playbooks and quick-win guides. Would you like me to proceed with that now?
