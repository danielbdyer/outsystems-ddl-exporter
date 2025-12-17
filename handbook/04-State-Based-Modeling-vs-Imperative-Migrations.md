# 4. State-Based Modeling vs. Imperative Migrations

---

## The Core Idea

In OutSystems, when you changed an entity, you were implicitly saying: *"Here's what I want it to become."* OutSystems figured out how to get there.

SSDT works the same way — but makes it explicit.

**Imperative approach (what you might expect):**
```sql
-- "Do these steps in this order"
ALTER TABLE dbo.Person ADD MiddleName NVARCHAR(50) NULL;
ALTER TABLE dbo.Person ALTER COLUMN Email NVARCHAR(200);
ALTER TABLE dbo.Person DROP COLUMN LegacyId;
```

**Declarative approach (what SSDT actually does):**
```sql
-- "Here's what the table should look like"
CREATE TABLE [dbo].[Person]
(
    PersonId INT IDENTITY(1,1) NOT NULL,
    FirstName NVARCHAR(100) NOT NULL,
    MiddleName NVARCHAR(50) NULL,           -- Added
    LastName NVARCHAR(100) NOT NULL,
    Email NVARCHAR(200) NOT NULL,           -- Widened
    -- LegacyId removed
    CONSTRAINT [PK_Person] PRIMARY KEY CLUSTERED (PersonId)
)
```

You write the second version. SSDT compares it to the target database and generates the first version automatically.

---

## Why This Matters

### You Describe End State, Not Transitions

Your `.sql` files aren't scripts to run. They're declarations of what the schema *should be*.

When you edit a table definition:
- You're not writing an ALTER statement
- You're changing the declared end state
- SSDT computes the delta between current and desired
- SSDT generates whatever DDL is needed to close the gap

**Practical implication:** Stop thinking "what command do I run?" Start thinking "what should this table look like when I'm done?"

### The .sql File IS the Schema

In imperative migration systems (like Entity Framework migrations or Flyway), you have:
- Migration files: the steps to get from version N to version N+1
- Maybe a snapshot: what the schema looks like now

In SSDT, you have:
- Table definitions: what each table looks like, period
- The history lives in git, not in the schema itself

**Your `dbo.Person.sql` file is the source of truth.** It represents the current desired state of that table. Git history shows how it evolved.

### SSDT Computes the Delta

When you deploy, SSDT:

1. Reads your project (desired state)
2. Connects to target database (current state)
3. Compares them
4. Generates a deployment script (the delta)
5. Executes that script

```
┌─────────────────┐      ┌─────────────────┐
│  SSDT Project   │      │ Target Database │
│  (desired)      │      │ (current)       │
└────────┬────────┘      └────────┬────────┘
         │                        │
         │    ┌───────────────┐   │
         └───►│   Compare     │◄──┘
              └───────┬───────┘
                      │
                      ▼
              ┌───────────────┐
              │ Generated     │
              │ Deploy Script │
              │ (the delta)   │
              └───────┬───────┘
                      │
                      ▼
              ┌───────────────┐
              │ Execute       │
              │ (database     │
              │  transformed) │
              └───────────────┘
```

**This means:** The same project deployed to different databases generates different scripts. A fresh database gets `CREATE TABLE`. An existing database with the table gets `ALTER TABLE` (or nothing, if it matches).

---

## What You Do vs. What SSDT Generates

| You do this... | SSDT generates this... |
|----------------|------------------------|
| Add a column to table definition | `ALTER TABLE ... ADD ...` |
| Remove a column from table definition | `ALTER TABLE ... DROP COLUMN ...` |
| Change column type in definition | `ALTER TABLE ... ALTER COLUMN ...` |
| Create new table file | `CREATE TABLE ...` |
| Delete table file | `DROP TABLE ...` (if `DropObjectsNotInSource=True`) |
| Rename via refactorlog | `EXEC sp_rename ...` |
| Add constraint to definition | `ALTER TABLE ... ADD CONSTRAINT ...` |

You never write ALTER statements. You edit declarations. SSDT translates.

---

## When the Abstraction Leaks

The declarative model is powerful but not omniscient. SSDT's generated script is *correct* but not always *optimal* or *safe*.

### SSDT Doesn't Know Your Data

SSDT sees schema, not rows. It will happily generate:

```sql
ALTER TABLE dbo.Person ALTER COLUMN Email NVARCHAR(50) NOT NULL
```

It doesn't know that you have 10,000 rows with emails longer than 50 characters, or 500 rows with NULL emails.

**Your job:** Validate data fits before deploying changes that constrain it.

### SSDT Doesn't Know Your Intentions

If you rename a column by editing the file directly:

```sql
-- Before
FirstName NVARCHAR(100)

-- After (you just changed the text)
GivenName NVARCHAR(100)
```

SSDT sees: "FirstName is gone. GivenName is new."

SSDT generates:
```sql
ALTER TABLE dbo.Person DROP COLUMN FirstName
ALTER TABLE dbo.Person ADD GivenName NVARCHAR(100)
```

Data in FirstName? Gone.

**Your job:** Use the refactorlog for renames so SSDT knows it's identity-preserving, not drop-and-create.

### SSDT Optimizes for Correctness, Not Performance

SSDT will generate a working script, but not necessarily the fastest one. For example:

- Adding a NOT NULL column with a default might do it in a way that rewrites the whole table
- Changing a clustered index might rebuild everything
- Reordering columns (if `IgnoreColumnOrder=False`) triggers a full table rebuild

**Your job:** Review generated scripts, especially for large tables. Know when to override with manual scripts.

---

## The Review Discipline

Because SSDT generates scripts from your declarations, you must review what it generates before deploying to production.

**For Tier 1 changes:** Skim the generated script. Verify it's doing what you expect.

**For Tier 2+ changes:** Read carefully. Check for:
- Unexpected DROP statements
- Table rebuilds (look for temp table creation)
- Large data movements
- Constraint validations on big tables

**How to see the generated script:**

1. **In Visual Studio:** Right-click project → Schema Compare → compare to target → view script
2. **In pipeline:** Most SSDT pipelines save the generated script as an artifact
3. **Using SqlPackage:** `SqlPackage /Action:Script /SourceFile:project.dacpac /TargetConnectionString:...`

**The rule:** Never deploy to production without reviewing the generated script. The abstraction is not trustworthy without inspection.

---

## Mental Model Summary

| Imperative (migrations) | Declarative (SSDT) |
|-------------------------|---------------------|
| Write migration scripts | Edit table definitions |
| Migrations accumulate forever | Current state is the only truth |
| Order of migrations matters | Order doesn't matter — just the end state |
| Rollback = write reverse migration | Rollback = restore previous definition (SSDT computes reverse) |
| You control exactly what runs | You control the outcome; SSDT controls the path |
| Risk: migrations diverge from reality | Risk: generated script surprises you |

**The shift:** You stop thinking "what steps do I take?" and start thinking "what should exist when I'm done?"

---

## Connecting to What You Know

If OutSystems's 1-Click Publish felt like magic, SSDT is the same magic made visible.

OutSystems compared your model to the database and made changes. You just didn't see the comparison or the generated SQL.

SSDT does the same thing, but:
- You see the model (your `.sql` files)
- You can see the comparison (Schema Compare)
- You can see the generated SQL (deployment script)
- You control whether it runs (publish with review)

More visibility. More control. More responsibility.

---

