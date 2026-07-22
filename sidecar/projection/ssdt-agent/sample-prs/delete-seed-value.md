# Status: remove a lookup value (the hard DELETE is clean when the value is unused, and blocked by the foreign key when it is referenced)

**In OutSystems** — You retire a record from the `Status` **Static Entity** — you want the value gone from the list the app offers.
**In SSDT** — you drop the entry from the `VALUES` block of the seed MERGE, and its `WHEN NOT MATCHED BY SOURCE THEN DELETE` branch removes the row on the next deploy. **A hard DELETE is safe only when nothing points at the value** — the discipline is *deactivate, don't delete* (`IsActive = 0`) precisely because the foreign key refuses to let a referenced lookup row be deleted.

## Summary

You ask to remove a `Status` value. Whether that is safe is **not** a property of the `.sql` — it is a
property of the *data*: does any fact row still reference the value? This PR proves both outcomes
objectively on a disposable copy. An **unused** value deletes cleanly through the seed's `WHEN NOT
MATCHED BY SOURCE THEN DELETE`, and the re-run is silent (zero rows, identical content-hash). A
**referenced** value — one that `Customer` or `Order` rows still point at — is **refused by the foreign
key** (`Msg 547`), and the whole MERGE rolls back with the value and every reference intact. The lesson
the run teaches directly: **you cannot delete a lookup value that is still in use.** Remove the
references first (as proven here), or — the safer standing pattern — **deactivate the value with
`IsActive = 0`** so its identity and history are preserved and nothing is ever orphaned.

This was proven objectively against a **Twin** — a disposable SQL Server 2022 database published from
this estate and filled with real-shaped synthetic data, with the estate's own guarded MERGE (which
carries the `WHEN NOT MATCHED BY SOURCE THEN DELETE` branch). No work item was provided with the request;
attach one before merge so the record is traceable.

## Review & release

- **A dev lead or an experienced developer should review a retirement of a referenced value**: the value
  leaves the set the running application offers, so someone must confirm no active flow depends on it. A
  value nothing references can be reviewed by any team member.
- **Prefer deactivate over delete.** The standing discipline is `IsActive = 0`, not a hard DELETE: it
  retires the value in place while preserving its Id, so every reference stays valid and nothing is
  orphaned. A hard DELETE that orphans fact rows removes data irreversibly and is usually **wrong** — the
  foreign key proven below is exactly why.
- **Ships as one release**: the seed MERGE re-runs and either removes the (unused) row or sets its
  `IsActive = 0`. The table definition is unchanged.
- Added scrutiny: none for an unused value; removing a referenced one is a referential decision, reviewed
  as above.

## Changes

| File | Change |
|---|---|
| `Data/StaticSeeds.sql` | Removes the retired value from the `Status` MERGE `VALUES` (its `WHEN NOT MATCHED BY SOURCE THEN DELETE` branch then deletes the row) — or, preferred, sets `IsActive = 0` for it |

No renames (the refactorlog is unchanged). No table, index, or column change — only the seed's `VALUES`
set changes.

## Data remediation

The gate is the **reference probe**, run before choosing: `SELECT COUNT(*) FROM dbo.[Order] WHERE
StatusId = <id>` (and the same for `Customer`). Nonzero means a hard DELETE orphans those fact rows and
is refused by the foreign key — deactivate instead, or repoint the references first. On the copy both
outcomes were exercised: an unreferenced value deleted cleanly; a referenced value was blocked until its
references were removed.

## Deployment evidence — objective proof, live Twin (SQL Server 2022), 2026-07-22

The proof is a green integration test that seeds `Status` on a live Twin, forces the fact rows into a
known reference shape, then runs the seed with a value removed — once for an **unused** value and once
for a **referenced** value — and asserts each outcome by consuming the data. The content-hash is an
order-sensitive `SHA2_256` over the table's `FOR XML RAW` projection.

**Test:** `Twin.Tests.Integration.SamplePrSeedTests+SamplePrSeedTests.delete-seed-value: an unused value deletes cleanly and idempotently; a referenced value is blocked by the FK (Msg 547) until the reference is removed`

```
Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 59 s - Twin.Tests.Integration.dll (net9.0)
```

Setup: every `Customer` and `Order` row was pointed at `Open (1)`, leaving `Pending (3)` unreferenced.

**Fact 1 — an UNUSED value deletes cleanly and idempotently.** With `Pending (3)` unreferenced, removing
it from the seed `VALUES` fired the `WHEN NOT MATCHED BY SOURCE THEN DELETE` branch: **1 row** deleted,
`Status` 3 → 2. The re-run touched **0 rows** with a **byte-identical content-hash**. Verbatim from the
run:

```
baseline: Status rows=3; references to Open(1)=50, to Pending(3)=0 (unreferenced)
CASE A (delete UNUSED Pending): seed re-run with Pending removed -> WHEN NOT MATCHED BY SOURCE THEN DELETE, rows affected=1 (1), Id=3 present=0 (0 = deleted), Status rows=2, hash=2D0228AB3CC9FC3754D5D39BEDDBFD88DA5EFD6B7C933B779354D7266087B371
  SECOND run (Pending already gone): rows affected=0 (0 = idempotent), hash=2D0228AB3CC9FC3754D5D39BEDDBFD88DA5EFD6B7C933B779354D7266087B371 (identical=true)
```

**Fact 2 (the discovered guard) — a REFERENCED value is BLOCKED by the foreign key.** With `Open (1)`
still referenced by all 50 fact rows, removing it from the seed `VALUES` made the DELETE branch try to
delete a referenced lookup row — the foreign key **refused it (`Msg 547`)** and the whole MERGE rolled
back, all three values intact. Removing the references first let the identical delete succeed (1 row),
and its re-run was silent. Verbatim:

```
CASE B setup: Status restored to rows=3; references to Open(1)=50 (still referenced)
CASE B (delete REFERENCED Open): seed re-run with Open removed -> the DELETE is REFUSED by the FK: Msg 547, Line 1: The MERGE statement conflicted with the REFERENCE constraint "FK_Customer_Status". The conflict occurred in database "twin", table "dbo.Customer", column 'StatusId'.
The statement has been terminated.
  after the blocked delete: Open(1) present=1 (1 = survived, rolled back), Status rows=3 (all 3 intact)
  remedy: repoint references off Open(1) -> references now=0; the SAME delete now succeeds, rows affected=1 (1), Open(1) present=0 (0 = deleted), Status rows=2
  SECOND run (Open already gone): rows affected=0 (0 = idempotent), hash=DED7A5BFBD7126DD5103C5CE5286A079A710CA408AD25198EDE41DB39F861288 (identical=true)
```

`Msg 547` is the objective proof: **a lookup value still in use cannot be hard-deleted.** That is exactly
why the standing discipline is `IsActive = 0` — it retires the value without ever attempting the delete
the foreign key would refuse.

## Verification — run in each environment after deployment

```sql
-- BEFORE removing a value — expect the count of fact rows still pointing at it. Nonzero means a hard
-- DELETE is refused by the FK; deactivate instead, or repoint these references first.
SELECT (SELECT COUNT(*) FROM dbo.[Order] WHERE StatusId = <id>)
     + (SELECT COUNT(*) FROM dbo.Customer WHERE StatusId = <id>) AS refs;

-- AFTER a deactivate (preferred) — expect IsActive = 0: the value is retired in place, not deleted
-- SELECT IsActive FROM dbo.Status WHERE Id = <id>;

-- AFTER a delete of an unused value — expect 0 rows: it is gone and nothing was orphaned
SELECT COUNT(*) FROM dbo.Status WHERE Id = <id>;
```

## Rollback

- **Deactivate (`IsActive = 0`)** is reversible without data loss: set `IsActive = 1` and redeploy — the
  row was never deleted, so its identity and every reference stay intact.
- **A hard DELETE of an unused value** re-inserts through the seed's `WHEN NOT MATCHED BY TARGET THEN
  INSERT` when the value is put back in `VALUES` — lossless for a lookup whose Id and label are model
  data. A hard DELETE of a *referenced* value cannot happen (the FK refuses it), which is the failure this
  retirement avoids. Backing the change out was not exercised.

## Not verified

- **Application impact.** Any screen or logic that offers the value stops offering it once retired; code
  that resolves it by Id keeps working only if the row still exists (deactivate) — not if it was deleted.
  Which paths the running application exercises is not confirmed on the disposable copy; the app owner
  owns it.
- **Other environments.** Test, UAT, and Prod may hold additional fact rows referencing the value, or
  carry it under a different Id. The disposable copy of Dev cannot show this — run the reference probe in
  each before promotion; a value unused in Dev may be referenced elsewhere and blocked there.
- **The `IsActive` column.** This estate's `Status` lookup has no `IsActive` column, so the preferred
  deactivate path is described, not exercised; the objective proof here is the FK guard that makes
  deactivation the right call. Adding `IsActive` is an `add-default` change.
- **Reversibility.** The forward paths are proven (clean delete of an unused value, FK block of a
  referenced one, and the delete after remediation); backing the changes out was not exercised.
