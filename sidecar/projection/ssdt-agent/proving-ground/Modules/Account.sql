/*
  dbo.Account — the move-attribute destination + split/merge partner surface.

  CREATE-only schema item. Each Account is 1:1 with a Customer via a new nullable
  Customer.AccountId (added to Customer.sql WITHOUT breaking the existing make-mandatory /
  rename seed). This 1:1 shape is the whole point: move-attribute (STR-03, move Region from
  Customer to Account) can only be PROVEN total when the relationship is provably 1:1.

  PARALLEL EXECUTORS — READ FIRST: do NOT edit this authored file in place. Copy the whole
  proving-ground tree to a private scratch dir and publish to a UNIQUE database
  (/TargetDatabaseName:PG_<testId>_<rand>) per `../self-test/PROTOCOL.md`.

  WHAT THIS TABLE UNLOCKS
  -----------------------
  - move-attribute (STR-03): Region lives here as the DESTINATION. The proof moves Region from
    Customer to Account and demonstrates the value survives across the 1:1 join. A cross-table
    MOVE has NO refactorlog identity mapping — it is copy-then-drop, never a rename. See
    skills/_index/multi-phase/ (coexistence + totality proof) and
    skills/_index/identity-and-refactorlog/ (why a move is not a rename).
  - merge/split partner: Account is the second surface a split can land on and a merge can fold.

  Region is seeded here so the STR-03 move has a destination shape to prove into; the AUTHORED
  positive keeps Customer.Region as the source of truth (the move is proven in a scratch copy
  that adds Customer.Region — see Customer.sql header). Region NVARCHAR(50) NULL matches the
  Customer-side attribute so the copy is type-clean.

  UNLOCKS self-test ids: STR-03 (move-attribute), STR-01/STR-02 (split/merge partner surface).
*/

CREATE TABLE dbo.Account
(
    Id      INT             IDENTITY(1,1) NOT NULL,
    Name    NVARCHAR(100)   NOT NULL,

    -- move-attribute DESTINATION. In the STR-03 proof, Customer.Region is copied here then
    -- dropped from Customer. Same type/nullability as the Customer-side attribute.
    Region  NVARCHAR(50)    NULL,

    CONSTRAINT PK_Account PRIMARY KEY CLUSTERED (Id)
);
GO
