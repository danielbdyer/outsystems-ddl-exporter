# Customer: shorten Email (a populated column is blocked; narrowing over the data would truncate)

**In OutSystems** — You reduce the length of the `Email` Attribute on the `Customer` Entity (its *Length* property goes from 250 down to a smaller number).
**In SSDT** — `[Email] NVARCHAR(250) NOT NULL` becomes a narrower `NVARCHAR(...)` in `Tables/dbo.Customer.sql`. You shrink the column; the publish engine decides whether the table's rows let that land.

## Summary

You shorten `Customer.Email`. In OutSystems this is a one-property edit; here the same intent is a
declarative narrowing of an existing column, and on a **populated** table a production publish
**refuses it** — the same data-blind guard the make-mandatory change hits. This was proven objectively
against a Twin — a disposable SQL Server database published from this estate and filled with
real-shaped synthetic data — with a **production-faithful** publish (`BlockOnPossibleDataLoss = true`,
the deployment a real environment runs).

On the proof substrate the minted emails are up to **9 characters**, so a narrowing to `NVARCHAR(6)`
would truncate every one of the 25 rows — a genuine data-loss narrowing. But the production block is
**row-presence, not length**: like make-mandatory, the guard fires because the table *holds rows*, so
narrowing is refused on any populated table **even when every value already fits**. This is the
**tightening class** (see `../skills/_index/tightening-class/SKILL.md`). No work item was provided with
the request; attach one before merge so the record is traceable.

## Review & release

- On an **empty** table: any team member can review this — no data can be lost — and it ships as a
  single schema change, applied in place.
- On a **populated** table: a dev lead or an experienced developer must review this, because the
  running application can no longer store values longer than the new size, and existing data is at
  risk. It does not ship as a single in-place edit — the production publish is blocked while
  `dbo.Customer` holds rows. It ships one of two ways: **(a)** after proving `MAX(LEN(Email))` fits the
  new size, a **logged, reviewed `BlockOnPossibleDataLoss` relaxation** for that one deployment; or
  **(b)** if values exceed the new size, reconcile the over-length rows first (a data change), then
  ship the narrowing — staged across releases if the extra length is real data that must be preserved.
- Added scrutiny: at production row counts the `ALTER COLUMN` rewrite may block writes or run long —
  schedule a window.

## Changes

| File | Change |
|---|---|
| `Tables/dbo.Customer.sql` | Narrows `[Email]` from `NVARCHAR(250)` to a smaller `NVARCHAR(...)` in the table definition |

No renames (the refactorlog is unchanged). No index, view, or procedure changes. Only the length of
`[Email]` changes — its type (`NVARCHAR`), nullability (`NOT NULL`), and every other column are
untouched.

## Data remediation

First **probe the data**: `SELECT MAX(LEN(Email)) FROM dbo.Customer;` and
`SELECT COUNT(*) FROM dbo.Customer WHERE LEN(Email) > <new>;` quantify how many rows would truncate.

- If **no value exceeds** the new size: the values are safe, but the populated table is still blocked
  by the row-presence guard — ship via the logged gate relaxation (path a).
- If **any value exceeds** the new size (as on the proof substrate — all 25 rows exceed 6): those rows
  truncate. Reconcile them first — shorten them deliberately, or keep the extra length and stage the
  narrowing across releases. No truncation value is prescribed here; it is a data-owner decision (named
  under Not verified).

## Deployment evidence — objective proof, production-faithful publish, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, seeds the blocking
condition in real-shaped data, and asserts the outcome under a **production-faithful** DacFx posture
(`BlockOnPossibleDataLoss = true`, no smart-defaults). DacFx is the same publish engine `sqlpackage`
wraps.

**Test:** `Twin.Tests.Integration.SamplePrTighteningTests+SamplePrTighteningTests.narrow: over-length data and the row-presence guard block a populated column, applies when empty`

```
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 1 m 51 s - Twin.Tests.Integration.dll (net9.0)
```

Baseline probe: `Customer` held **25 rows**, `MAX(LEN(Email)) = 9`, `Email` declared `NVARCHAR(250)`
(`max_length = 500` bytes). The narrowing under test is to `NVARCHAR(6)` — **all 25 rows** exceed it.

**Fact 1 (primary) — a populated table is REFUSED by the row-presence guard.** The production-faithful
publish of the narrowing was refused. The warning flags the data-loss direction; the block itself is
the data-blind guard (identical to make-mandatory), not the truncation check. Verbatim from the run:

```
Could not deploy package.
Warning SQL72015: The type for column Email in table [dbo].[Customer] is currently  NVARCHAR (250) NOT NULL but is being changed to  NVARCHAR (6) NOT NULL. Data loss could occur and deployment may fail if the column contains data that is incompatible with type  NVARCHAR (6) NOT NULL.
Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 50000, Level 16, State 127, Line 6 Rows were detected. The schema update is terminating because data loss might occur.
Error SQL72045: Script execution error.  The executed script:
IF EXISTS (SELECT TOP 1 1
           FROM   [dbo].[Customer])
    RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127)
        WITH NOWAIT;
```

The guard (`IF EXISTS ... RAISERROR`) sits above the `ALTER COLUMN` and never measures the column's
lengths. After the refusal: `Customer` rows = **25** (intact), `Email max_length = 500` bytes
(unchanged) — the block left the schema and data exactly as they were.

**Fact 2 (contrast) — the same edit on an EMPTY table applies clean.** With `Customer` emptied to 0
rows (children removed first), the identical narrowing published successfully under the same posture:

```
CONTRAST empty-table production publish APPLIED: Email max_length=12 bytes (NVARCHAR 6), rows=0
```

`max_length = 12` bytes — the column is now `NVARCHAR(6)`. The guard's `IF EXISTS` is false, so
nothing blocks.

**Fact 3 (secondary) — the Twin's own relaxed publish blocks on the truncation itself.** The Twin's
`Runs.up` publishes with `BlockOnPossibleDataLoss = false`, which suppresses the row-presence guard.
Under it, the `ALTER COLUMN` actually runs and SQL Server's execution-time truncation check refuses it
instead:

```
Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 2628, Level 16, State 1, Line 1 String or binary data would be truncated in table 'twin.dbo.Customer', column 'Email'. Truncated value: ''.
Error SQL72045: Script execution error.  The executed script:
ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR (6) NOT NULL;
```

`Email max_length` stayed 500 (rolled back). This is the weaker of the two blocks: it only fires when a
value actually exceeds the new size, whereas the *production* guard (Fact 1) blocks any populated
table — which is why Fact 1 governs how this ships.

## Verification — run in each environment after deployment

```sql
-- BEFORE narrowing — expect 0 rows: no value exceeds the new size (necessary, not sufficient:
-- the populated table is still blocked by the row-presence guard until the gate is relaxed)
SELECT Id, LEN(Email) AS len FROM dbo.Customer WHERE LEN(Email) > <new>;

-- AFTER deployment — expect max_length = 2 * <new> bytes: the column landed at the new size
SELECT max_length FROM sys.columns
WHERE object_id = OBJECT_ID('dbo.Customer') AND name = 'Email';
```

## Rollback

Re-widening the column is lossless:

```sql
ALTER TABLE dbo.Customer ALTER COLUMN Email NVARCHAR(250) NOT NULL;
```

Any value shortened by a truncating reconcile is **not** recoverable from the schema — the originals
must be preserved before the narrowing (a backup, or the before/after capture from a permissive run).
Backing the change out was not exercised.

## Not verified

- **Application impact.** Any code path that writes a value longer than the new size is now rejected
  (or would be silently truncated under a permissive publish); application-side length validation is
  not confirmed here — the application owner owns closing it.
- **The truncation value.** If over-length rows must be shortened, no truncation rule is chosen here; a
  data owner must supply it.
- **Other environments.** Test, UAT, and Prod may hold longer `Email` values than this copy. Run the
  `MAX(LEN)` probe in each before promotion — and note the row-presence guard fires in *every*
  populated environment regardless.
- **Production scale and timing.** Once the gate is relaxed, the `ALTER COLUMN` rewrite may block
  writes or run long at production row counts; the small copy cannot show that. Schedule a window.
- **Reversibility.** The forward narrowing is proven (both blocks and the empty-table apply); a
  truncating reconcile cannot be undone, and backing the change out was not exercised.
