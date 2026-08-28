# Order.Total: stop enforcing the non-negative rule (drop CK_Order_Total_NonNegative)

## Verdict
This change removes the check `CK_Order_Total_NonNegative` from `dbo.[Order]`, so the database
stops refusing a negative Total; refunds recorded as negative order totals become writable. It
ships as a single in-place schema change and never blocks. Confirm the rule is retired for
good — while the check is gone, violating rows can be written, and re-adding it later is
blocked by each one until reconciled. No work item supplied — attach one before merge.

## Intent
The developer's stated intent: the business now records refunds as negative order totals, so
the rule that Total must be zero or more no longer holds. No work item supplied — attach one
before merge.

## What changes
- `dbo.[Order]`: the constraint `CK_Order_Total_NonNegative CHECK (Total >= 0)` is removed from
  the CREATE. Columns and other constraints are unchanged.

## Before promoting
- Confirm with the product owner that negative totals are legitimate from now on — this is a
  retirement of a business rule, not a deploy convenience. A temporary suspension for a load is
  a different change (disable and re-enable, which re-validates on the way back).
- Check with the application owner whether application-side validation still refuses negative
  totals; after this lands, the database no longer does.

## The data
- `dbo.[Order]` holds 4 rows; every Total is currently non-negative (120.00, 540.50, 75.25,
  300.00), so nothing depends on the check today. The change is about what may be written
  tomorrow.

## How it ships
- Ships as a single schema change, applied in place. No data is read or written. The generated
  script is one statement:
  `ALTER TABLE [dbo].[Order] DROP CONSTRAINT [CK_Order_Total_NonNegative];`

## What proving showed
Published to a throwaway copy on this branch (sqlpackage 170.5.76).
- **Tried:** first the add, as its own change: adding `CHECK (Total >= 0)` over the 4 clean
  rows published clean and the constraint landed trusted (`is_not_trusted = 0`) — the engine
  re-validated every row as it went on.
- **Did:** remove the check from the CREATE, build, Strict publish → published. The delta
  contains the single `DROP CONSTRAINT` and nothing else.
- **Realized:** the drop validates nothing, so it can never block — the publish is not where
  this change's cost lives. The cost is every row written while the rule is gone: each
  violating one blocks the rule's return until reconciled.

## After deploy — check
```sql
-- expect 0 rows: the check no longer exists
SELECT name FROM sys.check_constraints WHERE name = 'CK_Order_Total_NonNegative';
```

## How to roll this back
Re-adding the check reverses the change:
`ALTER TABLE dbo.[Order] ADD CONSTRAINT CK_Order_Total_NonNegative CHECK (Total >= 0);`
The re-add re-validates every existing row; a negative Total written while the rule was gone
blocks it (the check's violation error names the value) until that row is corrected or the
rule is redefined. The drop itself loses no data.

## Not checked / still open
- Writes during the gap. Nothing at the data layer refuses a violating row after this lands;
  the rule's owner watches for violations if the rule ever needs to return.
- Application-side validation. Whether the application still refuses negative totals is not
  confirmed here.
- Other environments. QA, UAT, and Prod were not published here; run the check query in each
  before promotion.
