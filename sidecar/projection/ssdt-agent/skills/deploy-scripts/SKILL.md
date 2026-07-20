---
name: deploy-scripts
description: The deployment-script lifecycle rails. Use whenever a change needs a pre-deployment, post-deployment, or ad-hoc script — reason about whether a script is needed at all (pure-declarative first), where it goes (pre vs post vs out-of-DACPAC), what permanence class it is (the folder is the contract), how it proves idempotent (silence on redeploy), and when it retires (a tracked act, not a vibe). Walks the six gates the change-author follows when authoring scripts. Composes classify-mechanism (the five mechanisms), _index/idempotent-seed (silence-is-the-proof), _index/tightening-class (the pre-deploy trap), _index/multi-phase (phase-bound death), and prove-on-dacpac/talk-to-local-sql (the proof). Does NOT re-derive those concerns — it points to them.
---

# Deployment scripts — the lifecycle rails

> **The one line.** The folder is the contract; the header is the memory; the no-op redeploy is the
> proof; retirement is a tracked act. Hold those four and the post-deploy stays an instrument of
> execution instead of an accreting monument to everything the team was once afraid to delete.

SSDT is **state-based**: you declare the end state and `sqlpackage` computes the delta. But two of
the five mechanisms (`classify-mechanism`; handbook file 15 = §18.1, the mechanism axis) hand SSDT a
T-SQL script it runs *around* that delta — pre-deployment before it, post-deployment after it. The
fact everything here hangs on:

> **`Script.PostDeployment.sql` is not a migration runner. It has no "which scripts already ran"
> ledger. It executes in full, top to bottom, on every single publish.**

Three consequences, and they are why "how long does a script stay in the file?" is even a question:

1. **Permanence is the default.** A script you add re-runs forever until a human deletes the file.
2. **Idempotency is mandatory, not a nicety.** Anything that runs on every publish must be safe to
   run on every publish. A script correct only the first time is a latent production incident.
3. **Transience must be actively managed.** A one-time fix does not remove itself. If you want it
   gone, that is a tracked act — a PR that deletes the file. Left undone, the post-deploy accretes
   into a monolith nobody trusts and every deploy re-executes.

The discipline is the answer to (3): at the moment a script is born, decide what will end it, and
write that decision where the next reader cannot miss it.

## The permanence-class spine (the folder is the contract)

Every deployment script belongs to exactly one class. The class is not a comment — it is the
**folder**, and the folder is a contract about lifespan, guard, re-run behaviour, and review. Get the
class right and the rest is mechanical.

| Class | Folder | Guard that keeps it silent | Re-run | Lifespan | Retirement trigger | Default review |
|---|---|---|---|---|---|---|
| **Pure declarative** (no script) | — (the `.sql` model) | n/a — SSDT computes the delta | n/a | forever (it *is* the model) | never | per the op |
| **Permanent · idempotent** | `/Migrations/`, `/ReferenceData/` | natural: `WHERE … IS NULL`, `IF NOT EXISTS`, guarded `MERGE` | **silent** — 0 rows, identical hash | **forever** | **never** | usually any team member |
| **Guarded · marker-inert** | `/Migrations/` | a `MigrationHistory` marker row, not natural idempotency | inert after first success per env | forever *until every env is past it* | eligible once the marker exists in all envs | reviewer, sometimes principal |
| **Transient · one-time** | `/OneTime/` | idempotent so a lagging env can re-run safely | silent, but slated to leave | until prod-confirmed + one sweep | **prod-applied → deprecation-train PR removes the file** | matches the op it serves |
| **Ad-hoc · outside the deploy** | `/AdHoc/` (checked in, *not* run by the DACPAC) | idempotent + chunked + resumable | run by a human, possibly interrupted | until reconciled + prod-confirmed | archived after reconciliation proves no drift | **principal** (runs outside the safety net) |

The two `classify-mechanism` findings stay independent here too: **how a script ships is not who must
review it.** A one-time `DELETE` of populated rows ships as a single transient post-deploy script, yet
a principal must review it because the data is gone irreversibly. Never fold review level into how
simply the script ships.

**The classes in one breath.** *Pure declarative* is the first and best answer — no script at all;
SSDT already does add-column, widen, add-nullable, add-index, rename-with-refactorlog. *Permanent ·
idempotent* is a reference-data seed or a model-restoring backfill — an invariant made executable,
earning permanence by proving silent on re-run, carrying no death certificate. *Guarded · marker-inert*
is a migration that can't be written as a natural idempotent statement; the marker is a smell (prefer
converting it). *Transient · one-time* is born with a death certificate. *Ad-hoc* is the escape hatch
and the drift frontier (below).

## Placement — pre vs post, and the trap in it

Execution order (handbook file 06 = Pre-Deployment-and-Post-Deployment-Scripts):

```
1. PRE-DEPLOYMENT   → runs BEFORE the schema delta; DB still in the OLD shape
2. SCHEMA DELTA     → SSDT's ALTER/CREATE/DROP; OLD → NEW
3. POST-DEPLOYMENT  → runs AFTER the delta; DB now in the NEW shape
```

Placement is dependency direction:
- **Pre-deployment** when the data work must happen **to unblock** the schema change — drop a
  dependency blocking an `ALTER`, delete orphans before an FK is trusted, clear a violation before a
  constraint is added.
- **Post-deployment** when the data work **depends on the new shape existing** — backfill a new
  column, seed a lookup, migrate into a new structure, populate the far side of a reshape.

**The trap: the plan is computed before the pre-deploy runs.** SSDT computes the whole deployment plan
*before* the pre-deployment script executes. So a pre-deploy that backfills NULLs **cannot** license a
`NULL → NOT NULL` tightening in the *same* deploy on a **populated** table — the
`BlockOnPossibleDataLoss` guard fires on **row presence**, not NULL content. The backfill is necessary
but not sufficient. This is the tightening class; its WHY is owned by
`../_index/tightening-class/SKILL.md` — do not re-derive it. **Rail:** never tell a developer a
pre-deploy backfill "solves" a tightening on a populated table; that change is genuinely multi-phase,
or it needs a *logged* `BlockOnPossibleDataLoss` relaxation scoped to one deploy (a principal's call).
Probe to predict (`../talk-to-local-sql/SKILL.md`), let the Strict publish prove
(`../prove-on-dacpac/SKILL.md`).

**The transaction boundary.** Pre-/post-deploy scripts run inside the deployment's transaction by
default — a failed statement rolls the whole deploy back. That is the safety you want for small, fast
data work, and exactly the wrong container for a long-running or lock-heavy operation. That boundary
is the hinge that sends the largest operations *out* of the DACPAC entirely.

## Ad-hoc — the escape hatch and the drift frontier

Ad-hoc lives **outside** the source of truth, which is why it is the most dangerous. A script leaves
the DACPAC for exactly two legitimate reasons — **scale/lock** (too long or too lock-heavy for the
single deploy transaction) or a **true one-off** (a production correction under a change ticket that
does not belong in the versioned deploy path). If neither holds, it is not ad-hoc — it belongs in
pre/post as a proper transient script.

Because it runs outside the pipeline, an ad-hoc script earns its way back to trustworthy through four
obligations: **justify** (state in the header why it can't ride in pre/post) · **idempotent · chunked ·
resumable** (a human runs it, possibly interrupted) · **leave a trace** (check it into `/AdHoc/` with
ticket, date, operator, environments-applied, and the batching parameters used — a thing that ran
against prod with no mark in source control *is* drift) · **reconcile** (after the motion, the
declarative model and any permanent seed must still describe the resulting state; update the seed so a
fresh deploy reproduces it, then prove no drift — a clean schema compare and, where data is governed,
a matching content hash via `../talk-to-local-sql/SKILL.md`). Highest scrutiny; **principal review by
default.** "Removal" for ad-hoc means archiving out of `/AdHoc/` once reconciled and prod-confirmed.

## The header is the memory (birth writes the death certificate)

Every script carries a provenance header, and the decision that ends it is written at birth. A
**permanent** script asserts it will never leave; a **transient** header *is a death certificate*
naming the trigger and the work item that removes it; a **guarded** header names the marker and the
all-environments-past-it out.

```sql
/*
  [TRANSIENT · one-time · REMOVE AFTER PROD]  OneTime/Release_2025.02_PhoneFix.sql
  Ticket:        JIRA-1456
  What:          One-shot correction of legacy-import phone formats.
  Guard:         idempotent (WHERE PhoneNumber NOT LIKE '+%') — safe to re-run in a lagging env.
  Prod-applied:  <stamp on the prod deploy>
  Removal:       JIRA-1502 — sweep on the deprecation train once prod-confirmed. Delete file; keep git history.
*/
```

A permanent header ends `Retire: never — model-restoring; re-establishes state on any fresh deploy.`
A phase-bound header names the **phase/release** that ends it, not just "after prod" (see retirement).

## Silence is the proof (do not re-derive — point)

Anything that runs every publish is proven by what it *doesn't do* on the second run. The no-op
redeploy asserts all three: **0 rows affected** · **identical content hash** (order-independent
`SHA2_256(FOR XML RAW)`, NULL distinct from `''`) · **0 CDC captures** if the table (or its consumer)
is CDC-tracked. A non-zero result on a no-op redeploy is the anti-proof — the guard is wrong (usually
an unconditional `WHEN MATCHED`); fix it and confirm silence. This is owned by
`../_index/idempotent-seed/SKILL.md` and run by `../prove-on-dacpac/SKILL.md` over
`../talk-to-local-sql/SKILL.md`. *"The values match" is not the same as "the redeploy was silent."*

## Retirement is a tracked act, on a cadence

The direct answer to "how long does it stay?": a one-time script is **not** deleted the instant it
runs — a downstream env may still be behind, and you may need to re-run it there (which is why it
stays idempotent). It stays, dated and work-item-attached, **until confirmed applied in production,
then removed on the next deprecation-train sweep as a tracked PR** (removal leaves the deploy path but
keeps git history). Permanent scripts carry **no** death certificate and stay forever — they earn that
by proving silent on re-run. Neither *delete-immediately* (you lose re-apply to a lagging env) nor
*leave-forever* (the post-deploy monolith) is the rail; **prod-confirmed → next deprecation train →
tracked removal PR** is.

**Multi-stage changes stagger the deaths.** A single logical change (expand/contract, split, retype,
extract-to-lookup) spawns scripts across releases, each with its own lifecycle. The rail specific to
it: **a transient script must be retired no later than the phase that invalidates its assumptions** —
a backfill that populates `ColumnB` from `ColumnA` dies when `ColumnA` is dropped; leave it and the
build breaks (it references a missing column) or re-runs stale logic against a vanished shape. The
header's `Removal:` names the phase/release, not just "after prod". The full multi-phase reasoning
(cardinality, coexistence, the forward-only limit) is owned by `../_index/multi-phase/SKILL.md`; this
is only the script-lifecycle limb of it.

## The six gates (the ordered protocol)

For any change that might need a deployment script, walk these in order. Each gate is provisional
until `../prove-on-dacpac/SKILL.md` confirms it — classification from the `.sql` text alone is a guess;
the data decides.

- **Gate 0 — Does this need a script at all?** Default to **pure declarative**. If SSDT's own delta
  does the work (add-column, widen, add-nullable, add-index, rename-with-refactorlog), write **no
  script**. Refuse script sprawl. This is the "no more and no less" rail — the precise change is
  often *no script*.
- **Gate 1 — Pre, post, or ad-hoc?** Dependency direction decides pre vs post; the transaction
  boundary + scale/lock decides whether it leaves the DACPAC. If ad-hoc, escalate to principal now and
  attach the four obligations.
- **Gate 2 — Permanence class → folder + guard + header.** Assign the class; it dictates the folder,
  the guard style, and the header template. If transient, the header **must** carry a death
  certificate with a removal work item.
- **Gate 3 — Prove idempotent (the gate nothing skips).** The no-op redeploy on a disposable copy:
  0 rows · identical hash · 0 CDC. A non-silent redeploy is a bug even when the final data is correct.
- **Gate 4 — State the retirement condition explicitly.** Permanent → "Retire: never". Transient →
  the trigger (prod-confirmed → deprecation train) + work item. Phase-bound → the phase/release.
  Guarded → the all-environments-past-it condition. A script with no stated retirement is not done.
- **Gate 5 — Reconcile (no drift).** Confirm the model + any permanent seed still describe the
  resulting state; after ad-hoc motion, update the governing seed and prove a clean schema compare +
  matching content hash. Drift is the failure this gate catches.
- **Gate 6 — Record the fragment.** Contribute to `../author-pr/SKILL.md`: the mechanism, the
  permanence class, the retirement condition, the idempotency proof (the three silent-redeploy
  assertions), and the review level with any added scrutiny — *how it ships* and *who reviews it* kept
  as two independent findings.

## The named traps (recognize on sight, stop)

- **Unconditional `WHEN MATCHED`** — rewrites every seed row on every deploy; the CDC-silence
  violation (`../_index/idempotent-seed/SKILL.md`).
- **Pre-deploy backfill mistaken for a tightening solution** — the plan is computed before the
  pre-deploy runs; the guard is row-presence, not NULL-content (`../_index/tightening-class/SKILL.md`).
- **One-time script left in past prod** — the `/OneTime/` folder that never gets swept; the post-deploy
  monolith. The death-certificate header + deprecation train is the antidote.
- **Transient script that outlives the column it references** — a phase-bound backfill not retired in
  the contract phase (`../_index/multi-phase/SKILL.md`).
- **Ad-hoc run with no source-control trace** — a sanctioned bypass that forked the truth; `/AdHoc/`
  check-in + reconciliation closes it.
- **Marker table used to fake idempotency** — a `MigrationHistory` guard papering over a
  non-idempotent operation that should have been rewritten. The marker is a smell, not a destination.
- **Long-running migration inside the deploy transaction** — rollback-and-timeout hazard; belongs in
  ad-hoc, chunked and resumable.
- **Hard `DELETE` of a referenced lookup value** — orphans fact rows, breaks the app's constant.
  Deactivate, don't delete (`../op/delete-seed-value/SKILL.md`).

## Honest limits (state them; don't let a green run overclaim)

The disposable substrate (`../talk-to-local-sql/SKILL.md`) is real-*shaped*, not real-*sized*, and
runs the **forward** publish only. It cannot prove production-scale timing or blocking (`>1M rows` is
added scrutiny — schedule a window); it cannot prove reversibility or that the running application
still works against the new shape (`@app-owner`, not verified here); it holds one catalog — other
environments may hold rows this copy does not, and External-Entity / ETL / report consumers are
dependency scope, not proven here.

## The verdict — to the developer (the mentor register)

Match the conversation register of `../../THE_RECORD.md` §3: teach the *why* briefly, tie back to
owned phrases, make the change easy.

> "This needed a post-deploy backfill, not just a schema edit, because SSDT can't know what value to
> put in the new column for existing rows. I put it in `/Migrations/` as a permanent, idempotent
> script — guarded by `WHERE … IS NULL`, so on a disposable copy of your data the second deploy moved
> **zero rows** and left an identical hash. That silence is the proof it's safe to re-run every
> publish. It carries no removal date because it re-establishes state on any fresh deploy — that's
> what earns it a permanent home. Contrast last release's phone-format fix: that one lives in
> `/OneTime/` with a death certificate and gets swept on the next deprecation train once it's
> confirmed in prod."

## On the record — the evidence fragment (for author-pr)

What this skill contributes to the pull request (`../author-pr/SKILL.md`), in the record register —
**almost exclusively presentation of evidence**, the precise scripts required and nothing more:

**Review & release**
- Who must review, and why — from the permanence class (any team member for a permanent idempotent
  seed; a principal for an ad-hoc or an irreversible one-time `DELETE`), independent of how it ships.
- How it ships — the mechanism (pre-deploy prepares then the delta lands; the delta then a post-deploy;
  a scripted change; staged across releases), plus the permanence class and folder.
- Added scrutiny, if any — CDC-tracked / `>1M rows` / first-time on this estate, each on its own line.

**Deployment evidence** — findings with proof beneath, no narration:
- the exact pre-/post-deployment script(s) shipped, each with its permanence class and header;
- the idempotency proof — the no-op redeploy's three assertions verbatim (0 rows affected · identical
  content digest before/after · 0 CDC captures if tracked);
- the sanity/verification SQL run before the change was allowed to land, with its expected result.

**Retirement** — the stated condition for every transient/guarded/ad-hoc script (the trigger + work
item, or the phase that ends it); "Retire: never" for permanent scripts, with the reason.

**Not verified** — the standing limits above, specific to this change.

## Hard rules

- **Gate 0 is a refusal.** No script when the schema diff already does the work. Precise means *no
  more and no less*.
- **Compose; duplicate nothing.** Silence-proof → `../_index/idempotent-seed/`; the tightening trap →
  `../_index/tightening-class/`; phase-bound death → `../_index/multi-phase/`; deactivate-don't-delete
  → `../op/delete-seed-value/`; the five mechanisms → `../classify-mechanism/`; the proof loop →
  `../prove-on-dacpac/` + `../talk-to-local-sql/`. Point to each owner; never re-derive its WHY here.
- **You scaffold; you do not ship a wrapper.** The agent authors the scripts and runs the proof
  commands itself.
- **Everything authored lives under `ssdt-agent/`.** Scripts under proof live in the
  `proving-ground/` class folders (or a per-executor scratch under `../../self-test/PROTOCOL.md`).
