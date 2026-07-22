# Order: change the Channel default from Web to Store (future inserts only; existing rows keep their value)

**In OutSystems** — You change the default value of the `Channel` Attribute on the `Order` Entity from `Web` to `Store`, so new orders that don't specify a channel come in as `Store`.
**In SSDT** — the named default in `Tables/dbo.Order.sql` changes from `CONSTRAINT [DF_Order_Channel] DEFAULT (N'Web')` to `DEFAULT (N'Store')`. SSDT implements a default *change* as a DROP-then-ADD of the constraint — it drops the old default and adds the new one — and touches no existing row.

## Summary

You change the default channel for new orders from `Web` to `Store`. This is a **schema-only change**:
SSDT drops the old `DF_Order_Channel` constraint and adds the new one, and **no existing row is touched**.
A default is a rule about *future* inserts — orders already in the table were written under the old rule
and keep exactly the value they were written with. This was proven objectively against a Twin — a
disposable SQL Server database published from this estate and filled with real-shaped synthetic data —
with a **production-faithful** publish (`BlockOnPossibleDataLoss = true`, the deployment a real
environment runs). No work item was provided with the request; attach one before merge so the record is
traceable.

**The nuance this change lives or dies on: changing a default does not rewrite history.** The proof shows
it directly — a row written *before* the change under the old default kept `Web`, a row written *after*
the change got `Store`, and every pre-existing row's `Channel` was byte-for-byte unchanged. If you also
need the old orders re-stamped to `Store`, that is a **separate** operation — a post-deployment backfill
`UPDATE`, proven the same way — and is deliberately **not** part of this change (named under Not
verified). Expecting a changed default to reach back and rewrite yesterday's rows is the one surprise this
PR exists to prevent.

## Review & release

- Any team member can review this, in any data state: no existing row values change — a default governs
  only future inserts.
- It ships as a single schema change, applied in place: SSDT does a DROP-then-ADD of `DF_Order_Channel`
  (a brief no-default window inside the deploy transaction). No table rebuild, no row updates, no staging.
- The constraint is **named** (`DF_Order_Channel`). Insist on this — an unnamed default
  (`DF__Order__Channel__<hash>`) gets a different name per environment, which makes the DROP-then-ADD
  fragile: the deploy can't find the constraint it means to drop.
- Added scrutiny: none — a modified default changes no existing row values.

## Changes

| File | Change |
|---|---|
| `Tables/dbo.Order.sql` | Changes `[Channel]`'s named default from `CONSTRAINT [DF_Order_Channel] DEFAULT (N'Web')` to `DEFAULT (N'Store')` |

No renames (the refactorlog is unchanged). No index, view, or procedure changes. Only the default's
*definition* changes — the column's name, type (`NVARCHAR(20)`), nullability, and every existing row are
untouched.

## Data remediation

None — deliberately. Existing orders keep the `Channel` they were written with; a changed default never
re-stamps them. If the business needs the old orders moved to `Store` as well, that is a separate,
idempotent post-deployment `UPDATE` (a backfill), proven on its own — see `add-default` and the
idempotent-seed pattern. It is not bundled here, so this change stays a clean schema-only default swap.

## Deployment evidence — objective proof, production-faithful publish, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, materializes real-shaped
data, adds the named default (`Web`) and converges, then changes the default's value to `Store` and
asserts the outcome under a **production-faithful** DacFx posture (`BlockOnPossibleDataLoss = true`, no
smart-defaults). DacFx is the same publish engine `sqlpackage` wraps.

**Test:** `Twin.Tests.Integration.SamplePrCleanApply2Tests+SamplePrCleanApply2Tests.modify-default: changing a default's value applies clean and leaves existing rows unchanged`

```
Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 1 m 16 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact — the default's value changes, future inserts see the new value, and existing rows keep the old.**
`Order` held **25 rows** with `Channel` filled by the data mint (not the default), and the named default
was `(N'Web')`. A row was inserted under the old default and came in as `Web`. The production-faithful
publish then changed the default to `(N'Store')`; a row inserted afterward came in as `Store`, while the
row written under the old default **kept `Web`** and every minted row's `Channel` was unchanged (identical
aggregate digest). Verbatim from the run:

```
baseline: DF_Order_Channel exists=1, definition=(N'Web'); Order minted rows=25 (Channel filled by the mint, not the default), max minted Id=25, minted-Channel digest=586602991
row Id=26 inserted under the OLD default: Channel=Web
production publish (BlockOnPossibleDataLoss=true) modify DF_Order_Channel DEFAULT (N'Web') -> (N'Store') [SSDT drop-then-add]: APPLIED (Ok)
  after apply: DF_Order_Channel exists=1, definition=(N'Store') (was (N'Web'))
  FUTURE inserts: row Id=27 written after the change carries Channel=Store (the NEW default)
  EXISTING rows unchanged: row Id=26 (written under the old default) still Channel=Web; minted-Channel digest 586602991 -> 586602991
```

The publish returned `Ok` under the production-faithful posture — a DROP-then-ADD of a default touches no
row, so there is no data condition to violate. After the apply: `DF_Order_Channel` still exists but its
definition is now `(N'Store')` (was `(N'Web')`); a **future** insert (row `Id = 27`) received `Store`;
the row written under the old default (row `Id = 26`) **still reads `Web`**; and the 25 minted rows'
`Channel` values are unchanged (aggregate digest `586602991 → 586602991`). That is the whole teaching in
one run: the new default governs only what gets written next.

## Verification — run in each environment after deployment

```sql
-- expect 1 row carrying the new default definition (N'Store')
SELECT dc.name, c.name AS column_name, dc.definition
FROM sys.default_constraints dc
JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
WHERE dc.name = 'DF_Order_Channel';

-- optional — confirm existing rows were NOT re-stamped (this change leaves them as written):
-- the count of non-Store channels among rows that predate the change is unchanged by this deploy.
SELECT Channel, COUNT(*) AS rows FROM dbo.[Order] GROUP BY Channel;
```

## Rollback

Lossless: no existing rows change either way. Backing out re-creates `DF_Order_Channel` with its previous
definition — `DEFAULT (N'Web')`. Record the prior definition (it was `(N'Web')`) so the restore is exact.
Backing the change out was not exercised.

## Not verified

- **Application impact.** Inserts that omit `Channel` now receive `Store` instead of `Web`; whether any
  code path relied on the old default is not confirmed here — the application owner owns it.
- **Other environments.** A default created unnamed (`DF__Order__Channel__<hash>`) or by an ad-hoc script
  in Test / UAT / Prod would carry a different name and could defeat the DROP-then-ADD; the disposable
  copy of Dev cannot see it. Run the verification query before promotion.
- **Retro re-stamp.** Existing rows are deliberately left as written. If the new value must also apply to
  old orders, that is a separate, proven backfill and is not part of this change.
