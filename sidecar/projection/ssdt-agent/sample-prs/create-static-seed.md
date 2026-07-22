# Priority: add a lookup Static Entity (seeded by an idempotent guarded MERGE — the redeploy is silent)

**In OutSystems** — You add a new **Static Entity** `Priority` with its records (`High`, `Medium`, `Low`) — a small fixed lookup whose rows *are* part of the model, like the `Status` list the app already ships.
**In SSDT** — a declarative `CREATE TABLE [dbo].[Priority]` (structure) plus an **idempotent guarded MERGE** that seeds its rows in the static-data lane (`Data/StaticSeeds.sql`, the post-deployment slot). The IDs are **explicit constants, not IDENTITY**, so `PriorityId = 3` means the same row in every environment.

## Summary

You add a `Priority` lookup and its three records. The schema half is a plain `CREATE TABLE`; the data
half is a **MERGE that seeds the rows and is safe to run any number of times**. For reference data the
correctness property is not "the values are right" — it is that **re-running the seed changes nothing.**
That is the whole point of the guarded MERGE, and it is what this PR proves: seed once, then run the
identical seed again and watch it touch **zero rows** and leave a **byte-identical content-hash**. A
seed that rewrites its rows on every deploy is broken even when the values still match; the silent
second run is how you know this one is not.

This was proven objectively against a **Twin** — a disposable SQL Server 2022 database published from
this estate and filled with real-shaped synthetic data. The Twin converged the estate (schema + the
static-data lane), then the identical guarded MERGE was re-run directly against the seeded table, and a
second convergence was attempted. No work item was provided with the request; attach one before merge so
the record is traceable.

## Review & release

- **Any team member can review this**: the change is additive and the running application is unaffected —
  a new lookup and its rows appear; nothing existing is touched.
- **Ships as one release**: the schema change, then the post-deployment MERGE that runs after the table
  lands. The `CREATE TABLE` rides its own estate file (`Tables/dbo.Priority.sql`); the seed rides the
  static-data lane.
- **Explicit IDs, not IDENTITY** — the IDs are part of the model, so the app can rely on `PriorityId = 3`
  meaning the same row everywhere. A lookup keyed by IDENTITY is the trap this avoids (IDs drift between
  environments); here `Id` is a plain `INT` set by the seed.
- Added scrutiny: none — unless the lookup ever holds more than a million rows (at production row counts
  the seed may run long or block writes — schedule a window). A lookup is almost never that large.

## Changes

| File | Change |
|---|---|
| `Tables/dbo.Priority.sql` | Adds `CREATE TABLE [dbo].[Priority] ([Id] INT NOT NULL, [Name] NVARCHAR(50) NOT NULL, CONSTRAINT [PK_Priority] PRIMARY KEY ([Id]))` |
| `Data/StaticSeeds.sql` | Appends the guarded MERGE seeding `Priority` with `(1, High)`, `(2, Medium)`, `(3, Low)` |

No renames (the refactorlog is unchanged). No table other than the new lookup is touched; the existing
`Status` seed is unchanged.

## Data remediation

None. The seed rows are model data carried in the estate's static-data lane, not migrated from anywhere.
The MERGE is guarded so it is safe to run on every deploy: `WHEN NOT MATCHED BY TARGET THEN INSERT` adds
a missing row, `WHEN MATCHED AND [Name] <> [Name] THEN UPDATE` amends a changed label **only when it
actually differs**, and re-running against an already-seeded table fires no branch at all.

## Deployment evidence — objective proof, live Twin (SQL Server 2022), 2026-07-22

The proof is a green integration test that converges this estate to a live Twin, seeds the new lookup
through the static-data lane, then **re-runs the identical guarded MERGE and converges a second time** to
demonstrate silence. The content-hash is an order-sensitive `SHA2_256` over the lookup's `FOR XML RAW`
projection — a byte-identical hex string means the rows are byte-identical.

**Test:** `Twin.Tests.Integration.SamplePrSeedTests+SamplePrSeedTests.create-static-seed: a new lookup + guarded MERGE lane seeds on first converge and is idempotent (0 rows + identical hash) on the second`

```
Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 59 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact 1 — the first converge INSERTs the seed rows with explicit IDs.** `dbo.Priority` did not exist;
after the converge it held all three rows keyed by explicit `Id`, and `Id` is **not** IDENTITY. Verbatim
from the run:

```
baseline: dbo.Priority exists=0 (absent), estate has Status seeded only
first converge (schema published=true, lanes applied=1): dbo.Priority exists=1, rows=3, Id is_identity=0 (0 = explicit IDs, not IDENTITY)
  seeded rows: 1=High,2=Medium,3=Low
  content-hash after first seed = B62149B8C7EB3D44CDD6F71ADBAC179D7E1424DB5B48341E0E461D42F2B9B260
```

**Fact 2 (the signature proof) — the second run is silent.** The identical guarded MERGE, re-run against
the already-seeded table, touched **0 rows** and left a **byte-identical content-hash**. And the Twin's
own convergence, run a second time, reported **`NothingToApply`** — it saw both planes current and
skipped the lane entirely. Verbatim:

```
SECOND run of the identical guarded MERGE: rows affected = 0 (0 = idempotent no-op), rows = 3, content-hash = B62149B8C7EB3D44CDD6F71ADBAC179D7E1424DB5B48341E0E461D42F2B9B260 (identical=true)
SECOND converge (Runs.up): NothingToApply (the Twin sees both planes current and skips the lane)
  content-hash after second converge = B62149B8C7EB3D44CDD6F71ADBAC179D7E1424DB5B48341E0E461D42F2B9B260 (identical=true)
```

The hash after the first seed, after the second MERGE, and after the second converge are the **same 64
hex characters** — the redeploy is a no-op. That silence is the strongest guarantee the seed is correct:
it is the *absence of a change* you would otherwise have to trust.

## Verification — run in each environment after deployment

```sql
-- expect the lookup's model rows, unchanged, by explicit Id
SELECT Id, Name FROM dbo.Priority ORDER BY Id;   -- 1 High, 2 Medium, 3 Low

-- expect is_identity = 0: the key is an explicit constant, not IDENTITY (so PriorityId = 3
-- means the same row in every environment)
SELECT is_identity FROM sys.columns
WHERE object_id = OBJECT_ID('dbo.Priority') AND name = 'Id';

-- redeploy the post-deploy seed a second time: it must report 0 rows affected. A non-zero
-- count means the WHEN MATCHED is unconditional and rewriting rows — fix the guard.
```

## Rollback

The seed rows are model data held in `Data/StaticSeeds.sql`, so removing the lookup loses no unique
values: drop any child foreign keys that reference `Priority`, then `DROP TABLE dbo.Priority;`. A
redeploy re-runs the same idempotent seed to restore the rows. Backing the change out was not exercised.

## Not verified

- **Application impact.** Whether the running application references a `PriorityId` the seed does not
  provide is not confirmed by a disposable copy — the app owner confirms the ID set.
- **Other environments.** The copy proves the redeploy is silent for this data shape; whether another
  environment already holds drifted IDs from a prior IDENTITY-seeded table is not visible here — run the
  `is_identity` and `SELECT Id, Name` probes before promotion.
- **Production scale / timing.** The MERGE's cost at production row counts is not shown by a small copy;
  a lookup is rarely large enough to matter.
- **Reversibility.** The forward redeploy is proven (the silent second run); dropping the lookup is not
  exercised here.
