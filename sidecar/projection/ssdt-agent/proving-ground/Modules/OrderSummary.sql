/*
  dbo.vOrderSummary — the authored view (enumerated columns) and the base for the SELECT * View
  trap variant.

  CREATE-only schema item. The authored view lists its columns explicitly, never SELECT *. That
  is the correct, stable shape: a column added to Order, Customer, or Status does not silently
  change the view's contract, and the view is a safe base for a compatibility or indexed view.

  PARALLEL EXECUTORS — READ FIRST: do not edit this authored file in place. Copy the tree, publish
  to a unique database per `../self-test/PROTOCOL.md`.

  WHAT THIS VIEW UNLOCKS
  ----------------------
  - create-view (VIE-01): an OutSystems Advanced Query / view over a join. The authored,
    enumerated form is the clean destination: it ships as a single schema change, applied in
    place, and no data is read or written. See skills/op/create-view/.
  - the SELECT * View trap (handbook 16 = §19): the SELECT * variant below is proven in a scratch
    copy. Replace the enumerated SELECT with `SELECT *`, and the view's column set is frozen at
    first bind and drifts against the base tables — a base column add is not reflected, and a base
    column drop breaks the view at runtime, not at deploy. The authored view stays enumerated so
    VIE-01 positive passes; the trap is a scratch edit only. See skills/op/create-view/ and
    skills/op/compat-view/.
  - compat-view target (VIE-02): after a rename, a view carrying the old name over the new table
    is the backward-compatibility bridge — the identity survives the move, and the name is just an
    address. See skills/op/compat-view/ and skills/_index/identity-and-refactorlog/.
  - indexed-view (VIE-04): a scratch edit adds WITH SCHEMABINDING and a UNIQUE CLUSTERED index to
    materialize an aggregation. SCHEMABINDING is why the enumerated column list matters. See
    skills/op/indexed-view/.

  synonym (VIE-03) needs no table here — it targets an external database or server and is proven
  by the runtime-resolution gap: the dacpac cannot validate the far side. See skills/op/synonym/.

  The SELECT * View trap and the indexed-view variants are scratch edits — do not bake them into
  this file. The authored view must stay enumerated and non-schemabound so VIE-01 positive
  publishes clean.

  UNLOCKS self-test ids: VIE-01 (create-view), VIE-02 (compat-view target),
  VIE-04 (indexed-view — scratch edit), and the SELECT * View trap (scratch edit).
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
