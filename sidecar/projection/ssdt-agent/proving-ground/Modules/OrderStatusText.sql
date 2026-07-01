/*
  Modules/OrderStatusText.sql — DOCUMENTATION-ONLY module (NO CREATE TABLE).

  There is exactly ONE dbo.[Order] CREATE (in Modules/Order.sql). This file RECORDS the free-text
  StatusText column ADDED to dbo.[Order] on 2026-06-30 to unlock extract-to-lookup. No schema
  object here (harmless empty batch under the SDK Build glob). Do not add a CREATE.

  COLUMN ADDED TO dbo.[Order] (see Modules/Order.sql):

    StatusText NVARCHAR(20) NOT NULL DEFAULT (N'Pending')
        Free-text string values seeded distinct ('Pending','Shipped','Cancelled') that MAP to
        dbo.Status.Code. This is the extract-to-lookup (STA-03) SOURCE: promoting the free text to
        a proper Status FK is a MULTI-PHASE move (add StatusId FK -> backfill from the text via the
        Status.Code map -> drop StatusText) whose licensing gate is a TOTAL-MAPPING proof — every
        distinct StatusText must resolve to a seeded Status row, else the backfill leaves NULLs and
        the FK cannot be trusted. See skills/_index/multi-phase/ (totality proof before the drop)
        and skills/_index/constraint-is-a-claim/ (the FK is a claim proven at apply time).

  THE STA-03 TOTAL-MAPPING NEGATIVE IS A SCRATCH EDIT — DO NOT BAKE IT HERE. The negative seeds an
  UNMAPPED StatusText value (e.g. 'Backordered' with no matching Status.Code) in a THROWAWAY copy
  so the mapping is provably non-total. The AUTHORED positive keeps every StatusText mapped, so
  STA-03 positive passes. Add the unmapped row only in a scratch copy per `../self-test/PROTOCOL.md`.

  UNLOCKS self-test ids: STA-03 (extract-to-lookup, total-mapping positive; the non-total negative
  is a SCRATCH seed edit).
*/

-- Intentionally no schema object. The column lives in Modules/Order.sql.
