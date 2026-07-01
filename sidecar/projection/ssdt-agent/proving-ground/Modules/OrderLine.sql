/*
  dbo.OrderLine — the composite-PK + cascade blast-radius table (deep FK graph).

  CREATE-only schema item. Child of dbo.[Order]. The seed plants 2-3 lines per Order so the
  Order -> OrderLine chain has real depth: a CASCADE delete rule change on Order.CustomerId (or on
  the OrderLine->Order FK) has a VISIBLE blast radius, and orphan/cascade proofs have children to
  act on.

  PARALLEL EXECUTORS — READ FIRST: do NOT edit this authored file in place. Copy the tree, publish
  to a UNIQUE database per `../self-test/PROTOCOL.md`.

  WHAT THIS TABLE UNLOCKS
  -----------------------
  - define-pk COMPOSITE (KEY-01): the natural key is (OrderId, LineNumber), not the surrogate Id.
    Proving a composite PK on populated data is a claim about UNIQUENESS of the pair (a duplicate
    pair vetoes the PK build) — see skills/_index/constraint-is-a-claim/ and skills/op/define-pk/.
    A surrogate Id IDENTITY is present so the table is addressable while the composite PK is proven.
  - change-delete-rule / cascade (KEY-04): Order -> OrderLine is the chain whose CASCADE makes the
    delete blast radius visible in the delta. See skills/op/change-delete-rule/.
  - FK graph depth for cascade/orphan proofs across keys-and-refs.

  The OrderLine -> Order FK is intentionally NOT declared (declaring it is a create-fk proof).
  Parents (Order) seed before this child.

  UNLOCKS self-test ids: KEY-01 (define-pk composite), KEY-04 (change-delete-rule cascade chain).
*/

CREATE TABLE dbo.OrderLine
(
    -- Surrogate so the table is addressable; the composite (OrderId, LineNumber) is the natural
    -- key define-pk proves.
    Id          INT             IDENTITY(1,1) NOT NULL,

    OrderId     INT             NOT NULL,
    LineNumber  INT             NOT NULL,
    Amount      DECIMAL(18, 2)  NOT NULL CONSTRAINT DF_OrderLine_Amount DEFAULT (0),

    CONSTRAINT PK_OrderLine PRIMARY KEY CLUSTERED (Id)
);
GO
