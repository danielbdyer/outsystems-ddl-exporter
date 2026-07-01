/*
  dbo.CustomerAddress — the split / merge partner table (proven 1:1 with Customer).

  CREATE-only schema item. The AUTHORED seed plants EXACTLY ONE CustomerAddress per Customer, so
  the relationship is PROVABLY 1:1. That 1:1-ness is load-bearing:

    - merge-tables (STR-02): folding CustomerAddress back into Customer is TOTAL only when every
      Customer has AT MOST ONE address. The positive STR-02 proof depends on the authored 1:1
      seed. Its conservation proof: absorbed-rows == distinct-parents (see
      skills/_index/multi-phase/).

    - split-table (STR-01): Customer split into Customer + CustomerAddress is the reverse move.

  THE STR-02N NEGATIVE IS A SCRATCH EDIT — DO NOT BAKE IT HERE. The 1:many negative (a Customer
  with TWO addresses, which makes the merge non-total) is produced by a SCRATCH-COPY seed edit
  that appends a 2nd row for one CustomerId. Baking 1:many into the AUTHORED positive seed would
  BREAK the STR-02 positive. Keep the authored seed strictly 1:1; add the extra row only in a
  throwaway copy per `../self-test/PROTOCOL.md`.

  PARALLEL EXECUTORS — READ FIRST: do NOT edit this authored file in place. Copy the tree, edit
  the COPY, publish to a UNIQUE database. See `../self-test/PROTOCOL.md`.

  CustomerId is a plain column (FK to Customer intentionally NOT declared — declaring it is a
  create-fk proof). Parents (Customer) seed before this child.

  UNLOCKS self-test ids: STR-01 (split-table), STR-02 (merge-tables, 1:1 positive),
  STR-02N (1:many negative — SCRATCH seed edit only).
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

    CONSTRAINT PK_CustomerAddress PRIMARY KEY CLUSTERED (Id)
);
GO
