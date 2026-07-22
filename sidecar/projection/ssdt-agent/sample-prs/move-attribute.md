# Customer → CustomerProfile: move the CreatedOn attribute onto the profile (phase 1 — join proven 1:1, destination backfilled with a matching digest; the source-column drop is the guarded later phase)

**In OutSystems** — You move the `CreatedOn` Attribute off `Customer` and onto the existing 1:1
`CustomerProfile` Entity — "this field is on the wrong Entity." One Attribute crossing from one Entity to
another, carrying its data.
**In SSDT** — a one-column split: **add** the column to the destination (`CustomerProfile.CreatedOn`),
**copy** the values keyed by the 1:1 relationship (`Customer.Id = CustomerProfile.CustomerId`), repoint
readers, then **drop** the column from the source (`Customer.CreatedOn`). SSDT adds the nullable column and
publishes clean; the copy runs post-deploy; and it **blocks** the source-column drop under
`BlockOnPossibleDataLoss` until the values are proven to have moved. A cross-table move is **copy-then-drop,
never a rename** — a cross-table column has no refactorlog identity mapping, so a "rename" would drop the
column and lose its data. Edit the `CREATE`; never write the `ALTER`.

## Summary

This PR proves **phase 1** of a staged move. Like `split-table`, relocating a field between Entities moves
existing data behind a coexistence window and **cannot ship in one publish**. The named trap for a move is
**relationship ambiguity**: the copy needs a join key, and if the relationship is not 1:1 the moved value
is ambiguous — which `Customer`'s `CreatedOn` wins for a profile with many customers? So the join must be
proven **1:1 before any copy runs**.

What phase 1 establishes objectively, on a disposable copy: the `Customer → CustomerProfile` join is
**exactly 1:1** (no parent has more than one profile, every customer maps to one); the destination column
is added and backfilled so **every** value moves (0 left NULL) with a **byte-identical** `SHA2_256` digest
match; and — the coexistence discipline — the drop of the source column `Customer.CreatedOn` is shown to
**block single-phase** under the production publish, because a populated column cannot be removed while the
guard cannot see the values already arrived on the destination.

**The source column `Customer.CreatedOn` is deliberately retained here.** Dropping it is a **later phase**
(its own PR), gated on every reader having repointed to `CustomerProfile`. This is why a move ships across
more than one release: the running app keeps working while the field moves.

Proven objectively against a **Twin** — a disposable SQL Server 2022 database published from this estate
and filled with real-shaped synthetic data — under a **production-faithful** publish
(`BlockOnPossibleDataLoss = true`, `GenerateSmartDefaults = false`, `DropObjectsNotInSource = false`). The
move is imperative data motion, so it was run as scripted steps against the live Twin. No work item was
provided with the request; attach one before merge so the record is traceable.

## Review & release

- **A dev lead must review this**: existing data is moved between tables — the values are copied to the
  destination and the source column is dropped (in a later phase).
- **Ships across multiple releases (multiple pull requests)**: add the column to the destination and copy
  the values keyed by the relationship, repoint every reader, then drop the source column — the two tables
  coexist while readers migrate. This PR is the add + copy phase.
- **The relationship must be proven 1:1 before the copy in every environment.** If any environment holds a
  one-to-many parent this copy did not, the moved value is ambiguous there — STOP and treat it as a design
  decision (which value wins?), not a matter of how it ships.
- **A move is never a rename.** A cross-table column has no refactorlog identity mapping; letting SSDT
  DROP-and-CREATE it would lose the data. It must be copy-then-drop.
- Added scrutiny: none for a small, clean, 1:1 move; at >1M rows the copy scans the table and may block
  writes or run long — schedule a window.

## Changes

| Object | Change |
|---|---|
| `dbo.CustomerProfile` | Adds the destination column `[CreatedOn] DATETIME2 NULL` (nullable → publishes clean over the populated satellite) |
| copy (post-deploy) | `UPDATE p SET p.CreatedOn = c.CreatedOn FROM dbo.CustomerProfile p JOIN dbo.Customer c ON c.Id = p.CustomerId` — after the 1:1 proof |
| `dbo.Customer.CreatedOn` | **Retained this phase** — the source column stays while readers migrate; its drop is a later PR, and the production publish blocks it until the copy is proven (see the evidence) |

No renames — a cross-table move is **copy-then-drop, never a rename**. The refactorlog is unchanged.

## Data motion

The destination column is added nullable, then `CustomerProfile.CreatedOn` is backfilled from
`Customer.CreatedOn` joined on the 1:1 key. Two proofs gate the phase: the **1:1** proof (no parent has
more than one child, every parent maps to one) runs **first, before any copy**, so no moved value is
ambiguous; and the **value** proof (a before/after content-hash of the moving column, source vs.
destination) confirms every value arrived byte-for-byte.

## Deployment evidence — objective proof, production-faithful publish, live Twin (SQL Server 2022), 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, creates and seeds an
existing 1:1 `CustomerProfile` satellite, runs the move's phase-1 steps, and asserts the 1:1, copy-fidelity,
and guarded-drop properties by consuming the data directly. The content-hash is an order-sensitive
`SHA2_256` over the column's `FOR XML RAW` projection. DacFx is the same publish engine `sqlpackage` wraps.

**Test:** `Twin.Tests.Integration.SamplePrStructuralTests+SamplePrStructuralTests.move-attribute (phase 1): the join is proven 1:1, the destination column is backfilled with a matching digest, and the source-column drop is the guarded later phase`

```
Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 1 m 2 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact 1 — the join is proven 1:1, then every value moves byte-identical.** The `Customer → CustomerProfile`
relationship had **zero** parents with more than one profile and **zero** customers with no profile — a
clean 1:1, so no moved value is ambiguous. Only then was `CustomerProfile.CreatedOn` added and backfilled:
**0** destination rows were left NULL, and the source-vs-destination digest matched **byte-for-byte**.
Verbatim from the run:

```
baseline: Customer rows=25, source column Customer.[CreatedOn] exists=1, CustomerProfile exists=0 (absent)
setup: production publish CREATE TABLE [dbo].[CustomerProfile] (existing 1:1 satellite): APPLIED (Ok); seeded 25 profiles, rows=25
phase 1 relationship: Customer->CustomerProfile parents with >1 child=0, Customers with no profile=0 (both zero + rows equal = 1:1; no moved value is ambiguous)
phase 1 additive+copy: production publish ADD [dbo].[CustomerProfile].[CreatedOn] (nullable): APPLIED (Ok); destination column exists=1; backfilled 25 rows keyed by the 1:1 relationship
  fidelity: destination rows with NULL CreatedOn=0 (0 = every value moved); source digest=9F2BD9E3D9852A4CC6C086A1367BFBCB75833507446768DD03FAE4F4BB337342, destination digest=9F2BD9E3D9852A4CC6C086A1367BFBCB75833507446768DD03FAE4F4BB337342 (match=true)
```

**Fact 2 — the source-column drop is the guarded later phase (blocked single-phase).** Rewriting `Customer`
to remove `CreatedOn` and re-publishing under the same production-faithful posture was **refused**:
`CreatedOn` is populated on 25 rows, so the data-loss guard fires. The column **survived** the block
(`exists=1`) and all 25 rows were left intact — the block is transactional. Verbatim — the DacFx warning
that names the source column, and the row-presence guard that terminated the deploy:

```
phase 3 subtractive (the guarded later phase): production publish DROP the source column [dbo].[Customer].[CreatedOn]: REFUSED (blocked)
  after the block: Customer.[CreatedOn] column exists=1 (1 = survived the block), Customer rows=25 (intact)
  strict detail:
Could not deploy package.
Warning SQL72015: The column [dbo].[Customer].[CreatedOn] is being dropped, data loss could occur.
Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 50000, Level 16, State 127, Line 6 Rows were detected. The schema update is terminating because data loss might occur.
Error SQL72045: Script execution error.  The executed script:
IF EXISTS (SELECT TOP 1 1
           FROM   [dbo].[Customer])
    RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127)
        WITH NOWAIT;
```

`SQL72015` names exactly what would be lost — `[dbo].[Customer].[CreatedOn]` — and the guard
(`IF EXISTS (SELECT TOP 1 1 FROM [dbo].[Customer]) RAISERROR(...)`) sits above the `DROP COLUMN` and fires on
**row presence**. That is *why* the drop is a separate release: SSDT keeps the source-column drop blocked
until the copy is proven, and the proven-1:1, digest-equal phase-1 copy is what licenses that later drop.

## Verification — run in each environment after deployment

```sql
-- expect 0 rows: the relationship is 1:1, so no moved value is ambiguous (a returned row is a customer
-- with more than one profile — STOP, the move is unsafe as stated)
SELECT CustomerId, COUNT(*) AS profiles
FROM dbo.CustomerProfile
GROUP BY CustomerId
HAVING COUNT(*) > 1;

-- expect equal hashes: the moving values are the same content on source and destination
-- (run after the copy, before the source-column drop)
SELECT
  CONVERT(CHAR(64), HASHBYTES('SHA2_256',
    CAST((SELECT Id AS Cid, CreatedOn FROM dbo.Customer ORDER BY Id FOR XML RAW) AS VARBINARY(MAX))), 2) AS source_hash,
  CONVERT(CHAR(64), HASHBYTES('SHA2_256',
    CAST((SELECT CustomerId AS Cid, CreatedOn FROM dbo.CustomerProfile WHERE CreatedOn IS NOT NULL ORDER BY CustomerId FOR XML RAW) AS VARBINARY(MAX))), 2) AS destination_hash;
```

## Rollback

Before the source-column drop, backing out is lossless: `ALTER TABLE dbo.CustomerProfile DROP COLUMN
CreatedOn;` and repoint readers back to `Customer.CreatedOn`, which still holds its values. The drop is
**not** auto-reversible — once the source column is gone the values live only on `CustomerProfile`;
restoring it means re-adding the column and copying back from the destination (the values were proven equal
to the source originals before the drop). Keep the source column's data recoverable — a backup, or the
coexisting source column — until the drop is confirmed durable. Backing the change out was not exercised.

## Not verified

- **The later phases.** Only phase 1 (add column + copy + 1:1 proof) is proven here, plus the
  *demonstration* that the source-column drop blocks single-phase. The application cutover (readers moving
  to `CustomerProfile`) and the final, completed `Customer.CreatedOn` **drop** are separate PRs and are
  **not** exercised by this proof.
- **Application impact.** Any read or write path still pointing at `Customer.CreatedOn` breaks once it is
  dropped; that every reader has been repointed to the destination is confirmed by the app owner, not here.
- **Other environments.** The relationship was proven 1:1 on a disposable copy of Dev only; Test, UAT, and
  Prod may hold a one-to-many parent this copy does not — run the 1:1 check before the copy in each
  environment.
- **Production scale / timing.** The copy and drop are exercised at seed scale only; blocking and duration
  at >1M rows are not shown by the small copy.
- **Reversibility.** Only the forward move is proven; re-adding the source column and copying back from the
  destination is not exercised here.
