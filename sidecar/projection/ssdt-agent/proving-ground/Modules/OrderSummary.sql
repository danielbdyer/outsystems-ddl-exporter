/*
  dbo.vOrderSummary — the authored VIEW (ENUMERATED columns) + the SELECT * trap surface.

  CREATE-only schema item. The AUTHORED view lists its columns EXPLICITLY (never SELECT *). That
  is the correct, stable shape: a column added to Order/Customer/Status does NOT silently change
  the view's contract, and the view is a safe compat/indexed-view base.

  PARALLEL EXECUTORS — READ FIRST: do NOT edit this authored file in place. Copy the tree, publish
  to a UNIQUE database per `../self-test/PROTOCOL.md`.

  WHAT THIS VIEW UNLOCKS
  ----------------------
  - create-view (VIE-01): an OutSystems Advanced Query / view over a join. The authored,
    enumerated form is the clean Mechanism 1 destination. See skills/op/create-view/.
  - the SELECT * View trap (handbook 16 = §19): the documented SELECT * VARIANT below is proven in
    a SCRATCH copy. Replace the enumerated SELECT with `SELECT *` and observe that the view's
    column set is now FROZEN at first-bind and drifts against the base tables (a base column add is
    not reflected; a base column drop breaks the view at runtime, not deploy). The AUTHORED view
    stays enumerated so VIE-01 positive passes; the trap is a SCRATCH edit only. See
    skills/op/create-view/ and skills/op/compat-view/.
  - compat-view TARGET (VIE-02): after a rename, a view carrying the OLD name over the NEW table is
    the backward-compat bridge — identity survives the move, the name is just an address. See
    skills/op/compat-view/ and skills/_index/identity-and-refactorlog/.
  - indexed-view (VIE-04): a SCRATCH edit adds WITH SCHEMABINDING + a UNIQUE CLUSTERED index to
    materialize an aggregation. SCHEMABINDING is why the enumerated column list matters. See
    skills/op/indexed-view/.

  synonym (VIE-03) needs NO table here — it targets an external DB/server and is proven by the
  runtime-resolution gap (the dacpac cannot validate the far side). See skills/op/synonym/.

  THE SELECT * TRAP + INDEXED-VIEW VARIANTS ARE SCRATCH EDITS — DO NOT BAKE THEM HERE. The authored
  view must stay enumerated and non-schemabound so VIE-01 positive publishes clean.

  UNLOCKS self-test ids: VIE-01 (create-view), VIE-02 (compat-view target),
  VIE-04 (indexed-view — SCRATCH edit), and the SELECT * View trap (SCRATCH edit).
*/

CREATE VIEW dbo.vOrderSummary
AS
    SELECT
        o.Id            AS OrderId,
        o.Total         AS Total,
        o.StatusText    AS StatusText,
        c.Id            AS CustomerId,
        c.Name          AS CustomerName,
        s.Code          AS StatusCode
    FROM dbo.[Order]    AS o
    INNER JOIN dbo.Customer AS c ON c.Id = o.CustomerId
    INNER JOIN dbo.Status   AS s ON s.Id = o.StatusId;
GO
