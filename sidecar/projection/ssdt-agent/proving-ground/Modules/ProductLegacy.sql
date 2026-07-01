/*
  Modules/ProductLegacy.sql — DOCUMENTATION-ONLY module (NO CREATE TABLE).

  There is exactly ONE dbo.Product CREATE (in Modules/Product.sql); a second CREATE for the same
  table is illegal. This file is the RECORD of the two columns ADDED to dbo.Product on 2026-06-30
  to unlock delete-attribute and the delete-seed-value FK negative WITHOUT disturbing the existing
  narrow / add-unique Code scenarios. It contains no schema object, so the SDK Build glob picks it
  up harmlessly as an empty batch. (Keep it as a header; do not add a CREATE.)

  COLUMNS ADDED TO dbo.Product (see Modules/Product.sql):

    LegacyCode NVARCHAR(40) NOT NULL DEFAULT (N'LEGACY')
        A POPULATED, NOT NULL column. It is seeded non-empty (see Data/Seed.sql, Product block),
        so DROPPING it on the populated table fires SSDT's BlockOnPossibleDataLoss guard —
        table-has-rows, the same data-BLIND guard as make-mandatory / narrow. This is the
        tightening class: see skills/_index/tightening-class/ (do NOT re-derive the guard). The
        DEFAULT lets the column ADD cleanly to a populated Product; the DROP is the veto proof.
        Distinct from Code, so narrow (COL-06) and add-unique (CON-02) on Code are unaffected.

    CategoryId INT NULL
        FK-shaped target for dbo.Category. Nullable so it does not touch the Code scenarios.
        Makes delete-seed-value (STA-04N) real: a hard DELETE of a referenced Category row
        orphans the Product rows pointing at it. See skills/_index/constraint-is-a-claim/ and
        skills/_index/idempotent-seed/ (deactivate-don't-delete).

  UNLOCKS self-test ids: COL-09 (delete-attribute, populated-column BlockOnPossibleDataLoss veto),
  STA-04N (delete-seed-value orphan negative via the Product->Category FK).
*/

-- Intentionally no schema object. The columns live in Modules/Product.sql.
