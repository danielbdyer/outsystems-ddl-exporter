# Proving-path runbook — Windows, Visual Studio, a local SQL Server

This runbook stands up the one thing the ssdt-agent workflow cannot work without: a local SQL
Server that an agent can publish a disposable copy of the schema to, pointed at real-shaped data.
The workflow proves a change by publishing it and reading what the deployment engine does. If that
publish cannot run on the developer's machine, the agent has nothing to read, and the workflow
stops. This is the highest prerequisite for the cutover week — set it up and verify it before the
team leans on the tool.

Everything here runs on a Windows machine with Visual Studio, which is where the team works. The
commands are PowerShell.

## What you are setting up

Four pieces, on each developer's machine:

1. A local SQL Server engine to publish against.
2. `sqlpackage`, the command-line tool that publishes a dacpac.
3. A copy of real Dev data, restored locally, so proving happens against real-shaped rows rather
   than a toy.
4. A publish profile that points only at the local copy.

Once those exist, the proving loop the skills describe runs unchanged, against your real schema
instead of the sample.

## Step 1 — A local SQL Server engine

The simplest option is **SQL Server Express LocalDB**, which installs with Visual Studio's data
tooling. Create and start an instance:

```powershell
sqllocaldb create MSSQLLocalDB
sqllocaldb start MSSQLLocalDB
```

Its connection target is `Server=(localdb)\MSSQLLocalDB`.

LocalDB is enough to prove schema publishes against real-shaped data, and it is the least trouble
to install. If you want behavior closer to production — larger data, features LocalDB omits — use
**SQL Server Developer edition** instead; it is free for non-production use and behaves like the
real engine. Whichever you choose, it must be **local and disposable**: this database is thrown
away and rebuilt, and no publish in this loop ever points at a shared or real environment.

## Step 2 — sqlpackage

`sqlpackage` may already be on the machine through Visual Studio's SQL tooling. Check:

```powershell
sqlpackage /version
```

If it is missing, install it as a .NET tool (the same tool this repository's proving loop uses):

```powershell
dotnet tool install --global microsoft.sqlpackage
```

**Pin the version to the pipeline's.** The deployment guard behaves differently across engine
versions — in particular, whether a new foreign key, check, or unique constraint ends up trusted
(re-validated against existing rows) depends on the version. Record the version your Azure DevOps
pipeline runs, and install the matching `sqlpackage`, so a proof on the developer's machine matches
what production will do. Write both versions into `estate/toolchain.md` in this tree. Until they
match, treat any "the constraint is trusted" finding as assumed, and confirm it with the check in
step 6.

## Step 3 — Real-shaped data

The point of proving is to see what the engine does with the actual rows, so the local database
needs real-shaped data, not the sample seed.

- The most faithful copy is a **restore of a Dev backup**. Restore a recent `.bak` of the Dev
  database into your local engine.
- If the Dev data is sensitive, restore a **sanitized copy** — real shapes and volumes, masked
  values. The proving loop cares about row counts, nulls, duplicates, orphans, and lengths, not the
  literal values, so masking does not weaken the proof as long as it preserves those shapes.

Restore, against LocalDB, looks like:

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -Q "RESTORE DATABASE [ProvingCopy] FROM DISK = N'C:\path\to\Dev.bak' WITH MOVE 'Dev' TO N'C:\SqlData\ProvingCopy.mdf', MOVE 'Dev_log' TO N'C:\SqlData\ProvingCopy_log.ldf', REPLACE"
```

The logical file names (`Dev`, `Dev_log`) and paths are yours to fill from the backup. This
`ProvingCopy` database is the disposable copy the loop publishes against; rebuild it whenever you
want a clean slate.

## Step 4 — A local publish profile

Create a publish profile that points only at the local copy. It mirrors this tree's Strict profile
(`proving-ground/profiles/ProvingGround.Strict.publish.xml`), with the connection string changed to
the local engine. Save it beside your SSDT project as `Local.Strict.publish.xml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="Current" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <TargetConnectionString>Server=(localdb)\MSSQLLocalDB;Initial Catalog=ProvingCopy;Integrated Security=True;TrustServerCertificate=True</TargetConnectionString>
    <BlockOnPossibleDataLoss>True</BlockOnPossibleDataLoss>
    <GenerateSmartDefaults>False</GenerateSmartDefaults>
    <IgnoreColumnOrder>True</IgnoreColumnOrder>
    <DropObjectsNotInSource>True</DropObjectsNotInSource>
    <IncludeTransactionalScripts>True</IncludeTransactionalScripts>
    <AllowIncompatiblePlatform>False</AllowIncompatiblePlatform>
    <IgnorePermissions>True</IgnorePermissions>
  </PropertyGroup>
</Project>
```

`DropObjectsNotInSource` is `True` here, the diagnostic posture, which makes a would-be drop visible
on the disposable copy. For the deployment-shaped outcome, also keep a copy with
`DropObjectsNotInSource` set to `False` — the production posture. This tree's
`proving-ground/profiles/ProvingGround.Pipeline.publish.xml` explains the difference; the same two
profiles apply here, pointed at the local copy.

## Step 5 — The loop

Build the project to a dacpac, then publish it:

```powershell
dotnet build YourProject.sqlproj -c Release
sqlpackage /Action:Publish /SourceFile:bin\Release\YourProject.dacpac /Profile:Local.Strict.publish.xml
```

**Read the result from the text, not the exit code.** A blocked publish prints `Could not deploy
package.` with a `Msg` line; a clean one prints `Successfully published database.`. The block is
the finding, so the agent reads the printed lines, not the process exit status.

## Step 6 — Verify the substrate before you rely on it

Two checks confirm the substrate is real. Run them once on each machine before the week.

**The acceptance test — the make-mandatory block reproduces.** Take a table that holds rows and has
some blank values in a nullable column. Edit that column to `NOT NULL` in the project, rebuild, and
publish under the Strict profile. Expect it to be blocked:

```
Msg 50000, Level 16, State 127 — Rows were detected. The schema update is terminating because
data loss might occur.
```

Then clear every blank in that column on the copy and publish the same change again. Expect the
**same block** — the guard fires on whether the table holds rows, not on whether the column has
blanks. If both publishes block, the substrate reproduces the workflow's central finding, and the
proving loop is trustworthy on this machine.

**The constraint-trust check — close the version risk.** Add a foreign key to a table whose rows all
point at real parents, rebuild, and publish. Then read whether the constraint was trusted:

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d ProvingCopy -Q "SELECT name, is_not_trusted FROM sys.foreign_keys WHERE name = 'YOUR_FK_NAME'"
```

`is_not_trusted = 0` means the engine re-validated the existing rows as the constraint went on —
the behavior the skills describe. `is_not_trusted = 1` means it did not: the constraint is on but
the existing rows were never checked, so a violating row would not have blocked the deploy. If you
see `1`, your engine version reads untrusted. Record it in `estate/toolchain.md`, and add to the
foreign-key and constraint records the step that re-validates the constraint after it is added, so
the safety the skills claim actually holds on your engine.

## When proving cannot run

If the substrate is not up — no local engine, the build fails, `sqlpackage` is missing — the agent
is instructed to stop and tell the developer, not to guess a classification from the SQL text. That
is deliberate: a guess is the failure the whole workflow exists to prevent. Fix the substrate, then
prove; do not route around it.
