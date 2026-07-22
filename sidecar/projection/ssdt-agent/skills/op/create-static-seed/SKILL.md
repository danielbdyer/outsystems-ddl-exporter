---
name: create-static-seed
description: Use when the developer says "add a Status lookup", "create a static entity for order types", "I need a reference table with these fixed values", "a lookup entity with Active/Inactive" — a new OutSystems Static Entity whose rows are part of the model. SSDT destination = a declarative CREATE TABLE plus an idempotent MERGE seed in the post-deploy script.
---

# Create static / lookup entity (non-idempotent-seed + IDENTITY-lookup traps)

> **Default (provisional — the data decides).** Ships as one release: the schema change, then a
> post-deployment script that runs the idempotent seed after it lands. Any team member can review
> this — the change is additive and the running application is unaffected. Prove the redeploy is
> silent before the classification holds.

## OutSystems phrasing
"add a Status lookup", "create a static entity for order types", "a reference table with these fixed values", "a lookup with Active/Inactive".

## SSDT meaning
A declarative `CREATE TABLE` for the lookup (schema slot) PLUS a seed in `Script.PostDeployment.sql`
(data slot), usually `:r`-including `Data/Seed.sql`. The CREATE is pure structure; the rows are an
idempotent MERGE. Lookup keys are **explicit IDs, NOT IDENTITY** — the IDs are part of the model and
must be identical in every environment so the app can reference `StatusId = 3` by constant. Never
write ALTER.

## The named trap
Two named traps, both owned elsewhere: the **non-idempotent seed** (a bare INSERT that duplicate-keys
on the second deploy) and the **IDENTITY lookup** (auto-assigned IDs drift between environments). Both
are the idempotent-seed concern — see `../../_index/idempotent-seed/SKILL.md`; do not re-derive the
guarded-MERGE or explicit-ID reasoning here.

## How it flips (the specifics only)
- fresh lookup, explicit IDs, guarded MERGE → ships as one release (the schema change, then the
  post-deploy seed after it lands); any team member can review it — additive, the running application
  is unaffected.
- lookup is an **FK target** for other entities → seed the lookup **before** its children
  (parents-first); still one release, but a missing parent row makes a child's foreign key block the
  deploy.
- **+ >1M reference rows** (rare) → added scrutiny: at production row counts the seed may block writes
  or run long — schedule a window.

## Prove it
Deploy once (seed lands), then **deploy a SECOND time unchanged** and assert the post-deploy reports
**0 rows affected** + an **identical data-hash** (order-independent `SHA2_256(FOR XML RAW)` sum). A
changed hash on an unchanged seed means the MERGE is rewriting rows — fix the guard. See `prove-on-dacpac` for the publish loop and
`talk-to-local-sql` for the hash probe. On the enriched sample, `dbo.Category` (explicit-id,
`IsActive DEFAULT 1`) is the ready-made seed target.

## The verdict (to the developer)
"You asked for a Status lookup, and it's in — a declarative table plus an idempotent seed. The IDs are
explicit rather than IDENTITY, so `StatusId = 3` points at the same row in every environment and the
app can rely on that constant. On a disposable copy of your data the seed was deployed twice with no
changes: the second deploy touched zero rows and left an identical data hash, so redeploys are silent
— they won't rewrite the reference data."

## The reasoning (in conversation)
For reference data, the correctness property is that *re-running changes nothing* — and a silent second
deploy is how you prove it. The fail mode you're avoiding is a seed that rewrites its rows on every
deploy, which is broken even when the values still match. Full reasoning:
`../../_index/idempotent-seed/SKILL.md`.

## On the record
The fragment this op contributes to the pull request (`../../author-pr/SKILL.md`).

**Review & release**
- Any team member can review this: the change is additive and the running application is unaffected.
- Ships as one release: the schema change, then a post-deployment script that runs the idempotent seed
  after it lands.
- Added scrutiny: none — unless it holds more than a million reference rows (at production row counts
  the seed may block writes or run long — schedule a window).

**Verification** — run in each environment after deployment
```sql
-- expect the lookup's model rows, unchanged, by explicit Id (e.g. dbo.Category)
SELECT Id FROM dbo.<lookup> ORDER BY Id;

-- expect an identical order-independent SHA2_256(FOR XML RAW) hash sum in every environment:
-- the IDs are explicit, so the content matches. The hash probe is in talk-to-local-sql.
```

**Rollback**
The seed rows are model data held in `Data/Seed.sql`, so removing the lookup loses no unique values:
drop any child foreign keys that reference it, then drop the table; a redeploy re-runs the same
idempotent seed to restore the rows.

**Not verified**
- Application impact. Whether the running application references an Id the seed does not provide is not
  confirmed by a disposable copy — the app owner confirms the ID set.
- Other environments. A copy proves the redeploy is silent for this data shape; whether another
  environment already holds drifted IDs from a prior IDENTITY-seeded table is not visible here — run
  the verification query before promotion.
- Production scale / timing. The MERGE's cost at production row counts is not shown by a small copy.
- Reversibility. The publish loop exercises the forward redeploy only; dropping the lookup is not
  exercised here.
