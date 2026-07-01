/*
  Script.PostDeployment.sql — the POST-DEPLOY slot. (Build Action = PostDeploy.)

  Runs AFTER SSDT applies the schema delta. This is where static-data seeds and
  Declarative+Post-Deploy backfills live. A change that needs only this script (no pre-deploy
  data fix) is Mechanism 2 (Declarative + Post-Deploy), single-PR.

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

PRINT 'Post-deploy complete: seed MERGEs applied (idempotent).';
GO
