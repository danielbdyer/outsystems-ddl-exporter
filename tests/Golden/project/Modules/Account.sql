/*
  dbo.Account — move-attribute destination, and the split/merge partner table.

  CREATE-only schema item. Each Account is 1:1 with a Customer through a new nullable
  Customer.AccountId, added to Customer.sql without disturbing the existing make-mandatory /
  rename seed. The 1:1 shape is required for the move-attribute proof: move-attribute (STR-03,
  move Region from Customer to Account) is total only when the Customer-to-Account relationship
  is provably 1:1.

  Parallel executors, read first: do not edit this authored file in place. Copy the whole
  proving-ground tree to a private scratch directory and publish to a unique database
  (/TargetDatabaseName:PG_<testId>_<rand>) per `../self-test/PROTOCOL.md`.

  What this table exercises
  -------------------------
  - move-attribute (STR-03): Region lives here as the destination. The proof moves Region from
    Customer to Account and shows the value survives across the 1:1 join. A cross-table move has
    no refactorlog identity mapping — it is copy-then-drop, not a rename. See
    skills/_index/multi-phase/ (coexistence + totality proof) and
    skills/_index/identity-and-refactorlog/ (why a move is not a rename).
  - merge/split partner: Account is a second table a split can land on and a table a merge can
    fold.

  Region is seeded here so the STR-03 move has a destination shape to prove into. The authored
  positive keeps Customer.Region as the source of truth; the move is proven in a scratch copy
  that adds Customer.Region (see Customer.sql header). Region NVARCHAR(50) NULL matches the
  Customer-side attribute so the copy is type-clean.

  Self-test ids exercised: STR-03 (move-attribute), STR-01/STR-02 (split/merge partner).
*/

CREATE TABLE dbo.Account
(
    Id      INT             IDENTITY(1,1) NOT NULL,
    Name    NVARCHAR(100)   NOT NULL,

    -- move-attribute DESTINATION. In the STR-03 proof, Customer.Region is copied here then
    -- dropped from Customer. Same type/nullability as the Customer-side attribute.
    Region  NVARCHAR(50)    NULL,

    CONSTRAINT PK_Account_Id PRIMARY KEY CLUSTERED (Id)
);
GO
