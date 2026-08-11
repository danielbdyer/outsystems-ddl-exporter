# Migrations/ — permanent · idempotent (the folder is the contract)

Scripts here are **permanent and idempotent**: model-restoring backfills and one-off-shaped data
motions that earn a permanent home by **proving silent on re-run**. They carry **no death
certificate** — they re-establish the intended state on any fresh deploy, so they stay forever.

Contract for every file in this folder:
- **Guard** — natural idempotency (`WHERE … IS NULL`, `IF NOT EXISTS`, a guarded `MERGE`), never a
  `MigrationHistory` marker (a marker means the operation wasn't naturally idempotent — that is the
  *guarded · marker-inert* class, and the marker is a smell to convert away from).
- **Silent on redeploy** — the no-op redeploy touches **0 rows**, leaves an **identical content
  hash**, and captures **0 CDC** rows if the table is tracked. That silence is the proof
  (`../../skills/_index/idempotent-seed/SKILL.md`).
- **Header** — `[PERMANENT · idempotent]`, what it does, why it is silent, and `Retire: never` with
  the reason. See `../../skills/deploy-scripts/SKILL.md` §"The header is the memory".
- **Review** — usually any team member (a permanent idempotent seed is additive); the class does not
  set review level — the operation does (`../../skills/classify-mechanism/SKILL.md`).

`001_backfill_customer_region.sql` is the worked exemplar. Wire a script into the deploy by adding
its `:r` include, grouped under the migrations heading, in `../Script.PostDeployment.sql`.
