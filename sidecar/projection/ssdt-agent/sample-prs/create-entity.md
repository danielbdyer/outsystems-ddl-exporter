# Address: add a new Entity (a brand-new table nothing yet depends on)

**In OutSystems** — You create a new Entity `Address` with an auto-number Identifier and a reference Attribute `Customer` (so each address belongs to a customer).
**In SSDT** — a new file `Tables/dbo.Address.sql` holds a single `CREATE TABLE [dbo].[Address]` with its identity primary key and its foreign key to `dbo.Customer` inline. The table does not exist yet, so SSDT emits the `CREATE TABLE` verbatim — there is nothing to transition.

## Summary

You add a brand-new `Address` Entity that references `Customer`. This is the **purest additive change**:
a new table nothing yet depends on, holding no data. A production publish just runs the `CREATE TABLE`
and the table appears — empty. There is no existing data for SQL Server to be conservative about, so
nothing can conflict and nothing can be lost. This was proven objectively against a Twin — a disposable
SQL Server database published from this estate and filled with real-shaped synthetic data — with a
**production-faithful** publish (`BlockOnPossibleDataLoss = true`, the deployment a real environment
runs). No work item was provided with the request; attach one before merge so the record is traceable.

**The one thing worth checking is dependencies.** A create can only go wrong on a dependency, not on
anything the data does: a foreign key to a parent not yet in the project (the **build** fails, not the
deploy), or a `.sql` file the project glob misses (it silently never deploys). Here the foreign-key
parent `Customer` already exists in the estate, and the new file sits under the `Tables/*.sql` glob —
both confirmed by the clean publish. Nothing else in the estate references `Address` yet.

## Review & release

- Any team member can review this: the change is additive and self-contained — a brand-new table
  nothing yet depends on, and the running application is unaffected.
- It ships as a single schema change, applied in place: SSDT emits `CREATE TABLE [dbo].[Address]`
  verbatim (with its `PK_Address` primary key and `FK_Address_Customer` foreign key). No existing data
  is read or written.
- If the table later needs seed/lookup rows on creation, it ships as one release instead: the CREATE,
  then a post-deployment MERGE seed that runs after the table lands (see `create-static-seed`). Not
  needed here — `Address` is a plain data table, created empty.
- Added scrutiny: none for a standalone table.

## Changes

| File | Change |
|---|---|
| `Tables/dbo.Address.sql` | Adds `CREATE TABLE [dbo].[Address]` — `[Id]` IDENTITY primary key, `[CustomerId]` foreign key to `dbo.Customer`, and `[Line1]` / `[City]` / `[PostalCode]` columns |

No renames (the refactorlog is unchanged). No index, view, or procedure changes, and **no existing
table is touched** — the only object added is the new `Address` table and its two constraints.

## Data remediation

None. The table is created empty; there is no existing data to fill, reconcile, or backfill, and no
existing row anywhere is read or written. The foreign key `FK_Address_Customer` is created and validated
over an empty child, so it lands **trusted** with nothing to reconcile.

## Deployment evidence — objective proof, production-faithful publish, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, materializes
real-shaped data in the existing tables, adds the new table as its own estate file, and asserts the
outcome under a **production-faithful** DacFx posture (`BlockOnPossibleDataLoss = true`, no
smart-defaults). DacFx is the same publish engine `sqlpackage` wraps.

**Test:** `Twin.Tests.Integration.SamplePrCleanApply2Tests+SamplePrCleanApply2Tests.create-entity: a new table with an identity PK and an FK to Customer applies clean`

```
Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 1 m 16 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact — the new table publishes clean, its key and reference land, and nothing existing is touched.**
The three existing tables held **25 rows** each and `Address` did not exist. The production-faithful
publish of the `CREATE TABLE` was **accepted**; the table appeared with its primary key, its foreign key
(trusted), and its identity column, holding zero rows, and every existing table was left exactly as it
was. Verbatim from the run:

```
baseline: Address exists=0; existing rows Customer=25, Order=25, OrderLine=25
production publish (BlockOnPossibleDataLoss=true) CREATE TABLE [dbo].[Address] (+ PK, + FK to Customer): APPLIED (Ok)
  after apply: Address exists=1, PK_Address exists=1, FK_Address_Customer exists=1 (is_not_trusted=0), identity columns=1, Address rows=0 (created empty)
  existing rows intact: Customer=25, Order=25, OrderLine=25
```

The publish returned `Ok` under the production-faithful posture — a `CREATE TABLE` is a clean additive
change with no data condition to violate. After the apply: `Address` exists, `PK_Address` exists, the
identity column landed (`identity columns = 1`), `FK_Address_Customer` exists and is **trusted**
(`is_not_trusted = 0`, validated over the empty child), the table is empty (**0 rows** — the strict
publish creates the schema and does not mint data), and the existing tables are unchanged (**25 → 25**
each).

## Verification — run in each environment after deployment

```sql
-- expect 1 row: the new table exists
SELECT name, type_desc FROM sys.tables WHERE object_id = OBJECT_ID('dbo.Address');

-- expect 1 row: the primary key landed
SELECT name FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID('dbo.Address') AND type = 'PK';

-- expect 1 row, is_not_trusted = 0: the foreign key to Customer landed and is trusted
SELECT name, is_not_trusted FROM sys.foreign_keys WHERE name = 'FK_Address_Customer';
```

## Rollback

Remove the `Tables/dbo.Address.sql` file from the project and republish; SSDT emits
`DROP TABLE [dbo].[Address];`. Lossless **only while the table is unwritten** — it is created empty, so
until the application writes rows into it, dropping it discards nothing. Once rows are written, dropping
the table discards them (and any post-deployment seed rows go with it). Backing the change out was not
exercised.

## Not verified

- **Application impact.** A new table nothing yet reads or writes does not change existing behaviour, but
  any application code intended to read or write `Address` (screens, actions) is not exercised by the
  disposable copy — the application owner owns confirming it before promotion.
- **Dependencies in other environments.** The clean publish confirms the foreign-key parent (`Customer`)
  exists and the file is in the glob **for this dacpac**; it cannot see another environment's project
  structure. A foreign key to a parent missing there would fail that build.
- **Reversibility.** The forward create is proven; once the application writes rows into `Address`,
  dropping it is lossy (see Rollback).
