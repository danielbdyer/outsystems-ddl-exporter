/*
  [PERMANENT · idempotent]  Migrations/001_backfill_customer_region.sql
  Ticket:  (sample — illustrative exemplar, the standard to imitate)
  What:    Populates Region for Customer rows that predate a Region value, with a neutral default.
  Silent:  guarded by `WHERE Region IS NULL` — on a seed with no NULL Region it moves 0 rows and
           leaves an identical content hash. Prove the silent redeploy on a disposable copy before
           trusting it (see ../../skills/_index/idempotent-seed/SKILL.md).
  Retire:  never — model-restoring; re-establishes state on any fresh deploy. No death certificate.

  This is the permanent-class pattern to imitate. It is NOT wired into the active post-deploy by
  default (its :r include is commented in ../Script.PostDeployment.sql) so it does not perturb the
  standing seed the self-test scenarios pin. When an agent proves a real permanent backfill, it
  authors the script here and adds the live :r include under the migrations heading.
*/
UPDATE dbo.Customer
   SET Region = N'Unknown'
 WHERE Region IS NULL;
GO
