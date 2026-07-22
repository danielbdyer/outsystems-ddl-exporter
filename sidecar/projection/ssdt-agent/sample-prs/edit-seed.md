# Status: change a lookup value (Pending → On Hold; the guarded MERGE touches exactly one row and the re-run is silent)

**In OutSystems** — You rename the label of one record in the `Status` **Static Entity** — `Pending` becomes `On Hold` — leaving the record's Id (and every reference to it) exactly where it is.
**In SSDT** — you amend one entry in the `VALUES` block of the idempotent MERGE in the static-data lane (`Data/StaticSeeds.sql`). The table definition is unchanged. The **guarded** `WHEN MATCHED AND [Name] <> [Name] THEN UPDATE` path fires for that **one** row.

## Summary

You change the label on `Status` Id 3 from `Pending` to `On Hold`. The proof that matters for a seed
edit is two-part: the change must land on **exactly the one row that changed** — not register as a
table-wide rewrite — and **re-running the seed afterward must be silent** (zero rows, identical
content-hash). Both are properties of the *guard* on the MERGE: the `AND [Name] <> [Name]` compares each
row's value before updating, so an unchanged row is never rewritten and a no-op redeploy touches nothing.
An unconditional `WHEN MATCHED` would rewrite all three rows on every deploy and still *look* correct —
that is the failure this guard avoids.

This was proven objectively against a **Twin** — a disposable SQL Server 2022 database published from
this estate and filled with real-shaped synthetic data. The edited MERGE was run directly against the
live seeded table so the `WHEN MATCHED` UPDATE branch actually fired and its row count could be measured,
then re-run for the silence proof, then landed through the estate lane end-to-end. No work item was
provided with the request; attach one before merge so the record is traceable.

## Review & release

- **Any team member can review this**: a seed label is amended and the running application is unaffected.
  The change is additive to history — the row keeps its identity (`Id = 3`), so every reference to it
  stays valid; only the display label changes.
- **Ships as one release**: the seed MERGE in the post-deployment script re-runs and amends the one
  changed row. The table definition is unchanged.
- Added scrutiny: none — the guarded `WHEN MATCHED` is the standard idempotency requirement, updating
  only the changed row so a no-op redeploy touches zero rows.
- **This is a label change, not a retirement.** Removing a value the app references is a different
  operation — see `delete-seed-value.md` (deactivate, don't delete).

## Changes

| File | Change |
|---|---|
| `Data/StaticSeeds.sql` | Amends the `Status` MERGE `VALUES`: `(3, N'Pending')` → `(3, N'On Hold')` |

No renames (the refactorlog is unchanged). No table, index, or column change — only one seed label in the
post-deployment MERGE.

## Data remediation

None. The amendment rides the same guarded MERGE that already governs the `Status` seed: the changed row
is updated in place through `WHEN MATCHED`, its Id and every foreign-key reference preserved. No fact row
is touched — `Order` and `Customer` rows that point at `StatusId = 3` still point at the same row, now
labelled `On Hold`.

## Deployment evidence — objective proof, live Twin (SQL Server 2022), 2026-07-22

The proof is a green integration test that seeds `Status` on a live Twin, runs the **edited** guarded
MERGE directly so the `WHEN MATCHED` UPDATE branch fires and its rowcount is captured, re-runs it for the
silence proof, then converges the edit through the estate lane. The content-hash is an order-sensitive
`SHA2_256` over the table's `FOR XML RAW` projection.

**Test:** `Twin.Tests.Integration.SamplePrSeedTests+SamplePrSeedTests.edit-seed: the guarded WHEN MATCHED UPDATE changes exactly one row and the re-run is silent (0 rows + identical hash)`

```
Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 59 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact 1 — the guarded UPDATE changes exactly one row.** `Status` held Open / Closed / Pending. The
edited MERGE updated **1 row** (Id 3), and Ids 1 and 2 — whose labels did not change — were **not**
touched; the guard compared each value and skipped the equal ones. Verbatim from the run:

```
baseline: Status rows=3, Id=3 Name='Pending', content-hash=8F101F0463B756281E92DCB6824442BDD4B667654AB00CAD1C61207C8ACD098E
edited guarded MERGE (Pending -> On Hold): rows affected = 1 (1 = only the changed row, NOT the table of 3)
  Id=1 Name='Open' (unchanged), Id=2 Name='Closed' (unchanged), Id=3 Name='On Hold' (changed), rows=3
  content-hash after edit = 32074A97DDBFDEEA7E05C58315D0895479622683250E84895D4AF073CA1A7D7D (differs from baseline=true)
```

`rows affected = 1`, not 3 — the one-row discipline that keeps the edit from registering as a table-wide
rewrite. The content-hash shifted (the label genuinely changed), and no row count changed (no insert, no
delete).

**Fact 2 (the signature proof) — the re-run is silent.** The identical edited MERGE, run a second time,
touched **0 rows** and left a **byte-identical content-hash**. Landed through the estate lane, the first
convergence `Materialized` the new label and the second reported **`NothingToApply`** — the lane
redeploy is a no-op. Verbatim:

```
SECOND run of the identical edited MERGE: rows affected = 0 (0 = idempotent no-op), content-hash = 32074A97DDBFDEEA7E05C58315D0895479622683250E84895D4AF073CA1A7D7D (identical=true)
estate lane: first converge with the edit on disk = Materialized, Id=3 Name='On Hold', hash=32074A97DDBFDEEA7E05C58315D0895479622683250E84895D4AF073CA1A7D7D
  SECOND converge = NothingToApply (silent), hash=32074A97DDBFDEEA7E05C58315D0895479622683250E84895D4AF073CA1A7D7D (identical=true)
```

The lane-seeded content-hash matches the direct-edit content-hash exactly, and both re-runs are silent —
the edit lands once, then stays put.

## Verification — run in each environment after deployment

```sql
-- expect one row, the new label present on the same Id: the value changed in place
SELECT Id, Name FROM dbo.Status WHERE Id = 3;   -- 3, On Hold

-- expect every fact row that pointed at Id 3 still resolves to a live row (nothing orphaned)
SELECT COUNT(*) FROM dbo.[Order]  WHERE StatusId = 3;
SELECT COUNT(*) FROM dbo.Customer WHERE StatusId = 3;

-- redeploy the post-deploy seed a second time: it must report 0 rows affected.
```

## Rollback

Lossless — revert the `VALUES` entry (`(3, N'On Hold')` → `(3, N'Pending')`) and redeploy. The amendment
reverts through the same guarded `WHEN MATCHED`, which sets the row's `Name` back; the Id and every
reference are untouched throughout. Backing the change out was not exercised.

## Not verified

- **Application impact.** Code paths that switch on the exact label text — a screen bound to the list,
  logic that resolves the value by label rather than by Id — are not exercised on the disposable copy;
  the app owner confirms them. (Code that resolves by `Id` keeps working: the Id did not change.)
- **Other environments.** Whether Test, UAT, or Prod already hold this Id with a different label is
  unknown from the disposable copy of Dev; run the verification query before promotion.
- **Reversibility.** The forward edit is proven (one-row update, silent re-run); backing it out was not
  exercised.
