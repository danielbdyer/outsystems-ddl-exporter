# OrderLine → Order: change the Delete Rule to Delete / cascade (a clean metadata change; the risk is behavioural, not the publish)

**In OutSystems** — You change the *Delete Rule* on the `OrderLine`→`Order` reference from **Protect** to **Delete**: from now on, deleting an `Order` also deletes its `OrderLine` records instead of being blocked.
**In SSDT** — the foreign key `FK_OrderLine_Order` gains `ON DELETE CASCADE` in `Tables/dbo.OrderLine.sql`. You cannot alter a delete action in place, so SSDT **drops and re-adds** the foreign key to set it.

## Summary

You change the reference's Delete Rule to *Delete* (cascade). In OutSystems the flag flips and the platform
handles it; here the same intent is `ON DELETE CASCADE` on the foreign key, and the publish is **clean** — a
drop-and-re-add of the constraint touches no rows and is never blocked by the data. The thing to be careful
about is **runtime behaviour, not the deployment**: a delete that used to *fail* now *silently removes rows in
a second table*, and in a deeper reference graph it would chain further. This was proven objectively against a
Twin — a disposable SQL Server database published from this estate and filled with real-shaped synthetic data —
with a **production-faithful** publish (`BlockOnPossibleDataLoss = true`, the deployment a real environment
runs). No work item was provided with the request; attach one before merge so the record is traceable.

**A clean publish is not the same as a safe change.** How smoothly this deploys tells you nothing about whether
a delete will now cascade through your tables. The first time someone deletes an `Order`, its `OrderLine`
records go with it — that is the point of the change, but it is the part to review, not the deployment.

## Review & release

- A dev lead must review this: changing the Delete Rule toward *Delete* (`CASCADE`) alters runtime behaviour so
  that deleting a parent `Order` silently removes its child `OrderLine` rows in another table.
- It ships as a single schema change, applied in place: the foreign key is dropped and re-added to set its
  `ON DELETE` action. No existing data is modified by the publish. No gate relaxation, no staging.
- Map the full cascade graph before shipping. Here the chain is one hop (`Order` → `OrderLine`); in a deeper
  reference graph a single parent delete could chain through several tables. Confirm the blast radius is what
  you intend.
- Added scrutiny: first time this operation is proven on the Twin. At production row counts a large cascade may
  remove many rows and run long or block writes — schedule a window.

## Changes

| File | Change |
|---|---|
| `Tables/dbo.OrderLine.sql` | Adds `ON DELETE CASCADE` to `CONSTRAINT [FK_OrderLine_Order]` |

No renames (the refactorlog is unchanged). No index, view, or procedure changes. Only the foreign key's
`ON DELETE` action changes; the key columns, the referenced table, and every column are untouched.

## Data remediation

None required — the publish modifies no data. The drop-and-re-add of the foreign key is a metadata operation.
The behaviour it turns on (cascading deletes) is a *runtime* concern the application owner must sign off on, not
a data fix (named under Not verified).

## Deployment evidence — objective proof, production-faithful publish, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, materializes real-shaped data,
applies the delete-rule change under a **production-faithful** DacFx posture (`BlockOnPossibleDataLoss = true`,
no smart-defaults), and both asserts the metadata delta and exercises the cascade's runtime scope on a
rolled-back parent delete. DacFx is the same publish engine `sqlpackage` wraps.

**Test:** `Twin.Tests.Integration.SamplePrSchemaChangeTests+SamplePrSchemaChangeTests.change-delete-rule: an FK ON DELETE NO ACTION -> CASCADE is a clean metadata DROP+ADD`

```
Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 1 m 22 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact — the publish is a clean metadata DROP+ADD, and the delete rule flips from Protect to Delete.** Verbatim
from the run:

```
baseline: OrderLine rows=25, FK_OrderLine_Order exists=1, delete_referential_action=0 (NO_ACTION), object_id=1077578877
  busiest parent Order has 4 child lines; deleting that parent under NO ACTION returned SQL error number=547 (547 = FK conflict = BLOCKED / Protect)
production publish (BlockOnPossibleDataLoss=true) ALTER FK_OrderLine_Order ON DELETE NO ACTION -> CASCADE [SSDT DROP+ADD]: APPLIED (Ok)
  after apply: FK exists=1, delete_referential_action=1 (CASCADE), object_id=1141579105 (was 1077578877 -> changed=true, proves DROP+ADD), OrderLine rows=25 (intact)
  cascade scope (rolled back): deleting the busiest parent Order removed 4 child OrderLine rows (Delete); OrderLine rows after rollback=25 (fully restored)
```

Reading the facts:
- **Before (Protect).** The foreign key existed with `delete_referential_action = 0` (`NO_ACTION`). Deleting a
  parent `Order` that had child lines was **blocked** — SQL Server raised error **547** (foreign-key conflict).
  That is exactly the *Protect* behaviour: you cannot delete the parent while children exist.
- **The publish applied clean (`Ok`)** under `BlockOnPossibleDataLoss = true`. The `object_id` changed
  (`1077578877` → `1141579105`), which **proves SSDT dropped and re-added** the constraint rather than altering
  it in place. `OrderLine` held **25 rows** before and **25 rows** after — the schema change touched no data.
- **After (Delete).** `delete_referential_action = 1` (`CASCADE`). Deleting the busiest parent `Order` now
  **removed its 4 child `OrderLine` rows** — the cascade in action. That probe ran inside a transaction that was
  rolled back, so the table returned to **25 rows** intact; the number 4 is the measured blast radius, not a
  permanent change.

## Verification — run in each environment after deployment

```sql
-- expect CASCADE: the delete rule landed as specified
SELECT name, delete_referential_action_desc
FROM sys.foreign_keys
WHERE name = 'FK_OrderLine_Order';

-- optional, on a disposable copy only: confirm the cascade scope before trusting it in production.
-- Deleting one Order removes its OrderLines; run inside a transaction you ROLL BACK.
BEGIN TRAN;
  DELETE FROM dbo.[Order] WHERE Id = (SELECT TOP 1 OrderId FROM dbo.OrderLine);
ROLLBACK;   -- never commit this on real data
```

## Rollback

Revert the `.sql` edit and republish; SSDT drops and re-adds the foreign key with its previous `NO ACTION`
delete rule. Lossless at the schema level, because the publish modifies no data. **Not auto-reversible in
substance:** any child `OrderLine` rows a cascade has *already* removed in a live environment are gone, and
restoring the previous rule does not bring them back — those come from a backup. Backing the change out was not
exercised here.

## Not verified

- **Application impact.** Any code path that relied on the delete being *blocked* (Protect) now **succeeds** and
  removes child rows; a screen or action that deletes an `Order` will now silently delete its `OrderLine`
  records. The running application's delete behaviour is not confirmed here — the application owner owns closing
  this before promotion.
- **Cascade depth.** The disposable copy proves the seeded one-hop chain (`Order` → `OrderLine`, 4 rows removed).
  Deeper chains in the real reference graph are not exercised until the full cascade is mapped — do that before
  shipping.
- **Other environments.** Test, UAT, and Prod hold different `Order`/`OrderLine` volumes; the specific rows a
  cascade removes there are not visible from this copy. The rule change itself applies identically.
- **Production scale and timing.** A large cascade may remove many rows and run long or block writes at
  production row counts; the small copy does not show it. Schedule a window.
- **Reversibility.** The forward change is proven (clean publish + cascade scope); backing it out, and the loss
  of any already-cascaded rows, was not exercised.
