# Rate: add a new Entity with full history (system-versioned from birth)

**In OutSystems** — You create a new Entity `Rate` and want *full point-in-time history* on it from the start: every version of every row kept, so you can ask "what did this rate look like last Tuesday?"
**In SSDT** — a new file `Tables/dbo.Rate.sql` holds a single system-versioned `CREATE TABLE`: two `DATETIME2 GENERATED ALWAYS AS ROW START / ROW END` period columns, a `PERIOD FOR SYSTEM_TIME`, and `WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[Rate_History]))`. SQL Server creates the paired history table and maintains it on every write.

## Summary

You add a brand-new `Rate` Entity with system versioning turned on from the start. Because the entity is
**new**, no existing data is touched — SQL Server creates the main table, its paired `Rate_History`
table, and the two period columns in one clean publish. Temporal versioning *is* expressible
declaratively for a new table, so the change ships as a single schema change with nothing to transition
and nothing to backfill. This was proven objectively against a Twin — a disposable SQL Server database
published from this estate and filled with real-shaped synthetic data — with a **production-faithful**
publish (`BlockOnPossibleDataLoss = true`, the deployment a real environment runs). No work item was
provided with the request; attach one before merge so the record is traceable.

**Settle one thing at intake first: which kind of "history" do you want?** "Keep history" covers two
different mechanisms. *Point-in-time row history* — what a row looked like at any past moment — is
temporal versioning, and it is what this change delivers. A *row-level change feed* — a stream of
old→new values pushed to a downstream system — is a different mechanism handled outside this agent.
Standing up system versioning when a change feed was wanted (or the reverse) is a design error, cheapest
to catch before building. This PR builds point-in-time history.

**It is also a design commitment.** System versioning pairs the table with a history table that grows
with every update, forever, unless a retention policy is set. That is the feature working as intended,
but it is a standing cost worth a deliberate decision — hence a heavier reviewer than a plain new table,
even though no existing data is at risk.

## Review & release

- A dev lead or an experienced developer should review this: turning on system versioning is a design
  commitment — a paired history table that grows with every update. No existing data is touched; the
  entity is new.
- It ships as a single schema change, applied in place: SSDT publishes the system-versioned CREATE — the
  table, its paired history table, and the two `GENERATED ALWAYS AS ROW START / ROW END` period columns —
  verbatim. No existing data is read or written.
- The whole thing is one `CREATE TABLE` statement in one estate file; the history table `Rate_History` is
  created automatically by SQL Server from the `HISTORY_TABLE = [dbo].[Rate_History]` clause — you do not
  author it separately.
- Added scrutiny: this is the first system-versioned table on the estate (no prior proof here before
  this run), and if `Rate` is expected to reach large row counts the history table's growth and the
  versioning write overhead deserve a retention plan.

## Changes

| File | Change |
|---|---|
| `Tables/dbo.Rate.sql` | Adds a system-versioned `CREATE TABLE [dbo].[Rate]` — `[Id]` IDENTITY primary key, `[Code]` / `[Amount]` columns, `[ValidFrom]` / `[ValidTo]` `GENERATED ALWAYS` period columns, `PERIOD FOR SYSTEM_TIME`, and `SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[Rate_History])` |

No renames (the refactorlog is unchanged). No existing table, index, view, or procedure is touched — the
only objects added are the new `Rate` table, its constraints, and the auto-created `Rate_History` table.

## Data remediation

None. Both the main table and its history table are created empty; there is no existing data to fill,
reconcile, or backfill, and no existing row anywhere is read or written. (Turning versioning on over an
**existing populated** table is a different operation — the period columns must be backfilled first and
the change stages across releases — routed to `temporal-convert`. This PR is the greenfield case.)

## Deployment evidence — objective proof, production-faithful publish, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, materializes
real-shaped data in the existing tables, adds the system-versioned table as its own estate file, and
asserts the outcome under a **production-faithful** DacFx posture (`BlockOnPossibleDataLoss = true`, no
smart-defaults). DacFx is the same publish engine `sqlpackage` wraps.

**Test:** `Twin.Tests.Integration.SamplePrCleanApply2Tests+SamplePrCleanApply2Tests.temporal-new: a new system-versioned entity applies clean with its history table`

```
Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 1 m 16 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact — the system-versioned table publishes clean, its history table is created and linked, and nothing existing is touched.**
The three existing tables held **25 rows** each and `Rate` did not exist. The production-faithful publish
of the system-versioned `CREATE TABLE` was **accepted**; `Rate` came up as a system-versioned table
(`temporal_type = 2`) with two `GENERATED ALWAYS` period columns, and SQL Server created and linked the
`Rate_History` table (`temporal_type = 1`). Verbatim from the run:

```
baseline: Rate exists=0; existing rows Customer=25, Order=25, OrderLine=25
production publish (BlockOnPossibleDataLoss=true) CREATE system-versioned TABLE [dbo].[Rate] (SYSTEM_VERSIONING = ON, HISTORY_TABLE = [dbo].[Rate_History]): APPLIED (Ok)
  after apply: Rate exists=1, temporal_type=2 (2 = SYSTEM_VERSIONED_TEMPORAL_TABLE), PK_Rate exists=1, GENERATED-ALWAYS period columns=2
  history: Rate_History exists=1, temporal_type=1 (1 = HISTORY_TABLE), linked history table name=Rate_History
  existing rows intact: Customer=25, Order=25, OrderLine=25
```

The publish returned `Ok` under the production-faithful posture — a new system-versioned table is a clean
additive change with no data condition to violate. After the apply: `Rate` is
`SYSTEM_VERSIONED_TEMPORAL_TABLE` (`temporal_type = 2`), its primary key landed, both period columns are
`GENERATED ALWAYS` (`period columns = 2`), the history table `Rate_History` exists and is linked as
`Rate`'s history (`temporal_type = 1`, linked name `Rate_History`), and every existing table is unchanged
(**25 → 25** each). Confirmed on `mcr.microsoft.com/mssql/server:2022-latest`.

## Verification — run in each environment after deployment

```sql
-- expect 1 row, temporal_type_desc = SYSTEM_VERSIONED_TEMPORAL_TABLE, history_table = Rate_History
SELECT t.name, t.temporal_type_desc, h.name AS history_table
FROM sys.tables t
LEFT JOIN sys.tables h ON h.object_id = t.history_table_id
WHERE t.object_id = OBJECT_ID('dbo.Rate');

-- expect 2 rows: the period columns, GENERATED ALWAYS AS ROW START and ROW END
SELECT c.name, c.generated_always_type_desc
FROM sys.columns c
WHERE c.object_id = OBJECT_ID('dbo.Rate') AND c.generated_always_type <> 0;
```

## Rollback

Remove the `Tables/dbo.Rate.sql` file from the project and republish. A system-versioned table cannot be
dropped directly: the generated delta sets `SYSTEM_VERSIONING = OFF` first (which unlinks the history
table), then drops the main table and its history table. Lossless **only while both tables are unwritten**
— they are created empty, so until the application writes rows into `Rate`, dropping the pair discards
nothing. Once rows are written, dropping the pair discards the current rows and their accumulated history.
Backing the change out was not exercised.

## Not verified

- **Application impact.** A new entity nothing yet reads or writes does not change existing behaviour, but
  the application code that will query history (`FOR SYSTEM_TIME AS OF ...`) is new and is not exercised
  by the disposable copy — the application owner owns it.
- **Design intent — history vs change feed.** The copy proves the system-versioned table publishes clean;
  it cannot confirm that *point-in-time history*, and not a *row-level change feed*, is what the use case
  needs. That is a design confirmation owed at intake (see Summary), not something the copy can settle.
- **History growth, retention, and production timing.** The history table grows with every update and has
  no cleanup unless a retention policy is set; whether one is configured, and the versioning write
  overhead at production volumes, is not shown by the small copy.
- **Reversibility.** Only the forward create is proven; once the application writes rows, dropping the
  table and its history is lossy (see Rollback).
