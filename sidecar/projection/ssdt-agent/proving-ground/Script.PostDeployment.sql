/*
  Script.PostDeployment.sql — the POST-DEPLOY slot. (Build Action = PostDeploy.)

  Runs AFTER SSDT applies the schema delta. This is where static-data seeds and post-deploy
  backfills live. A change that needs only this script (no pre-deploy data fix) ships as one
  release: the declarative schema change plus this post-deployment script that runs after it
  lands.

  Everything here MUST be idempotent — it runs on every publish. The seed uses guarded MERGEs so
  a re-publish with unchanged data is SILENT (captures 0 rows). That CDC-silence property is
  itself a proof: a no-op redeploy must not churn the data (see Data/Seed.sql and
  skills/operations/static-data.md).

  The `:r` directive includes the seed file at build time. It is an SQLCMD directive, so this
  script (and the build) require SQLCMD mode — sqlpackage and `dotnet build` of the .sqlproj
  handle this for post-deploy scripts. The path is relative to THIS file.
*/

:r .\Data\Seed.sql

GO

/*
  Deployment scripts by permanence class (skills/deploy-scripts/SKILL.md — the folder is the
  contract). Group `:r` includes by class so the order and the class are legible at a glance.
  The example includes below are COMMENTED: the standing sample must not run the exemplar scripts
  (they would perturb the seed the self-test scenarios pin). When an agent proves a real change, it
  authors the script in the right folder and UNCOMMENTS (or adds) its include under the matching
  heading. AdHoc/ is never included here — it runs outside the DACPAC by definition.
*/

-- Migrations (permanent · idempotent) — model-restoring backfills; Retire: never.
-- PRINT 'Running migrations...';        :r .\Migrations\001_backfill_customer_region.sql

-- Reference data (permanent · idempotent) — guarded MERGE seeds; Retire: never.
--   (The live reference seed is Data\Seed.sql, included above.)

-- One-time (transient · REMOVE AFTER PROD) — each carries a death certificate + a removal work item.
-- PRINT 'One-time (remove after prod)...'; :r .\OneTime\Release_2026.07_email_normalize.sql

GO

PRINT 'Post-deploy complete: seed MERGEs applied (idempotent).';
GO
