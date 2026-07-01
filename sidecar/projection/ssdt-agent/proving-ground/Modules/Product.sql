/*
  dbo.Product — the narrowing and uniqueness proving table.

  CREATE-only schema item. Code starts WIDE (NVARCHAR(50)). The seed plants AT LEAST ONE Code
  longer than 10 characters and (separately) a DUPLICATE Code, so two different changes flip by
  data.

  WHAT THIS TABLE EXERCISES
  -------------------------
  - narrow (Ambitious Narrowing): edit `Code NVARCHAR(50)` to `Code NVARCHAR(10)`. The
    over-length seed row would truncate, so Strict VETOES on possible data loss. A
    MAX(LEN(Code)) probe PREDICTS the veto before you even build (see talk-to-local-sql).
    Empty / all-short -> Mechanism 1. Over-length present -> Mechanism 3 or 5 (reconcile first),
    Tier 3 (data change). (self-test prompt 5.)

  - add-unique on Code: add CONSTRAINT UQ_Product_Code UNIQUE (Code) (or a unique index). With a
    duplicate Code in the seed, the unique index build FAILS on the dupe. The remedy is a
    pre-deploy dedupe. Dupes present vs absent flips the bucket. See
    skills/operations/constraints.md.

  Code is intentionally non-unique below — adding uniqueness is the change you prove.
*/

CREATE TABLE dbo.Product
(
    Id      INT             IDENTITY(1,1) NOT NULL,

    -- Wide enough to hold the over-length seed value. Narrowing this is the data-loss proof.
    Code    NVARCHAR(50)    NOT NULL,

    Name    NVARCHAR(200)   NULL,

    -- delete-attribute proving column (see Modules/ProductLegacy.sql header, added 2026-06-30).
    -- POPULATED NOT NULL: dropping it on a table with rows fires BlockOnPossibleDataLoss
    -- (table-has-rows). A DISTINCT column so narrow / add-unique on Code stay unaffected.
    -- See skills/op/delete-attribute/ and skills/_index/tightening-class/.
    LegacyCode NVARCHAR(40)  NOT NULL CONSTRAINT DF_Product_LegacyCode DEFAULT (N'LEGACY'),

    -- FK target for Category (see Modules/Category.sql). Nullable so it does not disturb the
    -- existing narrow / add-unique Code scenarios. Makes the delete-seed-value negative (STA-04N)
    -- real: deleting a referenced Category orphans these rows. FK to Category(Id) intentionally
    -- NOT declared — declaring it is a create-fk proof.
    CategoryId INT           NULL,

    CONSTRAINT PK_Product PRIMARY KEY CLUSTERED (Id)
);
GO
