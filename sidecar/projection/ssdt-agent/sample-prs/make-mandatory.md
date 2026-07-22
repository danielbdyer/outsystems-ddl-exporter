# OrderLine: make Note required (a populated table is blocked until backfilled and gate-relaxed)

**In OutSystems** — You set *Is Mandatory = Yes* on the `OrderLine.Note` Attribute.
**In SSDT** — `[Note] NVARCHAR(200) NULL` becomes `[Note] NVARCHAR(200) NOT NULL` in `Tables/dbo.OrderLine.sql`. You edit the column; the publish engine decides whether the table's rows let that land.

## Summary

You set *Is Mandatory = Yes* on `OrderLine.Note`, so an order line can no longer be saved without a
note. In OutSystems the flag flips and Service Studio handles the rest; here the same intent is a
declarative tightening of an existing column, and a production publish **refuses it while the table
holds rows** — even after every NULL is filled in. This change was proven objectively against a Twin —
a disposable SQL Server database published from this estate and filled with real-shaped synthetic data
— with a **production-faithful** publish (`BlockOnPossibleDataLoss = true`, the deployment a real
environment runs). No work item was provided with the request; attach one before merge so the record
is traceable.

## Review & release

- A dev lead must review this: an existing column is tightened to NOT NULL while the table holds rows,
  and existing data must be remediated before the constraint can land.
- It does not ship as a single in-place edit. The production publish is blocked while `dbo.OrderLine`
  holds rows — the guard checks **row presence, not blank content**, so backfilling every NULL is
  necessary but **not sufficient**. It ships one of two ways: (a) backfill the NULL notes, then apply
  the tightening in a publish with a **logged, reviewed `BlockOnPossibleDataLoss` relaxation** for that
  one deployment; or (b) a staged rollout — add a new NOT NULL column with a default, migrate values,
  drop the old column — which the guard never fires on. On an empty table the same edit ships as one
  clean declarative change.
- Added scrutiny: first time this operation is proven on the Twin. At production row counts the
  `ALTER COLUMN` (once gate-relaxed) may run long or block writes — schedule a window.

## Changes

| File | Change |
|---|---|
| `Tables/dbo.OrderLine.sql` | Tightens `[Note]` from `NVARCHAR(200) NULL` to `NVARCHAR(200) NOT NULL` in the table definition |

No renames (the refactorlog is unchanged). No index, view, or procedure changes. Only the nullability
of `[Note]` changes — width (200), type (`NVARCHAR`), and every other column are untouched.

## Data remediation

Backfilling the NULL `Note` values is **necessary but not sufficient**. The production guard fires on
row presence, so a fully-backfilled populated table is still blocked; the backfill must be paired with
the logged gate relaxation, or replaced by the staged add-column rollout (see Review & release). No
backfill value is prescribed here — that is a data-owner decision (named under Not verified).

On the proof substrate the two facts were observed directly:
- With `Note` filled on **all 25 rows and zero NULLs**, the production-faithful publish was **still
  refused** (row-presence guard). Backfilling to zero NULLs does not clear the block.
- With the table **emptied to 0 rows**, the identical edit **applied** clean.

## Deployment evidence — objective proof, production-faithful publish, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, seeds the blocking
condition in real-shaped data, and asserts the outcome under a **production-faithful** DacFx posture
(`BlockOnPossibleDataLoss = true`, no smart-defaults — mirroring
`ssdt-agent/proving-ground/profiles/ProvingGround.Strict.publish.xml`). DacFx is the same publish
engine `sqlpackage` wraps.

**Test:** `Twin.Tests.Integration.SamplePrMakeMandatoryTests+SamplePrMakeMandatoryTests.make OrderLine.Note mandatory: production publish blocks a populated table, applies when empty`

```
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 55 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact 1 (primary) — a populated table is REFUSED, even with zero NULLs.** OrderLine held 25 rows and
`Note IS NULL` = 0 (`is_nullable` = 1). The production-faithful publish of the NOT NULL edit was
refused; the guard checks row presence, not blank content. Verbatim from the run — the generated deploy
script and the block it raised:

```
Warning SQL72016: The column Note on table [dbo].[OrderLine] must be changed from NULL to NOT NULL. If the table contains data, the ALTER script may not work. To avoid this issue, you must add values to this column for all rows or mark it as allowing NULL values, or enable the generation of smart-defaults as a deployment option.
Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 50000, Level 16, State 127, Line 6 Rows were detected. The schema update is terminating because data loss might occur.
Error SQL72045: Script execution error.  The executed script:
IF EXISTS (SELECT TOP 1 1
           FROM   [dbo].[OrderLine])
    RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127)
        WITH NOWAIT;
```

The guard (`IF EXISTS ... RAISERROR`) sits above the `ALTER COLUMN` and never inspects the `Note`
column. After the refusal: `is_nullable` = 1 (unchanged), rows = 25 (intact), `Note IS NULL` = 0
(unchanged) — the block left the schema and data exactly as they were.

**Fact 2 (primary) — the same edit on an EMPTY table applies clean.** With OrderLine emptied to 0 rows,
the identical NOT NULL edit published successfully under the same production-faithful posture:
`is_nullable` = 0 (`Note` is now mandatory). The guard's `IF EXISTS` is false, so nothing blocks.

**Fact 3 (secondary) — the Twin's own relaxed publish blocks only on an actual NULL.** The Twin's
`Runs.up` publishes with `BlockOnPossibleDataLoss = false`, which suppresses the row-presence guard.
Under it, with 3 of 25 rows holding a NULL `Note`, the publish was refused by SQL Server's
execution-time NULL check instead:

```
Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 515, Level 16, State 2, Line 1 Cannot insert the value NULL into column 'Note', table 'twin.dbo.OrderLine'; column does not allow nulls. UPDATE fails.
Error SQL72045: Script execution error.  The executed script:
ALTER TABLE [dbo].[OrderLine] ALTER COLUMN [Note] NVARCHAR (200) NOT NULL;
```

`is_nullable` stayed 1. This is the weaker of the two blocks: it clears once the NULLs are gone, which
is why the *production* guard (Fact 1) is the one that governs how this ships.

## Verification — run in each environment after deployment

```sql
-- BEFORE tightening — expect 0. Necessary, not sufficient: even at 0 the production
-- publish is blocked by the row-presence guard until BlockOnPossibleDataLoss is relaxed.
SELECT COUNT(*) AS null_notes FROM dbo.OrderLine WHERE Note IS NULL;

-- AFTER deployment — expect is_nullable = 0: the column landed NOT NULL
SELECT is_nullable FROM sys.columns
WHERE object_id = OBJECT_ID('dbo.OrderLine') AND name = 'Note';
```

## Rollback

Re-widening the column is lossless:

```sql
ALTER TABLE dbo.OrderLine ALTER COLUMN Note NVARCHAR(200) NULL;
```

The backfill is not auto-reversed — a full backout would also restore the rows that were backfilled to
their prior NULL, from the values recorded before the backfill. Backing the change out was not
exercised.

## Not verified

- **Application impact.** Any code path that saves an OrderLine without a `Note`, or writes NULL to it,
  will fail once the column is mandatory (`Msg 515` at insert/update). Application-side validation is
  not confirmed here — the application owner owns closing this before promotion.
- **The backfill value.** No `Note` placeholder is chosen here; a data owner must supply it. The value
  is visible anywhere `Note` is displayed.
- **Other environments.** Test, UAT, and Prod may hold NULL `Note` rows this copy cannot see. The
  row-presence guard fires in *every* populated environment regardless — run the BEFORE probe and plan
  the gate-relaxed (or staged) rollout in each before promotion.
- **Production scale and timing.** Once the gate is relaxed, the `ALTER COLUMN` may block writes or run
  long at production row counts; the small copy cannot show that. Schedule a window.
- **Reversibility.** The forward publish is proven (both the block and the empty-table apply); backing
  the change out was not exercised.
