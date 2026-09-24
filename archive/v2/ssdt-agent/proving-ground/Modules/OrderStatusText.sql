/*
  Modules/OrderStatusText.sql — a documentation-only module (no CREATE TABLE).

  There is exactly one dbo.[Order] CREATE, in Modules/Order.sql. This file records the free-text
  StatusText column added to dbo.[Order] on 2026-06-30, the source for the extract-to-lookup move.
  Nothing is created here — a harmless empty batch under the SDK Build glob. Do not add a CREATE.

  Column added to dbo.[Order] (defined in Modules/Order.sql):

    StatusText NVARCHAR(20) NOT NULL DEFAULT (N'Pending')
        Free-text values, seeded distinct ('Pending','Shipped','Cancelled'), that map to
        dbo.Status.Code. This column is the extract-to-lookup source (STA-03): promoting the free
        text to a proper Status foreign key is a multi-phase move — add the StatusId foreign key,
        backfill it from the text via the Status.Code map, then drop StatusText. The drop is
        authorized only by a total-mapping proof: every distinct StatusText must resolve to a
        seeded Status row, or the backfill leaves NULLs and the foreign key cannot be trusted. See
        skills/_index/multi-phase/ (the totality proof before the drop) and
        skills/_index/constraint-is-a-claim/ (the foreign key is a claim proven at apply time).

  The STA-03 total-mapping negative is a scratch edit, not baked into this file. The negative seeds
  an unmapped StatusText value (e.g. 'Backordered', with no matching Status.Code) in a disposable
  copy, so the mapping is provably non-total. The authored positive keeps every StatusText mapped,
  so the STA-03 positive passes. Add the unmapped row only in a scratch copy, per
  `../self-test/PROTOCOL.md`.

  Self-test ids exercised: STA-03 (extract-to-lookup, total-mapping positive; the non-total
  negative is a scratch seed edit).
*/

-- Intentionally no schema object. The column lives in Modules/Order.sql.
