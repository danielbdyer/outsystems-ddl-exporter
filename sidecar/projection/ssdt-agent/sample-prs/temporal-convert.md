# Customer: turn on full history for an existing populated Entity (staged — the single publish is blocked; the period columns need a historical start)

**In OutSystems** — You want *full point-in-time history* on the existing `Customer` Entity, which **already
has data in production**: every version of every customer row kept, so you can ask "what did this customer look
like last Tuesday?"
**In SSDT** — `Tables/dbo.Customer.sql` gains two `DATETIME2 GENERATED ALWAYS AS ROW START / ROW END` period
columns, a `PERIOD FOR SYSTEM_TIME`, and `SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[Customer_History])`.
Unlike a brand-new table, the period columns must be **added to rows that already exist** — and that is what
turns a one-file edit into a staged change.

## Summary

This is the **existing-populated-table** cousin of `temporal-new`. On a *new* Entity, system versioning ships
in one clean publish because there is nothing to backfill. On an **existing populated** Entity, it does not:
the two `ROW START` / `ROW END` period columns are `NOT NULL` and have no default, so adding them to a table
that holds rows is exactly the kind of change a production publish refuses. This was proven objectively against
a **Twin** — a disposable SQL Server 2022 database published from this estate and filled with real-shaped
synthetic data — under a **production-faithful** publish (`BlockOnPossibleDataLoss = true`,
`GenerateSmartDefaults = false`, `DropObjectsNotInSource = false`).

**The discovered outcome: the single declarative publish was *blocked*, and the conversion ships *staged*.** The
production gate refused the one-shot conversion — the period columns have no default and the table holds rows,
so the row-presence guard fired. The change is delivered as the staged program the skill prescribes: **add the
period columns with a sensible *historical* start default, then enable system versioning** — which, on the
Twin, applied cleanly, backfilled every existing row's `ROW START` to the historical floor (not to conversion
time), created and linked the paired `Customer_History` table, and left every existing row's business data
byte-for-byte unchanged.

**Settle the design question first, exactly as for a new temporal table.** "Keep history" covers two different
mechanisms. *Point-in-time row history* — what a row looked like at any past moment — is temporal versioning,
and it is what this change delivers. A *row-level change feed* — old→new values pushed to a downstream system —
is a different mechanism handled outside this agent. And it is a **standing design commitment**: the paired
history table grows with every update, forever, unless a retention policy is set. That is the feature working as
intended, but it is a cost worth a deliberate decision.

## The named trap — the backfilled ROW START

Adding the period columns to a populated table needs a **sensible historical default for `ROW START`**, or every
existing row falsely claims to have *begun at conversion time* — which quietly corrupts the very history the
feature was turned on to keep. The single declarative publish supplies **no** default (that is *why* it blocks);
the staged remedy supplies one deliberately. On the Twin the `ROW START` default was set to `2020-01-01`, a
floor that predates the synthetic data, so every historical row carries a truthful "existed since at least 2020"
start rather than a false "born at go-live." **The exact start date is a data-owner decision** — the conversion
date, or a real historical date if the business tracks one (named under Not verified). The `ROW END` default,
by contrast, is not a choice: it must be the `DATETIME2` maximum (`9999-12-31 23:59:59.9999999`) for every
current row, or SQL Server's system-versioning consistency check rejects the switch.

## Review & release

- **A dev lead must review this.** Existing data is modified — the period columns are backfilled into every
  existing `Customer` row on a live table, and system versioning is a standing design commitment (a paired
  history table that grows with every update).
- **It ships staged, not in one publish.** The production gate blocks the single declarative conversion (proven
  below). It is delivered as: (a) add the two period columns **with a chosen historical `ROW START` default**
  and the `PERIOD FOR SYSTEM_TIME`, then (b) enable `SYSTEM_VERSIONING = ON` with the paired history table.
  On an **empty** `Customer` the whole thing would collapse to a single clean publish (the `temporal-new` case)
  — prove the table is empty first; here it holds 25 rows.
- Added scrutiny: this is the first temporal conversion on the estate; at production row counts the period-column
  backfill and the versioning switch may block writes or run long — schedule a window.

## Changes

| Object | Change |
|---|---|
| `dbo.Customer` (period columns) | Adds `[ValidFrom] DATETIME2 GENERATED ALWAYS AS ROW START NOT NULL` and `[ValidTo] DATETIME2 GENERATED ALWAYS AS ROW END NOT NULL`, each with a **default** (a chosen historical `ROW START`; the `DATETIME2` max for `ROW END`), plus `PERIOD FOR SYSTEM_TIME ([ValidFrom], [ValidTo])` |
| `dbo.Customer` (versioning) | `SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[Customer_History])` — SQL Server creates and maintains the paired `Customer_History` table |
| `dbo.Customer_History` (new) | Created and linked automatically by SQL Server from the `HISTORY_TABLE` clause; not authored separately |

No renames (the refactorlog is unchanged). No existing column is dropped or retyped — the existing rows' data is
untouched by the conversion (proven by a before/after content-hash).

## Data motion — the staged conversion

`Customer_History` is created empty. The period columns are added to the 25 existing rows and **backfilled by
their defaults**: `ValidFrom = 2020-01-01` (the chosen historical floor) and `ValidTo = 9999-12-31
23:59:59.9999999` (the required "current row" marker). The completeness gate is a before/after content-hash of
the existing rows' **business columns** (`Id, Name, Email, StatusId, CreatedOn`): the conversion must not alter
any existing value, only add the two period columns. On the Twin the business-data hash matched byte-for-byte
before and after, and every row's `ROW START` landed on the historical floor — so the existing rows are
provably intact and none falsely dated to conversion time.

## Deployment evidence — objective proof, production-faithful publish, live Twin (SQL Server 2022), 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, materializes real-shaped
`Customer` data (25 non-temporal rows), **reads the generated deploy delta**, attempts the single declarative
conversion under the production-faithful posture, then runs the staged remedy and consumes the data to assert
the outcome. The business columns are hashed (an order-sensitive `SHA2_256` over the `FOR XML RAW` projection),
so any altered existing value would shift the digest. DacFx is the same publish engine `sqlpackage` wraps.

**Test:** `Twin.Tests.Integration.SamplePrRebuildTests+SamplePrRebuildTests.temporal-convert: turning the existing populated Customer system-versioned — the true outcome under the production gate, existing rows preserved`

```
Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2, Duration: 51 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact 1 (primary) — the single declarative conversion is REFUSED on the populated table.** `Customer` held
**25 rows** and was non-temporal (`temporal_type = 0`, 0 period columns, no history table). The production-
faithful publish of the system-versioned `CREATE` was **blocked**: the period columns are `NOT NULL` with no
default, so DacFx raised `SQL72015` for each and then the row-presence guard terminated the deploy. Verbatim
from the run — the block, exactly as a real environment refuses it:

```
baseline: Customer temporal_type=0 (0 = NON_TEMPORAL), rows=25, GENERATED-ALWAYS period columns=0, Customer_History exists=0
  business-data digest=0DFF52BED9977DD7C27CD32F2C688F4C7D9852D26FA06DC7DF2915201D2724DF
production publish (BlockOnPossibleDataLoss=true) convert Customer to system-versioned: REFUSED (blocked) — ships as the staged add-columns-with-defaults / enable-versioning program
  strict refusal detail:
Could not deploy package.
Warning SQL72015: The column [dbo].[Customer].[ValidFrom] on table [dbo].[Customer] must be added, but the column has no default value and does not allow NULL values. If the table contains data, the ALTER script will not work. To avoid this issue you must either: add a default value to the column, mark it as allowing NULL values, or enable the generation of smart-defaults as a deployment option.
Warning SQL72015: The column [dbo].[Customer].[ValidTo] on table [dbo].[Customer] must be added, but the column has no default value and does not allow NULL values. If the table contains data, the ALTER script will not work. To avoid this issue you must either: add a default value to the column, mark it as allowing NULL values, or enable the generation of smart-defaults as a deployment option.
Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 50000, Level 16, State 127, Line 8 Rows were detected. The schema update is terminating because data loss might occur.
Error SQL72045: Script execution error.  The executed script:
IF EXISTS (SELECT TOP 1 1
           FROM   [dbo].[Customer])
    RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127)
        WITH NOWAIT;
```

The generated delta reveals *why* this cannot be a one-shot: DacFx actually planned a **full table rebuild**
through a `tmp_ms_xx_Customer` shadow table (using `SET IDENTITY_INSERT ... ON` to preserve `Customer`'s
identity keys, then `SET (SYSTEM_VERSIONING = ON ...)`), but it placed the row-presence guard **above** the
rebuild — so under `BlockOnPossibleDataLoss = true` the deploy stops before it runs. `SQL72015` names exactly
the missing piece: the period columns have **no default value**, which is the `ROW START` backfill decision the
staged remedy makes explicit.

**Fact 2 (primary) — the staged conversion applies, backfills a historical start, and preserves every row.**
Run as the two scripted steps a real migration runs — add the period columns *with defaults*, then enable
versioning:

```sql
ALTER TABLE [dbo].[Customer] ADD
    [ValidFrom] DATETIME2 GENERATED ALWAYS AS ROW START NOT NULL CONSTRAINT [DF_Customer_ValidFrom] DEFAULT (CONVERT(DATETIME2, '2020-01-01T00:00:00.0000000')),
    [ValidTo]   DATETIME2 GENERATED ALWAYS AS ROW END   NOT NULL CONSTRAINT [DF_Customer_ValidTo]   DEFAULT (CONVERT(DATETIME2, '9999-12-31T23:59:59.9999999')),
    PERIOD FOR SYSTEM_TIME ([ValidFrom], [ValidTo]);
ALTER TABLE [dbo].[Customer] SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[Customer_History]));
```

After the staged apply: `Customer` is `SYSTEM_VERSIONED_TEMPORAL_TABLE` (`temporal_type = 2`) with **two**
`GENERATED ALWAYS` period columns, the paired `Customer_History` exists and is linked (`temporal_type = 1`),
all **25 rows** survived, the business-data digest is **byte-for-byte identical to before**, and every existing
row's `ROW START` is the historical floor `2020-01-01` — not conversion time. Verbatim from the run:

```
staged remedy ran (the gate blocked the declarative form): period columns added with ValidFrom default 2020-01-01 (a sane historical floor) and ValidTo the datetime2 max; SYSTEM_VERSIONING enabled with HISTORY_TABLE = Customer_History
phase 1 result (staged scripted conversion (ADD period columns with historical defaults, then SET SYSTEM_VERSIONING = ON)):
  Customer temporal_type=2 (2 = SYSTEM_VERSIONED_TEMPORAL_TABLE), rows=25 (was 25), GENERATED-ALWAYS period columns=2
  history: Customer_History exists=1, temporal_type=1 (1 = HISTORY_TABLE), linked history table name=Customer_History
  existing rows preserved: business-data digest=0DFF52BED9977DD7C27CD32F2C688F4C7D9852D26FA06DC7DF2915201D2724DF (match=true)
  ROW START backfill on existing rows: MIN(ValidFrom)=2020-01-01 00:00:00.0000000, MAX(ValidFrom)=2020-01-01 00:00:00.0000000
```

The `MIN` and `MAX` of `ValidFrom` both being `2020-01-01` is the trap avoided in the open: every historical row
carries the deliberate historical start, not conversion time. Confirmed on
`mcr.microsoft.com/mssql/server:2022-latest`.

## Verification — run in each environment after deployment

```sql
-- expect one row, temporal_type_desc = SYSTEM_VERSIONED_TEMPORAL_TABLE, with a paired history table
SELECT t.name, t.temporal_type_desc, h.name AS history_table
FROM sys.tables t
LEFT JOIN sys.tables h ON h.object_id = t.history_table_id
WHERE t.object_id = OBJECT_ID('dbo.Customer');

-- expect 2 rows: the period columns, GENERATED ALWAYS AS ROW START and ROW END
SELECT c.name, c.generated_always_type_desc
FROM sys.columns c
WHERE c.object_id = OBJECT_ID('dbo.Customer') AND c.generated_always_type <> 0;

-- expect 0 rows: no existing row falsely claims to have begun at conversion time
-- (every ROW START predates the go-live timestamp)
SELECT Id, ValidFrom FROM dbo.Customer WHERE ValidFrom >= '<conversion timestamp>';
```

## Rollback

The mirror of the conversion: `ALTER TABLE dbo.Customer SET (SYSTEM_VERSIONING = OFF);` then
`ALTER TABLE dbo.Customer DROP PERIOD FOR SYSTEM_TIME; ALTER TABLE dbo.Customer DROP COLUMN ValidFrom, ValidTo;`
and `DROP TABLE dbo.Customer_History;`. The existing rows' pre-existing column values are unchanged by the
conversion (the before/after business-data hash matched), so backing out the schema is lossless **for them**;
any history rows accumulated after go-live are lost when the history table is dropped, and are not recoverable.
Backing the change out was not exercised.

## Not verified

- **The `ROW START` start date.** `2020-01-01` was used on the copy as a sane historical floor; the real value
  is a data-owner decision (the conversion date, or a business-tracked historical date). It is visible anywhere
  history is queried.
- **Application impact.** How the running application behaves against a system-versioned table — explicit
  column-list writes, `SELECT *`, and any attempt to write the period columns — is not confirmed here, nor is
  the new code that will query history (`FOR SYSTEM_TIME AS OF ...`). The application owner owns closing this.
- **Design intent — history vs change feed.** The copy proves the conversion; it cannot confirm that
  *point-in-time history*, and not a *row-level change feed*, is what the use case needs. That is a design
  confirmation owed at intake (see Summary).
- **History growth and retention.** The history table grows with every update and has no cleanup unless a
  retention policy is set; whether one is configured is not shown by the small copy.
- **Other environments.** The block and the staged conversion were proven on a disposable copy of Dev only;
  Test, UAT, and Prod hold their own row counts, which the small copy cannot see — run the verification queries
  in each.
- **Production scale and timing.** The backfill and the versioning switch are exercised at 25 rows only;
  blocking and duration at >1M rows are not shown by the small copy. Schedule a window.
- **Reversibility.** Only the forward conversion is proven; disabling versioning and dropping the period columns
  and history table is not exercised here.
