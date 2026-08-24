# Order.Total → whole dollars (INT): a lossy retype staged across releases; 2 of 4 values lose their cents

## Verdict
This change stores `dbo.Order.Total` as a whole number instead of `DECIMAL(18,2)`; 2 of the 4 totals
(`540.50`, `75.25`) have cents that a whole-number type cannot keep. A single type change is refused on
a populated table, so it stages across releases: add a number column, convert the values, settle the
rounding of the two that lose cents, move the application, then drop the old column. Decide how the
cents round before merge. No work item supplied — attach one before merge.

## Intent
The developer's stated intent for this PBI: store the order total as a whole number rather than a
decimal — "make Total an integer". No work item supplied — attach one before merge.

## What changes
- Release A — `dbo.Order`: add `TotalWhole INT NULL` (the target type, nullable and additive).
- Release B — a script sets `TotalWhole` from `Total` under a stated rounding rule and settles the 2
  rows whose cents are lost. The application moves to read `TotalWhole`.
- Release C — after the application reads `TotalWhole`, drop the old `Total` column and (optionally, via
  a refactorlog entry) rename `TotalWhole` to `Total`.

## Before promoting
- Run the precision-loss query (below) in each environment. The set differs per environment; the copy
  held 2: Order 2 (`540.50`) and Order 3 (`75.25`). Settle each before the drop.
- Confirm the rounding rule with the data owner — round to nearest, or truncate — before the convert.
  A whole-dollar `Total` cannot hold cents; which direction the cents go is a business decision.
- Confirm each release has landed in an environment before sending the next up, and confirm the
  application reads `TotalWhole` before the old `Total` column is dropped.

## The data
- 4 orders. Totals `120.00`, `540.50`, `75.25`, `300.00`. Converting to a whole number keeps `120` and
  `300` exactly and drops the cents on `540.50 → 540` and `75.25 → 75` — 2 of 4 rows lose value.

## How it ships
- Several releases, because a single declarative type change on a populated table is refused: DacFx
  generates one `ALTER COLUMN`, and this pipeline (Azure DevOps → Octopus) publishes with the data-loss
  guard `BlockOnPossibleDataLoss` on, which blocks it on row presence.
- **Release A** — add `TotalWhole INT NULL`. A new nullable column is additive: DacFx generates a plain
  `ADD` with no data-loss step, so it ships in one release.
- **Release B** — set `TotalWhole` from `Total` under the agreed rounding; the 2 cents-losing rows are
  settled per the data owner's decision. The application moves to read `TotalWhole`.
- **Release C** — drop the old `Total` column. Dropping a populated column is refused by the same
  guard, so this leg is itself a two-release column drop (`delete-attribute`): a pre-deploy `DROP
  COLUMN` with the model still declaring `Total` (C1), then the model drops `Total` (C2). The seed
  stops writing `Total` in the same change set. The rename `TotalWhole` → `Total`, if wanted, needs a
  refactorlog entry so DacFx renames in place rather than dropping and re-adding.

## What proving showed (published to a throwaway copy, this branch)
Proven on copies this branch (`pg_retype2` block; `pg_base` conversion; sqlpackage 170.4.83.3,
`BlockOnPossibleDataLoss = True`).
- **Tried:** change `Total` from `DECIMAL(18,2)` to `INT`, publish under the guard → refused.
  `Warning SQL72015: The type for column Total in table [dbo].[Order] is currently DECIMAL (18, 2) NOT
  NULL but is being changed to INT NOT NULL. Data loss could occur…` then `Error SQL72014 … Msg 50000,
  Level 16, State 127: Rows were detected. The schema update is terminating because data loss might
  occur.` The column stayed `DECIMAL`. A single-step type change does not apply.
- **Did:** `CONVERT(INT, Total)` over the real data — all 4 rows convert, but 2 lose their cents
  (`540.50 → 540`, `75.25 → 75`); `120.00` and `300.00` are exact. The precision-loss rows are the fork
  to settle.
- **Realized:** convertibility must be proven before a type change is promised. Here every value
  converts (the fork is *how the cents round*); on a non-numeric column the proof can refute the whole
  premise — `TRY_CONVERT(INT, Product.Code)` returns NULL for all 5 rows (`A100`, `STANDARD-SKU-001`,
  `DUPE` … are not numbers, `pg_retype`), which is a STOP, not a staging. The type change earns its
  staging: the values that lose precision must be settled first, and the old and new columns must
  coexist while the application moves.

## After deploy — check (each environment)
```sql
-- before the convert: rows whose value is not preserved by a whole number (settle these first)
SELECT Id, Total FROM dbo.[Order] WHERE Total <> CONVERT(INT, Total);

-- prove convertibility before promising the type change (a non-numeric value returns NULL = STOP)
SELECT Id, Total, TRY_CONVERT(INT, Total) AS as_int FROM dbo.[Order] WHERE TRY_CONVERT(INT, Total) IS NULL;

-- after the cutover: the column is a whole number, expect type_name = 'int'
SELECT ty.name AS type_name
FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID('dbo.[Order]') AND c.name = 'Total';
```

## How to roll this back
Before the old `Total` column is dropped, backing out is lossless: drop `TotalWhole` and keep reading
`Total`, which still holds its original `DECIMAL` values. Once `Total` is dropped, its cents live
nowhere — the whole-dollar `TotalWhole` cannot reconstruct `540.50` from `540`. Keep the original
`DECIMAL` (a backup, or the coexisting `Total` column) until the drop is confirmed durable. Backing the
change out was not exercised.

## Not checked / still open
- The rounding of the cents — round or truncate on the 2 losing rows — is a data-owner decision, not
  made here. Either way the fractional cents cannot be recovered once `Total` is dropped.
- Application impact — every read and write path still using `Total` breaks once the column is swapped;
  that every caller has moved to `TotalWhole` is not confirmed (app owner).
- The rename leg — swapping `TotalWhole` back to the name `Total` needs a refactorlog entry; without
  one, DacFx drops and re-adds and the data is lost. Not exercised here.
- Other environments — QA, UAT, and Prod may hold more precision-loss rows than the 2 on the copy.
  Run the precision-loss query in each before the convert phase.
- Production scale and timing — the convert and the drop at production row counts are not shown by the
  small copy. Schedule a window.
