/*
  dbo.CustomerAddress — the split/merge partner table (proven 1:1 with Customer).

  CREATE-only schema item. The authored seed plants exactly one CustomerAddress per Customer, so
  the relationship is provably 1:1. The 1:1 relationship is load-bearing:

    - merge-tables (STR-02): folding CustomerAddress back into Customer is total only when every
      Customer has at most one address. The STR-02 positive proof depends on the authored 1:1
      seed. Its conservation proof: absorbed-rows == distinct-parents (see
      skills/_index/multi-phase/).

    - split-table (STR-01): Customer split into Customer + CustomerAddress is the reverse move.

  The STR-02N negative is a scratch edit — do not bake it into this file. The 1:many negative (a
  Customer with two addresses, which makes the merge non-total) is produced by a scratch-copy seed
  edit that appends a second row for one CustomerId. Baking 1:many into the authored positive seed
  would invalidate the STR-02 positive. Keep the authored seed strictly 1:1; add the extra row only
  in a scratch copy per `../self-test/PROTOCOL.md`.

  Parallel executors, read first: do not edit this authored file in place. Copy the tree, edit the
  copy, and publish to a unique database. See `../self-test/PROTOCOL.md`.

  CustomerId is a plain column: the foreign key to Customer is intentionally not declared, because
  declaring it is itself a create-fk proof. Parents (Customer) seed before this child.

  Self-test ids exercised: STR-01 (split-table), STR-02 (merge-tables, 1:1 positive),
  STR-02N (1:many negative — scratch seed edit only).
*/

CREATE TABLE dbo.CustomerAddress
(
    Id          INT             IDENTITY(1,1) NOT NULL,

    -- 1:1 back to Customer in the AUTHORED seed. The STR-02N negative adds a 2nd row for one
    -- CustomerId in a SCRATCH copy to break totality on purpose.
    CustomerId  INT             NOT NULL,

    Line1       NVARCHAR(120)   NOT NULL,
    City        NVARCHAR(80)    NULL,
    PostalCode  NVARCHAR(20)    NULL,

    CONSTRAINT PK_CustomerAddress_Id PRIMARY KEY CLUSTERED (Id)
);
GO
