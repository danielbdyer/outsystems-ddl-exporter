/*
  [TRANSIENT · one-time · REMOVE AFTER PROD]  OneTime/Release_2026.07_email_normalize.sql
  Ticket:        (sample — illustrative exemplar, the standard to imitate)
  What:          One-shot normalization of legacy-import Email casing to lower-case.
  Guard:         idempotent (`WHERE Email <> LOWER(Email)`) — safe to re-run in a lagging env;
                 a redeploy after it lands moves 0 rows and leaves an identical hash.
  Prod-applied:  <stamp on the prod deploy>
  Removal:       <work item> — sweep on the next deprecation train once prod-confirmed.
                 Delete this file; keep git history.

  This is the transient-class pattern to imitate: the header is the death certificate. It is NOT
  wired into the active post-deploy by default (its :r include is commented in
  ../Script.PostDeployment.sql) — a standing sample must not carry a one-time fix that re-runs on
  every publish. When an agent proves a real one-time correction, it authors the script here and
  adds the live :r include under the one-time heading, then names the removal work item on the PR.
*/
UPDATE dbo.Customer
   SET Email = LOWER(Email)
 WHERE Email IS NOT NULL
   AND Email <> LOWER(Email);
GO
