/*
  dbo.[Order] — the foreign-key proving table. (Bracketed: ORDER is a reserved word.)

  CREATE-only schema item. The seed plants AT LEAST ONE row whose CustomerId has no matching
  Customer — an ORPHAN. That orphan is what makes "add a foreign key" flip from a one-line
  declarative edit to a Script-Only reconcile.

  WHAT THIS TABLE EXERCISES
  -------------------------
  - create-FK clean vs with-orphans (Forgotten FK Check): adding
      CONSTRAINT FK_Order_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customer(Id)
    publishes clean ONLY if every CustomerId has a parent. With the orphan present, a clean
    declarative FK VETOES at deploy. The proven remedy is the script path:
    add WITH NOCHECK -> reconcile (delete/repoint the orphan) -> WITH CHECK CHECK to re-trust.
    Orphan present vs absent flips Tier 1 single-phase to Tier 3 Script-Only. (self-test prompt 4.)

  - change-delete-rule / cascade: the OutSystems delete rule (Protect / Ignore / Delete) maps to
    NO ACTION / NO ACTION / CASCADE. Changing it is DROP + ADD of the FK. Watch the delta for
    cascade chains. See skills/operations/keys-and-refs.md.

  The FK constraints are intentionally NOT declared below — declaring them is the change you
  prove. Start from the no-FK destination and add the FK to watch the orphan veto fire.
*/

CREATE TABLE dbo.[Order]
(
    Id              INT             IDENTITY(1,1) NOT NULL,

    -- Most rows match a Customer; the seed plants one orphan (CustomerId with no parent).
    CustomerId      INT             NOT NULL,

    -- References Status.Id (explicit-id lookup).
    StatusId        INT             NOT NULL,

    Total           DECIMAL(18, 2)  NOT NULL CONSTRAINT DF_Order_Total DEFAULT (0),

    -- extract-to-lookup SOURCE (STA-03, see Modules/OrderStatusText.sql header, added 2026-06-30).
    -- Free-text string values ('Pending','Shipped','Cancelled') that MAP to dbo.Status.Code.
    -- Promoting this free text to a Status FK must prove the mapping is TOTAL (every distinct
    -- StatusText resolves to a Status row). The STA-03 negative adds an unmapped value in a
    -- SCRATCH copy. See skills/op/extract-to-lookup/ and skills/_index/multi-phase/.
    StatusText      NVARCHAR(20)    NOT NULL CONSTRAINT DF_Order_StatusText DEFAULT (N'Pending'),

    CONSTRAINT PK_Order PRIMARY KEY CLUSTERED (Id)
);
GO
