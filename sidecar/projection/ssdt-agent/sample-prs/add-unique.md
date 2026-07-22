# Order / Customer: require a value to be unique (duplicates block the build; a unique column lands clean)

**In OutSystems** — You mark an Attribute as unique — "no two Orders can share a Channel", "no two Customers can share an Email" — the kind of rule you'd enforce with a unique index or a validation.
**In SSDT** — a `CREATE UNIQUE INDEX [UIX_<Table>_<Col>]` object is added for that column (the v2 emitter renders uniqueness as a unique index after the table, not an inline constraint). The publish engine builds that uniqueness over **every existing row** at deploy — and the data decides whether it can.

## Summary

You require a column to be unique. Unlike tightening a length or a nullability, a uniqueness rule is a
**claim about the existing data**: SQL Server validates it against every row the moment the index is
built. If two rows already share a value, the build **fails** and the deployment is blocked; if the
values are already distinct, it lands clean — *even on a populated table*. This was proven objectively
against a Twin — a disposable SQL Server database published from this estate and filled with real-shaped
synthetic data — with a **production-faithful** publish (`BlockOnPossibleDataLoss = true`, the
deployment a real environment runs).

This is the **constraint-is-a-claim** family (see `../skills/_index/constraint-is-a-claim/SKILL.md`),
distinct from the tightening class: it blocks on an actual **duplicate value**, not on mere row
presence — so clean data applies in place with no remediation. One caveat worth naming: a SQL `UNIQUE`
index permits exactly **one** NULL row; several NULLs on a nullable column block it the same way a
duplicate does, and the fix for legitimately-repeated blanks is a **filtered** unique index
(`... WHERE <Col> IS NOT NULL`). No work item was provided with the request; attach one before merge.

## Review & release

- A dev lead or an experienced developer should review this: the running application must change to
  keep working — any insert or update that would create a duplicate now fails.
- **Clean data** (values already distinct) → ships as a single schema change, applied in place; the
  uniqueness is built over the existing rows as it lands.
- **Duplicate data present** → the deployment is blocked. It ships as **one release with a
  pre-deployment de-dupe**: the de-dupe clears the duplicates first, then the unique index builds
  validated. A dev lead must review this, because existing data is modified.
- Added scrutiny: at production row counts the uniqueness build (and any de-dupe) may block writes or
  run long — schedule a window.

## Changes

| File | Change |
|---|---|
| `Tables/dbo.Order.UniqueChannel.sql` (blocking case) | Adds `CREATE UNIQUE INDEX [UIX_Order_Channel] ON [dbo].[Order] ([Channel])` |
| `Tables/dbo.Customer.UniqueEmail.sql` (clean case) | Adds `CREATE UNIQUE INDEX [UIX_Customer_Email] ON [dbo].[Customer] ([Email])` |

The index rides its **own** estate file — one statement per file. Appending the `CREATE UNIQUE INDEX`
to the table's own `CREATE TABLE` fails the model build with `SQL71006: Only one statement is allowed
per batch`. No table definition changes; no renames.

## Data remediation

Probe first: `SELECT <Col>, COUNT(*) FROM <table> GROUP BY <Col> HAVING COUNT(*) > 1;` (and a NULL
count for a nullable column). If it returns rows, the deployment will block. The remedy is a
pre-deployment de-dupe that resolves the duplicates — merge or delete the offending rows — before the
unique index builds. If repeated **blanks** are legitimate rather than duplicates, use a filtered
unique index instead. The rows a de-dupe removes or merges must be recorded for a manual restore
(named under Not verified).

On the proof substrate the two facts were observed directly:
- With two Orders set to share a `Channel` value (24 distinct across 25 rows), the unique index build
  was **refused** on the duplicate.
- With `Customer.Email` already distinct across all 25 rows, the identical operation **applied** clean
  and the index is enforced.

## Deployment evidence — objective proof, production-faithful publish, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, seeds the blocking
condition in real-shaped data, and asserts the outcome under a **production-faithful** DacFx posture
(`BlockOnPossibleDataLoss = true`, no smart-defaults). DacFx is the same publish engine `sqlpackage`
wraps.

**Test:** `Twin.Tests.Integration.SamplePrTighteningTests+SamplePrTighteningTests.add-unique: duplicate values block the unique index, a unique column applies`

```
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 1 m 51 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact 1 (primary) — a duplicate value REFUSES the unique index build.** `Order` held **25 rows** with
**24 distinct** `Channel` values (two rows share a value). The production-faithful publish of the
unique index was refused by SQL Server's index build. Verbatim from the run — the generated `CREATE
UNIQUE INDEX` and the duplicate-key error it raised:

```
Could not deploy package.
Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 1505, Level 16, State 1, Line 1 The CREATE UNIQUE INDEX statement terminated because a duplicate key was found for the object name 'dbo.Order' and the index name 'UIX_Order_Channel'. The duplicate key value is (DUPE).
Error SQL72045: Script execution error.  The executed script:
CREATE UNIQUE NONCLUSTERED INDEX [UIX_Order_Channel]
    ON [dbo].[Order]([Channel] ASC);
```

After the refusal: `Order` rows = **25** (intact), `UIX_Order_Channel` was **not built** — the block
left the schema and data as they were. This is a **value** block (the duplicate is named), not the
data-blind row-presence guard.

**Fact 2 (contrast) — a genuinely unique column applies clean, on a populated table.** `Customer` held
**25 rows** with **25 distinct** `Email` values. The identical operation published successfully under
the same production-faithful posture:

```
CONTRAST setup: Customer rows=25, DISTINCT Email natural=25, final=25 (unique)
CONTRAST production publish (UNIQUE INDEX over unique Email): APPLIED, is_unique=1
```

`is_unique = 1` — the unique index exists and is enforced. Clean data does not need an empty table, a
gate relaxation, or a de-dupe: the claim is true of the data, so it lands in place.

## Verification — run in each environment after deployment

```sql
-- BEFORE — expect 0 rows: no value is shared across rows, so uniqueness holds
SELECT <Col>, COUNT(*) FROM <table> GROUP BY <Col> HAVING COUNT(*) > 1;

-- AFTER deployment — expect 1 row, is_unique = 1: the unique index exists and is enforced
SELECT name, is_unique FROM sys.indexes
WHERE object_id = OBJECT_ID('<table>') AND name = 'UIX_<Table>_<Col>';
```

## Rollback

The unique index drops without data loss (the same for a filtered unique index):

```sql
DROP INDEX [UIX_Order_Channel] ON dbo.[Order];
```

A pre-deployment de-dupe is **not** auto-reversed; the rows it removed or merged (recorded under Data
remediation) are what a manual restore uses. Backing the change out was not exercised.

## Not verified

- **Application impact.** Any insert or update that would create a duplicate value now fails on the
  unique key ("duplicate key was found"); on a nullable column a second NULL fails the same way.
  Application-side handling is not confirmed here — the application owner owns it.
- **Other environments.** Test, UAT, and Prod may hold duplicates the disposable copy cannot see. Run
  the duplicate probe in each before promotion.
- **The de-dupe decision.** When duplicates exist, whether the offending rows are merged or deleted is
  a data-owner decision, not made here.
- **Production scale and timing.** On a large table the uniqueness build and any de-dupe may block
  writes or run long; the small copy does not show it.
- **Reversibility.** Dropping the index is lossless, but a pre-deployment de-dupe is not exercised in
  reverse here; the recorded originals are what a manual restore would use.
