# Customer: split the Email attribute out into a new CustomerContact entity (phase 1 — new entity created, every Email copied 1:1, FK trusted; the source-column drop is the guarded later phase)

**In OutSystems** — You split the `Customer` Entity into two: `Customer` keeps its core fields, and a new `CustomerContact` Entity holds the `Email` Attribute, linked one-to-one back to `Customer` by a `Customer` reference. This is one Entity becoming two — the way you pull a field into its own Entity when one Entity is doing too much.
**In SSDT** — a multi-step transform: **create** `dbo.CustomerContact` (its own identity PK, a `CustomerId` foreign key to `dbo.Customer`, a `UNIQUE` on `CustomerId` that makes it 1:1, and the `Email` column), **copy** every `Customer.Email` into it with a post-deploy `INSERT … SELECT`, and — much later, in its own release — **drop** the old `Customer.Email` column. SSDT creates the table and adds the FK declaratively but will **never move the data** (the copy is a post-deploy script), and it blocks the column drop under `BlockOnPossibleDataLoss` until the copy is proven. Edit the `CREATE`; never write the `ALTER`.

## Summary

This PR proves **phase 1** of a staged split. Like `extract-to-lookup`, moving existing data into a new
shape behind a new relationship **cannot ship in one publish** — the old `Email` column and the new
`CustomerContact` row have to coexist while every reader and writer migrates, so it stages across
several releases. What phase 1 establishes objectively, on a disposable copy, is the load-bearing
safety property: **the copy is complete and faithful.** The new table is created empty, every
`Customer.Email` is copied across, the relationship is **1:1** (no customer's Email is lost, none is
duplicated), the copied `Email` values are **byte-identical** to the source (a `SHA2_256` digest match),
and the foreign key lands **trusted**. Proving the copy carried every value **before** the old column is
ever dropped is what stops an Email from silently disappearing.

**The old `Customer.Email` column is deliberately retained here.** Dropping it is a **later phase** (its
own PR), gated on the application having stopped reading and writing the column — and the production
publish **blocks that drop single-phase**, which this PR also demonstrates: the guard refuses to remove a
populated column because it cannot see that the values already arrived in the new table.

This was proven objectively against a **Twin** — a disposable SQL Server 2022 database published from
this estate and filled with real-shaped synthetic data — under a **production-faithful** publish
(`BlockOnPossibleDataLoss = true`, `GenerateSmartDefaults = false`, `DropObjectsNotInSource = false`).
Because the split is imperative data motion (create, copy), it was run as scripted steps against the live
Twin, the same way a real migration runs. No work item was provided with the request; attach one before
merge so the record is traceable.

## Review & release

- **A dev lead must review this**: existing data is moved into a new table (`CustomerContact`) and a
  cross-table relationship (the foreign key back to `Customer`) is added.
- **Ships across three releases (three pull requests)**: create the new table and copy the moving
  column, cut the application over (repoint reads to the new Entity), then — in a *later* PR, only after
  the app stops using the old column — drop `Customer.Email`. This PR is the create + copy phase. On an
  empty source table the whole split collapses to a single additive release any team member can review;
  prove the source is empty first (here it holds 25 rows).
- **The copy must be proven complete before the drop phase**, so nothing is silently lost. If any
  environment holds an `Email` the copy missed, dropping the source column loses it — run the
  verification queries in each environment first.
- Added scrutiny: none for a small, clean split; at >1M rows the copy is a long-running batched operation
  that may block writes or run long — schedule a window.

## Changes

| Object | Change |
|---|---|
| `dbo.CustomerContact` (new) | `CREATE TABLE` — `[Id]` IDENTITY primary key, `[CustomerId]` foreign key to `dbo.Customer` (`FK_CustomerContact_Customer`), a `UNIQUE ([CustomerId])` making the relationship 1:1, and the `[Email] NVARCHAR(250)` column |
| copy (post-deploy) | `INSERT INTO dbo.CustomerContact ([CustomerId],[Email]) SELECT [Id],[Email] FROM dbo.Customer` — moves every Email keyed by the source PK |
| `dbo.Customer.Email` | **Retained this phase** — the source column stays while readers migrate; its drop is a later PR (and the production publish blocks it until the copy is proven — see the evidence) |

No renames — a split is **copy-then-drop, never a per-column rename** (a rename with no refactorlog entry
would read as drop-and-add and lose the column's data). The refactorlog is unchanged.

## Data motion

`CustomerContact` is created empty (a `CREATE` mints no rows), then every `Customer.Email` is copied into
it by `INSERT … SELECT` keyed by `Customer.Id`. The completeness gate is a before/after content-hash of
the `Email` column, source vs. new table, plus a cardinality check that the relationship is exactly 1:1.
On the copy the hashes matched and the cardinality was clean, so the new table provably holds every value
— the proof that licenses the *later* column-drop phase.

## Deployment evidence — objective proof, production-faithful publish, live Twin (SQL Server 2022), 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, materializes real-shaped
`Customer` data with `Email` populated on every row, runs the split's phase-1 steps, and asserts the
cardinality, copy-fidelity, trust, and guarded-drop properties by consuming the data directly. The
content-hash is an order-sensitive `SHA2_256` over the column's `FOR XML RAW` projection. DacFx is the
same publish engine `sqlpackage` wraps.

**Test:** `Twin.Tests.Integration.SamplePrStructuralTests+SamplePrStructuralTests.split-table (phase 1): the new CustomerContact is created, every Email copies 1:1 with a matching digest and trusted FK, and the source-column drop is the guarded later phase`

```
Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 1 m 2 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact 1 — the new entity is created and the copy is 1:1, byte-identical, and trusted.** `Customer` held
25 rows, each with `Email` populated. The production-faithful publish created `CustomerContact` empty;
the copy carried all 25 Emails; every customer got exactly one contact (**0 lost, 0 duplicated**); the
`Email` digest matched the source **byte-for-byte**; and `FK_CustomerContact_Customer` landed **trusted**
(`is_not_trusted = 0`). Verbatim from the run:

```
baseline: Customer rows=25 (each with Email populated), CustomerContact exists=0 (absent), source Email digest=80F7F3DE4BFC8990C6E159FF3D4E423D9365029DCB024F0A4A64E34590EA2FE7
phase 1a additive: production publish (BlockOnPossibleDataLoss=true) CREATE TABLE [dbo].[CustomerContact] (Id/CustomerId FK->Customer + Email, 1:1 UNIQUE): APPLIED (Ok)
  after create: CustomerContact exists=1, rows=0 (created empty; strict publish does not mint)
phase 1b copy: INSERT..SELECT every Customer.Email into CustomerContact -> rows copied=25
  cardinality 1:1: CustomerContact rows=25, distinct CustomerId=25, Customers with no contact (lost)=0, contacts sharing a CustomerId (duplicated)=0
  fidelity: FK_CustomerContact_Customer is_not_trusted=0 (0 = trusted); source Email digest=80F7F3DE4BFC8990C6E159FF3D4E423D9365029DCB024F0A4A64E34590EA2FE7, new-table Email digest=80F7F3DE4BFC8990C6E159FF3D4E423D9365029DCB024F0A4A64E34590EA2FE7 (match=true)
```

**Fact 2 — the source-column drop is the guarded later phase (blocked single-phase).** Rewriting
`Customer` to remove `Email` and re-publishing under the same production-faithful posture was **refused**:
`Email` is populated on 25 rows, so the data-loss guard fires. The column **survived** the block
(`exists=1`) and all 25 rows were left intact — the block is transactional. Verbatim — the DacFx warning
that names the column, and the row-presence guard that terminated the deploy:

```
phase 3 subtractive (the guarded later phase): production publish DROP the source column [dbo].[Customer].[Email]: REFUSED (blocked)
  after the block: Customer.[Email] column exists=1 (1 = survived the block), Customer rows=25 (intact)
  strict detail:
Could not deploy package.
Warning SQL72015: The column [dbo].[Customer].[Email] is being dropped, data loss could occur.
Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 50000, Level 16, State 127, Line 6 Rows were detected. The schema update is terminating because data loss might occur.
Error SQL72045: Script execution error.  The executed script:
IF EXISTS (SELECT TOP 1 1
           FROM   [dbo].[Customer])
    RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127)
        WITH NOWAIT;
```

`SQL72015` names exactly what would be lost — `[dbo].[Customer].[Email]` — and the guard
(`IF EXISTS (SELECT TOP 1 1 FROM [dbo].[Customer]) RAISERROR(...)`) sits above the `DROP COLUMN` and fires
on **row presence**. That is *why* the drop is a separate release: SSDT refuses to remove the old column
while it cannot see that the values already arrived in `CustomerContact`. The proven-complete phase-1 copy
is what licenses that later drop.

## Verification — run in each environment after deployment

```sql
-- expect equal hashes: Email holds the same content in the new table as in the source
-- (run after the copy, before the later column-drop phase)
SELECT
  CONVERT(CHAR(64), HASHBYTES('SHA2_256',
    CAST((SELECT Id AS Cid, Email FROM dbo.Customer ORDER BY Id FOR XML RAW) AS VARBINARY(MAX))), 2) AS source_hash,
  CONVERT(CHAR(64), HASHBYTES('SHA2_256',
    CAST((SELECT CustomerId AS Cid, Email FROM dbo.CustomerContact ORDER BY CustomerId FOR XML RAW) AS VARBINARY(MAX))), 2) AS newtable_hash;

-- expect 0 rows: every customer's Email has its copy in the new table (the copy carried all rows)
SELECT c.Id FROM dbo.Customer c
LEFT JOIN dbo.CustomerContact cc ON cc.CustomerId = c.Id WHERE cc.CustomerId IS NULL;

-- expect one row, is_not_trusted = 0: the foreign key landed trusted
SELECT name, is_not_trusted FROM sys.foreign_keys WHERE name = 'FK_CustomerContact_Customer';
```

## Rollback

This phase is additive to the source table (it creates a table and copies; the `Email` column stays), so
it backs out without data loss: `DROP TABLE dbo.CustomerContact;` — the original `Customer.Email` is
untouched throughout. The *later* column-drop phase is the one that needs the copy-complete proof to be
reversible (the Email is reconstructable from the `CustomerContact` join). Backing the change out was not
exercised.

## Not verified

- **The later phases.** Only phase 1 (create + copy) is proven here, plus the *demonstration* that the
  source-column drop blocks single-phase. The application cutover (readers/writers moving to
  `CustomerContact`) and the final, completed `Customer.Email` **drop** — which ships only after the app
  has migrated — are separate PRs and are **not** exercised by this proof.
- **Application impact.** Application code that reads or writes `Customer.Email` directly, rather than
  through the new Entity, keeps working *this* phase (the column is retained) but breaks once it is
  dropped later; that every reader and writer has moved is confirmed by the app owner, not here.
- **Other environments.** The copy's completeness (source and new-table hashes equal) was proven on a
  disposable copy of Dev only; Test, UAT, and Prod hold their own rows — run the verification queries
  before the drop in each.
- **Production scale / timing.** The copy is exercised at seed scale only; blocking and duration at >1M
  rows are not shown by the small copy.
- **Reversibility.** Only the forward split is proven; re-adding a dropped `Email` column and copying back
  from `CustomerContact` is a concern of the later phase and is not exercised here.
