/*
  dbo.vOrderSummary — the dependency-scope + SELECT * View fixture (enumerated columns).

  CREATE-only schema item. The authored view lists its columns explicitly, never SELECT *. That is
  the correct, stable shape: a column added to Order, Customer, or Status does not silently change
  the view's contract.

  PARALLEL EXECUTORS — READ FIRST: do not edit this authored file in place. Copy the tree, publish
  to a unique database per `../../self-test/PROTOCOL.md`.

  WHY THIS FIXTURE STAYS (though authoring views is principal-only)
  ----------------------------------------------------------------
  Authoring a view — create-view, compat-view, indexed-view — is PRINCIPAL-ONLY for this team (out
  of the developer catalog on purpose; see skills/operations/views-synonyms.md). But this view is a
  load-bearing FIXTURE for the developer curriculum, independent of those ops:

  - Dependency scope (the primary use): vOrderSummary is the canonical "this column feeds a view"
    example — a change to Order/Customer must account for the view that reads it. Used by
    skills/review/dependency-scope, the REV-08 named-risk review scenario, and THE_RECORD.md's
    worked correction. The view stays here so that lesson has a real object.
  - The SELECT * View trap (handbook 16 = §19): the enumerated form is the stable destination; a
    `SELECT *` variant (a scratch edit) drifts against the base tables — a base column add is not
    reflected until sp_refreshview, with no reviewable diff. Taught in skills/op/create-view/
    (principal-only) and proven as a scratch edit only.
  - The Twin holds it: a view carries no data, so the Twin publishes vOrderSummary but never wipes
    or mints it (DECISIONS 2026-07-20). Faithful on both substrates.

  The authored view MUST stay enumerated and non-schemabound. The SELECT * and indexed-view variants
  are scratch edits — do not bake them into this file.

  Self-test: the view-authoring cases VIE-01 (create-view), VIE-02 (compat-view), VIE-04
  (indexed-view) are downgraded to principal-route negatives; synonym (VIE-03) is unaffected — it
  authors no view.
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
