# AdHoc/ — outside the deploy (the escape hatch and the drift frontier)

Scripts here are **checked in but NOT run by the DACPAC** — they live outside the source of truth,
which is why they are the most dangerous class and carry **principal review by default**. A script
leaves the deploy path for exactly two legitimate reasons, and only two:

1. **Scale / lock** — too long-running or too lock-heavy for the single deploy transaction (a batched
   backfill of millions of rows, an online index rebuild, a chunked re-key). The transaction boundary
   makes riding in pre/post a rollback-and-timeout hazard.
2. **True one-off** — a production correction under a change ticket that does not belong in the
   versioned deploy path at all.

If neither holds, it is **not** ad-hoc — it belongs in `../OneTime/` (or `../Migrations/`) as a proper
in-pipeline script.

The four obligations that earn an ad-hoc script back to trustworthy (owned by
`../../skills/deploy-scripts/SKILL.md` §"Ad-hoc"):
- **Justify** — the header states the scale/lock or true-one-off reason.
- **Idempotent · chunked · resumable** — a human runs it, possibly interrupted, possibly against a
  lagging env.
- **Leave a trace** — ticket, date, operator, **environments-applied**, and the batching parameters
  actually used. A thing that ran against prod with no mark in source control *is* drift.
- **Reconcile** — after the motion, update the declarative model and any permanent seed so a fresh
  deploy reproduces the resulting state, then prove no drift: a clean schema compare and a matching
  content hash.

Nothing in this folder is `:r`-included by `../Script.PostDeployment.sql` — that is the point.
"Removal" for an ad-hoc script means archiving it out of this folder once reconciled and
prod-confirmed.
