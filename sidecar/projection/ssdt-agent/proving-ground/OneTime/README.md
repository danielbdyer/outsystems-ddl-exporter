# OneTime/ — transient · one-time (born with a death certificate)

Scripts here are **transient**: release-specific corrections and one-shot data motions. They are
**born with a death certificate** and stay only until confirmed applied in production, then are
removed on the next deprecation-train sweep as a tracked PR (removal leaves the deploy path but keeps
git history).

Contract for every file:
- **Idempotent** so a lagging environment can safely re-run it — the reason a one-time script is
  *not* deleted the instant it runs.
- **Header IS a death certificate** — `[TRANSIENT · one-time · REMOVE AFTER PROD]`, the guard, a
  `Prod-applied:` stamp slot, and a `Removal:` line naming the work item that will sweep it (or, for
  a phase-bound script, the phase/release that ends it — see
  `../../skills/_index/multi-phase/SKILL.md`). A script with no stated retirement is not finished.
- **Review** — matches the operation it serves; a one-time `DELETE` of populated rows still needs a
  principal because the data is gone irreversibly (`../../skills/classify-mechanism/SKILL.md`).

The named failure this folder exists to prevent: the one-time script left in past prod, re-executing
stale logic on every publish until the post-deploy is a monolith nobody trusts. The death-certificate
header + the deprecation train is the antidote. See `../../skills/deploy-scripts/SKILL.md`
§"Retirement is a tracked act". `Release_2026.07_email_normalize.sql` is the worked exemplar.
