# Status: retire a lookup value (the seed is additive — removing a value does NOT delete its row; the explicit delete is FK-guarded, and the discipline is IsActive = 0)

**In OutSystems** — You retire a record from the `Status` **Static Entity** — you want the app to stop offering the value.
**In SSDT** — You drop the entry from the `VALUES` block of the seed MERGE (`Data/StaticSeeds.sql`). That does **not** delete the row. The generated seed is **additive**: it inserts new rows (`WHEN NOT MATCHED BY TARGET`) and updates changed ones (`WHEN MATCHED`), but carries **no** `WHEN NOT MATCHED BY SOURCE … DELETE` arm — so a value you remove from `VALUES` simply stops being asserted, and its existing row **stays**. Actually removing the row is a **separate, explicit** step (a manual `DELETE`, or a scoped delete), and the foreign key guards it: a **referenced** value cannot be hard-deleted (`Msg 547`). The standing discipline is **deactivate, don't delete** (`IsActive = 0`).

## Summary

You ask to retire a `Status` value. The first thing to get right is a fact about the seed itself, not
about the data: **removing a value from the seed `VALUES` does not remove the row.** The seed the engine
generates is additive — it only ever inserts missing rows and updates changed ones. It has **no**
table-wide `WHEN NOT MATCHED BY SOURCE … DELETE` branch, and it will not grow one by default: a row that
is absent from the seed may still be referenced, so pruning is a deliberate, bounded operator decision,
not an automatic side effect of a redeploy. Drop the value from `VALUES` and the row you meant to retire
is still there on the next deploy — the seed has simply stopped asserting it.

To actually remove the row you take a **separate, explicit step** — and whether that is safe is a
property of the *data*, not of the `.sql`: does any fact row still reference the value? This PR proves
both outcomes objectively on a disposable copy. An **unused** value deletes cleanly with a plain `DELETE`,
and the re-run is idempotent (zero rows — it is already gone). A **referenced** value — one that
`Customer` or `Order` rows still point at — is **refused by the foreign key** (`Msg 547`) and the delete
rolls back with the value and every reference intact. The lesson the run teaches directly: **you cannot
hard-delete a lookup value that is still in use.** Repoint or remove the references first, or — the safer
standing pattern — **deactivate the value with `IsActive = 0`** so its Id and history are preserved and
nothing is ever orphaned.

This was proven objectively against a **Twin** — a disposable SQL Server 2022 database published from
this estate and filled with real-shaped synthetic data, carrying the estate's own **additive** guarded
seed. No work item was provided with the request; attach one before merge so the record is traceable.

## Review & release

- **Prefer deactivate over delete.** The standing discipline is `IsActive = 0`, not a hard `DELETE`: it
  retires the value in place while preserving its Id, so every reference stays valid and nothing is
  orphaned. A hard `DELETE` that would orphan fact rows is refused by the foreign key — proven below — and
  is usually the wrong tool for a value the app has ever used.
- **A dev lead or an experienced developer should review retiring a value the app offers**: the value
  leaves the set the running application presents, so someone must confirm no active flow depends on it.
  Simply removing a value from the seed `VALUES` (leaving the row in place) is low-risk; the explicit
  delete is the referential decision.
- **Ships as one release**: the seed MERGE re-runs (additive — it leaves the retired row untouched); the
  actual retirement is the separate `IsActive = 0` (preferred) or the explicit `DELETE` of the unused row.
  The table definition is unchanged.
- **This is a retirement, not a label change.** Renaming a value in place is a different, lighter
  operation — see `edit-seed.md`.

## Changes

| File | Change |
|---|---|
| `Data/StaticSeeds.sql` | Removes the retired value from the `Status` MERGE `VALUES`. The seed is **additive**, so this stops asserting the value but does **not** delete its existing row on deploy. |

No renames (the refactorlog is unchanged). No table, index, or column change — only the seed's `VALUES`
set changes. **Removing the row itself is a step outside the seed**: preferred `IsActive = 0`, or an
explicit, bounded `DELETE` of the (unused) row — never a table-wide delete arm bolted onto the seed.

## Data remediation

The gate is the **reference probe**, run before any explicit delete: `SELECT COUNT(*) FROM dbo.[Order]
WHERE StatusId = <id>` (and the same for `Customer`). Nonzero means a hard `DELETE` would orphan those
fact rows and is refused by the foreign key — deactivate instead (`IsActive = 0`), or repoint the
references first. On the copy both outcomes were exercised: an unreferenced value deleted cleanly and
idempotently; a referenced value was blocked (`Msg 547`) and rolled back.

## Deployment evidence — objective proof, live Twin (SQL Server 2022), 2026-07-22

The proof is a green integration test that seeds `Status` on a live Twin, points every fact row at
`Open (1)` (leaving `Pending (3)` unreferenced), then: (1) removes `Pending` from the seed `VALUES` and
runs the **emitted additive MERGE directly against the live rows** — the exact statement a real
post-deploy runs — and shows the row **survives**; (2) runs an explicit `DELETE` of the now-unused
`Pending` and re-runs it; (3) runs an explicit `DELETE` of the referenced `Open` and reads the foreign
key's refusal.

**Test:** `Twin.Tests.Integration.SamplePrSeedTests+SamplePrSeedTests.delete-seed-value: removing a value from the additive seed does NOT delete the row; the explicit, FK-guarded removal blocks a referenced value (Msg 547)`

```
Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 51 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact 1 (the one that matters) — removing a value from the seed does NOT delete the row.** With `Pending
(3)` unreferenced, it was removed from the seed `VALUES` and the emitted **additive** MERGE was run
against the live `{Open, Closed, Pending}` table. It touched **0 rows** — there is no `WHEN NOT MATCHED BY
SOURCE` arm to fire — and `Pending` was **still there**. A seed carrying a table-wide delete arm would
have dropped it here; the additive seed does not. Verbatim from the run:

```
baseline: Status rows=3 {Open,Closed,Pending}; references to Open(1)=50 (referenced), to Pending(3)=0 (unreferenced)
REMOVE 'Pending' from the seed VALUES; run the emitted additive MERGE against the live {Open,Closed,Pending} table: rows affected=0 (0 = no WHEN NOT MATCHED BY SOURCE DELETE arm — a delete-arm seed would have dropped Pending here), Id=3 present=1 (1 = SURVIVED), Status rows=3 (still 3) -> removing a value from the seed does NOT drop the row
```

`rows affected = 0` and `Status rows = 3` — the retired value's row persists. Removing it from the seed
only stops the seed *asserting* it; it does not remove it.

**Fact 2 — the explicit delete of an UNUSED value succeeds and is idempotent.** With `Pending (3)` still
unreferenced, an explicit `DELETE FROM dbo.Status WHERE Id = 3` removed **1 row** (`Status` 3 → 2). The
identical delete, run again, touched **0 rows** — it is already gone. Verbatim:

```
explicit DELETE of the now-UNUSED Pending(3) (references=0): rows affected=1 (1 = removed), Id=3 present=0 (0 = gone), Status rows=2 (3 -> 2)
  SECOND identical DELETE (Pending already gone): rows affected=0 (0 = idempotent), Status rows=2
```

**Fact 3 (the guard) — the explicit delete of a REFERENCED value is BLOCKED by the foreign key.** With
`Open (1)` still referenced by all 50 fact rows, an explicit `DELETE FROM dbo.Status WHERE Id = 1` was
**refused (`Msg 547`)** and rolled back — `Open` survived and `Status` was left intact. Verbatim:

```
explicit DELETE of the REFERENCED Open(1) (references=50): Msg 547, Line 1: The DELETE statement conflicted with the REFERENCE constraint "FK_Customer_Status". The conflict occurred in database "twin", table "dbo.Customer", column 'StatusId'.
The statement has been terminated.
  after the blocked delete: Open(1) present=1 (1 = survived, rolled back), Status rows=2 (intact)
```

`Msg 547` is the objective proof: **a lookup value still in use cannot be hard-deleted.** That is exactly
why the standing discipline is `IsActive = 0` — it retires the value in place without ever attempting the
delete the foreign key would refuse.

## Verification — run in each environment after deployment

```sql
-- BEFORE removing a value — expect the count of fact rows still pointing at it. Nonzero means a hard
-- DELETE would be refused by the FK; deactivate instead, or repoint these references first.
SELECT (SELECT COUNT(*) FROM dbo.[Order] WHERE StatusId = <id>)
     + (SELECT COUNT(*) FROM dbo.Customer WHERE StatusId = <id>) AS refs;

-- AFTER only removing the value from the seed VALUES — expect the row to STILL be present: the additive
-- seed does not delete it.
SELECT COUNT(*) FROM dbo.Status WHERE Id = <id>;   -- 1 (still there)

-- AFTER a deactivate (preferred) — expect IsActive = 0: the value is retired in place, not deleted.
-- SELECT IsActive FROM dbo.Status WHERE Id = <id>;

-- AFTER an explicit DELETE of an unused value — expect 0 rows: it is gone and nothing was orphaned.
SELECT COUNT(*) FROM dbo.Status WHERE Id = <id>;   -- 0
```

## Rollback

- **Removing a value from the seed** is trivially reversible and never touched the row: put the entry back
  in `VALUES` and the seed re-asserts it (and re-inserts it through `WHEN NOT MATCHED BY TARGET` in any
  environment where it was later deleted).
- **Deactivate (`IsActive = 0`)** is reversible without data loss: set `IsActive = 1` and redeploy — the
  row was never deleted, so its Id and every reference stay intact.
- **An explicit DELETE of an unused value** re-inserts through the seed's `WHEN NOT MATCHED BY TARGET` when
  the value is put back in `VALUES` — lossless for a lookup whose Id and label are model data. A hard
  `DELETE` of a *referenced* value cannot happen (the FK refuses it), which is the failure this retirement
  avoids. Backing the change out was not exercised.

## Not verified

- **Application impact.** Any screen or logic that offers the value stops offering it once retired; code
  that resolves it by Id keeps working only if the row still exists (removed-from-seed, or deactivate) —
  not if it was explicitly deleted. Which paths the running application exercises is not confirmed on the
  disposable copy; the app owner owns it.
- **Other environments.** Test, UAT, and Prod may hold additional fact rows referencing the value, or
  carry it under a different Id. The disposable copy of Dev cannot show this — run the reference probe in
  each before an explicit delete; a value unused in Dev may be referenced elsewhere and blocked there.
- **The `IsActive` column.** This estate's `Status` lookup has no `IsActive` column, so the preferred
  deactivate path is described, not exercised; the objective proof here is that the seed does not delete
  and the FK guard that makes deactivation the right call. Adding `IsActive` is an `add-default` change.
- **Reversibility.** The forward paths are proven (the additive seed leaves the row; a clean idempotent
  delete of an unused value; the FK block of a referenced one); backing the changes out was not exercised.
