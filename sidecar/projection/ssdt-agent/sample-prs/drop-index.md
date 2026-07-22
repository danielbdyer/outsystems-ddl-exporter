# Customer: drop the Email index (no rows are lost; the real risk is a slower query, not the publish)

**In OutSystems** — You remove an index you had added on `Customer.Email` — "we don't need that index anymore", "it's not used".
**In SSDT** — you delete the index's definition from the estate (here its own one-statement file, `Tables/dbo.Customer.IX_Email.sql`). SSDT emits `DROP INDEX`. An index is derived structure — it holds no row data — so dropping it loses nothing.

## Summary

You drop the `IX_Customer_Email` index. Dropping an index **never loses data** — an index stores no rows of its own, only a derived lookup structure — so this publishes clean every time. This was proven objectively against a Twin — a disposable SQL Server database published from this estate and filled with real-shaped synthetic data — under a **production-faithful** publish (`BlockOnPossibleDataLoss = true`, `DropObjectsNotInSource = false`, no smart-defaults).

There is a **DacFx subtlety worth knowing**, because the rest of this removal wave turns on it. The production posture sets `DropObjectsNotInSource = false` — the `sqlpackage` default that stops a deploy from dropping whole objects a developer merely stopped mentioning (it is why deleting a whole *Entity* is a "phantom" that does **not** drop the table — see `delete-entity.md`). But an **index is not gated by that switch.** DacFx governs index removal with a *separate* option, `DropIndexesNotInSource`, which defaults to **true** independently. So the index really is dropped by the declarative removal — proven below — and that is safe precisely because an index holds no rows.

The catch is entirely **behavioral, not structural**: "not used" is an assumption until the numbers back it up. If this index is quietly keeping a hot query fast, dropping it slows that query with nothing in the publish to warn anyone. That risk lives in usage evidence, not in a clean deploy. No work item was provided with the request; attach one before merge so the record is traceable.

## Review & release

- Any team member can review this **once usage evidence shows the index is unused**: no data is lost, and the drop reverses by re-creating the index.
- It ships as a single schema change, applied in place — SSDT emits `DROP INDEX`, no data is read or written, and the publish never blocks.
- Added scrutiny, when the index backs a hot query or an FK lookup: dropping it is a **silent performance regression**, so a dev lead or an experienced developer should review it — and "unused" must be proven from usage evidence (`sys.dm_db_index_usage_stats`) on a prod-shaped source first, because a disposable copy carries no production query load.

## Changes

| File | Change |
|---|---|
| `Tables/dbo.Customer.IX_Email.sql` | **Removed** — the `CREATE NONCLUSTERED INDEX [IX_Customer_Email] ON [dbo].[Customer] ([Email])` definition is deleted from the estate |

No table definition changes: `Customer`'s columns, keys, and every other object are untouched. Only the index is removed.

## Deployment evidence — objective proof, production-faithful publish, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, materializes real-shaped data with the index present, removes the index under the **production-faithful** posture (`BlockOnPossibleDataLoss = true`, `DropObjectsNotInSource = false`, no smart-defaults), and then confirms the scripted `DROP INDEX` is the equivalent lossless form. DacFx is the same publish engine `sqlpackage` wraps.

**Test:** `Twin.Tests.Integration.SamplePrRemovalTests+SamplePrRemovalTests.drop-index: removing an index publishes clean and loses no rows; the scripted DROP INDEX is lossless`

```
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 1 m 19 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact 1 — the declarative removal drops the index and touches no rows.** `Customer` held 25 rows with the index present. Removing the index from source and publishing under the production posture returned **`Ok`**: the index is gone, all 25 rows intact, every value byte-identical (the row digest is unchanged). Verbatim from the run:

```
baseline: Customer rows=25, IX_Customer_Email exists=1, Email/row digest=260219553
production publish (BlockOnPossibleDataLoss=true, DropObjectsNotInSource=false) remove IX_Customer_Email from source: APPLIED (Ok)
  DISCOVERED: index exists after=0 (0 = DROPPED declaratively by the granular DropIndexesNotInSource default; 1 = phantom survival), Customer rows=25 (was 25, intact), digest=260219553 (unchanged=true)
```

Reading the facts: `index exists after=0` — the index was **actually dropped**, even under `DropObjectsNotInSource = false`, because DacFx's `DropIndexesNotInSource` (default **true**) governs it independently. `Customer rows=25` and the unchanged digest (`260219553`) prove no row was read, written, or lost. This is the counterpoint to the phantom removals elsewhere in this wave: a *sub-object* like an index is dropped by its own default-on rule, whereas a whole *table* is protected by the off master switch.

**Fact 2 — the scripted `DROP INDEX` is the identical lossless form.** Re-creating the index and dropping it by hand behaves the same: index gone, rows and values intact. Verbatim:

```
scripted DROP INDEX [IX_Customer_Email] ON [dbo].[Customer]: index exists before=1 -> after=0 (gone), Customer rows=25 (intact), digest=260219553 (unchanged=true)
```

## Verification — run in each environment after deployment

```sql
-- BEFORE the drop, in each environment (from a prod-shaped source): expect zero
-- user_seeks / user_scans / user_lookups over a representative window — the index is unused.
-- This, not the publish, is the standard of proof for an index drop.
SELECT * FROM sys.dm_db_index_usage_stats WHERE object_id = OBJECT_ID('dbo.Customer');

-- AFTER deployment: expect 0 rows — the index is gone.
SELECT name FROM sys.indexes
WHERE object_id = OBJECT_ID('dbo.Customer') AND name = 'IX_Customer_Email';
```

## Rollback

Re-create the index from its definition (restore the `.sql` and republish, or `CREATE NONCLUSTERED INDEX [IX_Customer_Email] ON [dbo].[Customer] ([Email]);`). Lossless — the index holds no source data, only a derived structure — but re-creating it runs a write-blocking build whose duration scales with row count. Only the forward drop was exercised here.

## Not verified

- **Application impact / query performance.** A disposable copy carries no production query load, so whether any query depends on this index — and would slow down once it is gone — is not shown by the publish. Usage evidence from a prod-shaped source is what settles it (@app-owner).
- **Other environments.** Usage patterns differ by environment; zero seeks in one environment's window does not prove zero in Test, UAT, or Prod. Run the usage query in each before promotion.
- **Reversibility at scale.** Re-creating the index restores the structure, but the rebuild time and the write-blocking lock at production row counts are not measured on the small copy.
