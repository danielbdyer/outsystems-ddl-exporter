/*
  Script.PreDeployment.sql — the PRE-DEPLOY slot. (Build Action = PreDeploy.)

  This script runs BEFORE SSDT applies the schema delta. It is the home of the data fix-ups that
  let a change publish cleanly — a pre-deploy dedupe, an over-length reconcile, a NULL backfill.
  The pattern is: fix the data here so the destination CREATE lands without the deployment being
  blocked. A change genuinely cleared by this script ships as one release: the pre-deployment
  fix-up, then the declarative change lands validated.

  IMPORTANT — a pre-deploy backfill does NOT clear EVERY block. See the make-mandatory example
  below: on a POPULATED table the NULL->NOT NULL guard fires on table-has-rows, so clearing the
  NULLs here does NOT let the ALTER land. Prove the remedy with a clean Strict re-run; do not
  assume it.

  PARALLEL EXECUTORS: do NOT edit this authored file. Copy the proving-ground tree to a private
  scratch dir, edit the COPY, and publish to a UNIQUE database per `../self-test/PROTOCOL.md`.

  It is idempotent and safe to run on every publish — it only touches rows that still violate
  the rule. Empty by default; uncomment the worked example when proving a change.

  ----------------------------------------------------------------------------------------------
  WORKED EXAMPLE — make-mandatory (Customer.Email NULL -> NOT NULL)  [CORRECTED 2026-06-30]
  ----------------------------------------------------------------------------------------------
  Scenario: you edited Customer.sql to `Email NVARCHAR(256) NOT NULL`. The default seed has NULL
  Email rows, so a Strict publish is blocked.

  The intuitive fix — backfill the NULLs here, before the schema delta — was DISPROVEN on this
  proving ground. SSDT generates the BlockOnPossibleDataLoss guard for NULL->NOT NULL as:

      IF EXISTS (SELECT TOP 1 1 FROM [dbo].[Customer])
          RAISERROR (N'Rows were detected. The schema update is terminating because data loss
                       might occur.', 16, 127)
      -- ... then, below it:
      ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR(256) NOT NULL;

  That guard fires on the table HAVING ANY ROW — it never inspects the Email column. SSDT
  computes the deploy script ONCE, up front, from the pre-publish state, and is conservative by
  design: it cannot know that this pre-deploy backfill (which runs at deploy time, after the
  script is already generated) will have emptied the NULLs. PROVEN: with the backfill below
  active, `SELECT COUNT(*) FROM dbo.Customer WHERE Email IS NULL` returned 0, yet Strict STILL
  blocked the change and the column STAYED nullable.

  So this backfill is NECESSARY but NOT SUFFICIENT on a populated table. Run it, re-run the NULL
  probe to PROVE 0 NULLs remain, but it does NOT earn a clean NOT NULL in the same release. This
  estate (Azure DevOps -> Octopus) cannot relax BlockOnPossibleDataLoss, so the proven remedy on a
  populated table is the TWO-RELEASE:
    Release 1 -- keep the MODEL at NULL and do the work in this pre-deploy: backfill the NULLs, then
        run the ALTER ... NOT NULL yourself. Because the model still declares NULL, DacFx generates
        no data-loss step and the guard never fires; the column comes out NOT NULL. The corrected
        seed rides here too (a seed still declaring NULL fails Msg 515 after the tightening).
    Release 2 -- the model declares NOT NULL; the database is already there, so DacFx generates
        nothing. Proven: self-test/golden/make-mandatory-pr.md (DBs pg_mm / pg_mm_naive).
  An EMPTY table is the clean contrast: no rows, the IF EXISTS is false, the ALTER lands in one
  release, no script needed.

  Choose a backfill that is HONEST to the developer's intent. The literal placeholder below is
  fine for the proving ground; in production you confirm the real backfill value (a derived
  value, a sentinel, or a hard requirement to collect the data first). The point here is to show
  that the block is real, that the backfill clears the NULLs, and that Strict STILL refuses.
*/

-- Release 1 of the two-release (keep Modules/Customer.sql at NULL while this runs):
-- IF EXISTS (SELECT 1 FROM dbo.Customer WHERE Email IS NULL)
--     UPDATE dbo.Customer
--         SET Email = N'unknown+' + CAST(Id AS NVARCHAR(20)) + N'@example.invalid'
--         WHERE Email IS NULL;
-- GO
-- IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Customer')
--            AND name = 'Email' AND is_nullable = 1)
--     ALTER TABLE dbo.Customer ALTER COLUMN Email NVARCHAR(256) NOT NULL;  -- model still says NULL, so DacFx emits no ALTER
-- GO

/*
  ----------------------------------------------------------------------------------------------
  WORKED EXAMPLE — Ambitious Narrowing (Product.Code NVARCHAR(50) -> NVARCHAR(10))
  ----------------------------------------------------------------------------------------------
  Scenario: you edited Product.sql to `Code NVARCHAR(10)`. The seed has an over-length Code, so
  Strict blocks the change on data loss. Reconcile the over-length rows here before the delta.
  NOTE: silent truncation is a DECISION — confirm with the developer whether truncation is
  acceptable or whether the over-length values must be preserved (which stages it across releases
  instead). Probe MAX(LEN(Code)) first: if every value already fits the new size, nothing is
  blocked and no backfill is needed — the same narrow op is a single schema change applied in
  place, decided by the data not the .sql.
*/

-- UPDATE dbo.Product
--     SET Code = LEFT(Code, 10)
--     WHERE LEN(Code) > 10;
-- GO

PRINT 'Pre-deploy: no backfill active. Uncomment a worked example when proving a change.';
GO
