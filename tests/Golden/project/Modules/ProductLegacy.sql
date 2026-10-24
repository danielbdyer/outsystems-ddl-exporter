/*
  Modules/ProductLegacy.sql — documentation-only module (no CREATE TABLE).

  There is exactly one dbo.Product CREATE (in Modules/Product.sql); a second CREATE for the same
  table is illegal. This file records the two columns added to dbo.Product on 2026-06-30 to unlock
  delete-attribute and the delete-seed-value FK negative without disturbing the existing narrow /
  add-unique Code scenarios. It contains no schema object, so the SDK Build glob picks it up as an
  empty batch — a no-op at build time. Keep it as a header; do not add a CREATE.

  Columns added to dbo.Product (see Modules/Product.sql):

    LegacyCode NVARCHAR(40) NOT NULL DEFAULT (N'LEGACY')
        A populated, NOT NULL column. It is seeded non-empty (see Data/Seed.sql, Product block),
        so dropping it on the populated table fires SSDT's BlockOnPossibleDataLoss guard. The guard
        fires on the table holding rows, not on the column's values — the same row-presence guard
        as make-mandatory / narrow. This is the tightening class: see
        skills/_index/tightening-class/ (do not re-derive the guard). The DEFAULT lets the column
        ADD cleanly to a populated Product; the DROP is the operation SSDT blocks — proof the
        guard fires. Distinct from Code, so narrow (COL-06) and add-unique (CON-02) on Code are
        unaffected.

    CategoryId INT NULL
        An FK-shaped target for dbo.Category. Nullable, so it does not touch the Code scenarios.
        Makes delete-seed-value (STA-04N) real: a hard DELETE of a referenced Category row orphans
        the Product rows pointing at it. See skills/_index/constraint-is-a-claim/ and
        skills/_index/idempotent-seed/ (deactivate-don't-delete).

  Unlocks self-test ids: COL-09 (delete-attribute — the populated-column BlockOnPossibleDataLoss
  block), STA-04N (delete-seed-value orphan negative via the Product->Category FK).
*/

-- Intentionally no schema object. The columns live in Modules/Product.sql.
