# Product.Code: change the type to a whole number (1 value cannot convert; staged across several releases)

## Verdict
This change stores `dbo.Product.Code` as a whole number instead of text; 1 of the 5 codes,
`30X`, does not convert and needs a decision. A single type change is refused on a populated
table, so it stages across several releases: add a number column, convert the values that convert,
settle `30X`, move the application, then drop the old text column. Decide the fate of `30X` before
merge. No work item supplied — attach one before merge.

## Intent
The developer's stated intent for this PBI: store the product code as a whole number rather than
text — "make Code an integer". No work item supplied — attach one before merge.

## What changes
- Release A — `dbo.Product`: add `CodeNum INT NULL` (the target type, nullable and additive).
- Release B — a script sets `CodeNum = TRY_CONVERT(INT, Code)` and settles the 1 code that does not
  convert.
- Release C — after the application reads `CodeNum`, drop the old `Code` column and (optionally, via
  a refactorlog entry) rename `CodeNum` to `Code`.

## Before promoting
- Run the non-convertible query (below) in each environment. The set differs per environment; the
  copy held 1: Product 3, `Code = '30X'`. Settle every non-convertible row before the drop.
- Confirm the fate of `30X` with the data owner — corrected to a real number before the cutover, or
  allowed to land as NULL. Correcting it keeps this a type change; letting it drop loses data.
- Confirm each release has landed in an environment before sending the next one up, and confirm the
  application reads `CodeNum` before the old `Code` column is dropped.

## How it ships
- Several releases, because a single declarative type change on a populated table is refused: DacFx
  generates one `ALTER COLUMN`, and this pipeline (Azure DevOps → Octopus) publishes with the
  data-loss guard `BlockOnPossibleDataLoss` on, which blocks it on row presence.
- **Release A** — add `CodeNum INT NULL`. A new nullable column is additive: DacFx generates a plain
  `ADD` with no data-loss step, so it ships in one release.
- **Release B** — set `CodeNum = TRY_CONVERT(INT, Code)`. Rows that convert take their number;
  `30X` stays NULL and is settled per the data owner's decision. The application moves to read
  `CodeNum`.
- **Release C** — drop the old `Code` column. Dropping a populated column is refused by the same
  guard, so this leg is itself a two-release drop: a pre-deploy `DROP COLUMN` with the model still
  declaring `Code` (Release C1), then the model drops `Code` (Release C2). The seed stops writing
  `Code` in the same change set. The rename `CodeNum` → `Code`, if wanted, needs a refactorlog entry
  so DacFx renames in place rather than dropping and re-adding. This mirrors `delete-attribute.md`.

## The data
- 5 products. 4 codes are whole numbers (`100`, `200`, `400`, `500`); 1 is not: Product 3, `30X`.
- `TRY_CONVERT(INT, Code)` returns a number for 4 rows and NULL for `30X`.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried:** change `Code` from `NVARCHAR(50)` to `INT`, publish under the guard → refused.
  Warning SQL72015: "The type for column Code … is currently NVARCHAR (50) NOT NULL but is being
  changed to INT NOT NULL. Data loss could occur." `Msg 50000`: "Rows were detected. The schema
  update is terminating because data loss might occur." The column stayed text. A single-step type
  change does not apply.
- **Did:** add `CodeNum INT NULL` → published in one release. Set
  `CodeNum = TRY_CONVERT(INT, Code)` → 4 rows took `100`/`200`/`400`/`500`; `30X` stayed NULL
  (1 row unconverted).
- **Did:** drop the old `Code` column, publish under the guard → refused. Warning SQL72015: "The
  column [dbo].[Product].[Code] is being dropped, data loss could occur." `Msg 50000`. The drop leg
  is a data-loss drop, ships as its own two releases.
- **Realized:** the type change earns its staging twice over — one value does not convert and must
  be settled first, and the old and new columns must coexist while the application moves. The
  subtractive leg that removes the old column is a drop, blocked exactly like `delete-attribute`.

## After deploy — check
```sql
-- before the drop: every code converts, expect 0 rows (a returned row would land as NULL)
SELECT Id, Code FROM dbo.Product
WHERE Code IS NOT NULL AND TRY_CONVERT(INT, Code) IS NULL;

-- after the cutover: the column is a whole number, expect type_name = 'int'
SELECT ty.name AS type_name
FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID('dbo.Product') AND c.name = 'Code';
```

## How to roll this back
Before the old `Code` column is dropped, backing out is lossless: drop `CodeNum` and keep reading
`Code`, which still holds its original text. Once `Code` is dropped, its text values live only as
the converted number in `CodeNum`; `30X` — if it was allowed to land as NULL — is not recoverable
from the schema. Keep the original text (a backup, or the coexisting `Code` column) until the drop
is confirmed durable. Backing the change out was not exercised.

## Not checked / still open
- The fate of `30X` — corrected to a number or dropped as NULL — is a data-owner decision, not made
  here. Dropping it removes data that cannot be recovered.
- Application impact — every read and write path still using the text `Code` breaks once the column
  is swapped; that every caller has moved to `CodeNum` is not confirmed (app owner).
- The rename leg — swapping `CodeNum` back to the name `Code` needs a refactorlog entry; without
  one, DacFx drops and re-adds and the data is lost. Not exercised here.
- Other environments — Test, UAT, and Prod may hold more non-convertible codes than the 1 on the
  copy. Run the non-convertible query in each before the convert phase.
- Production scale and timing — the convert and the drop at production row counts are not shown by
  the small copy. Schedule a window.
