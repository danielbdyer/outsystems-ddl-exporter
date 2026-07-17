---
name: idempotent-seed
description: Cross-cutting KNOWLEDGE shared by create-static-seed, edit-seed, delete-seed-value, extract-to-lookup (its seed leg), and data-plane row ops. Owns the idempotent guarded MERGE (WHEN MATCHED ... AND value-differs, null-safe with NULL distinct from ''), explicit-IDs-not-IDENTITY for lookup keys (a constant must mean the same row across environments), deactivate-don't-delete (IsActive=0, never a hard DELETE that orphans fact rows), and silence-is-the-proof (0 rows affected + identical content-hash + 0 CDC captures on a no-op redeploy). Per-op skills POINT here. Points to _index/cdc for the CDC-silence face; the hash check lives in talk-to-local-sql.
---

# Idempotent seed — re-running changes nothing, and silence is the proof

> Reference rows are **part of the model**, so a redeploy must be a **no-op.** The strongest proof
> a seed is correct is that the second deploy touches **zero** rows, produces an **identical hash**,
> and captures **zero** CDC changes. Every static-data op points here so the seed discipline is the
> same, every time.

You are helping an **OutSystems-native developer** with Static Entities — the small enumerated
tables (Status, Country, OrderType) whose rows *are* the model, not user input. In SSDT their rows
live in a **post-deployment script** as an **idempotent MERGE**, because the schema project
describes structure and seed *data* rides in the deploy's data slot.

## The four disciplines this concern owns

### 1. The idempotent guarded MERGE

Every seed must produce the same result whether the deploy runs once or a hundred times. Never a
bare `INSERT` (duplicate-keys on the second deploy). Use a MERGE whose branches are:

- `WHEN NOT MATCHED BY TARGET THEN INSERT` — the new row.
- `WHEN MATCHED AND <value actually differs> THEN UPDATE` — **guarded.** The `AND` is
  load-bearing: an *unconditional* `WHEN MATCHED` rewrites every matched row on **every** deploy.
- **Null-safe comparison** — treat `NULL` and `''` as **distinct**, and compare with
  `IS DISTINCT FROM`-style guards (not `=`, which is *unknown* for NULL), so a NULL value neither
  false-matches nor false-updates.
- `WHEN NOT MATCHED BY SOURCE THEN DELETE` — **only with care and a scoped source**; it deletes
  rows the seed simply didn't list. Prefer omitting it (see deactivate-don't-delete).

### 2. Explicit IDs, NOT IDENTITY, for lookup keys

Lookup IDs are **part of the model** and must be identical in every environment, so the app can
reference them by constant. IDENTITY lets IDs **drift** between dev and prod, so the app's hard-coded
`StatusId = 3` silently means different rows in different environments. Seed lookups with **explicit
IDs** (`SET IDENTITY_INSERT` bracketing only if the column happens to be IDENTITY-shaped); a lookup
whose key is IDENTITY is a trap to recognize and refuse.

### 3. Deactivate, don't delete

A lookup ID is an **identity other rows depend on.** A hard `DELETE` of a seed row doesn't "remove a
value" — it **orphans every fact row** pointing at it and breaks the app's `StatusId = 3` constant.
The discipline is **`IsActive = 0`**: retire the value while preserving its identity and history. A
hard DELETE that orphans fact rows removes data irreversibly and is usually **wrong** — refuse it and
propose deactivation. A deletion pressed anyway is a principal's call, because the removal cannot be
undone.

### 4. Silence is the proof (specialize per op; do not restate the whole thing there)

A load is correct only if **re-running it is a no-op.** The canonical proof is the **silent
redeploy**: deploy the seed, then redeploy with identical source and assert all three:

1. **0 rows affected** reported by the post-deploy.
2. **Identical content-hash** before and after (the order-independent `SHA2_256(FOR XML RAW)` sum
   from `../../talk-to-local-sql/SKILL.md`, NULL kept distinct from `''`).
3. **0 CDC captures** on the second deploy, *if* the table (or its consumer) is CDC-tracked.

A non-zero capture count on a no-op redeploy is the **anti-proof** — it means the `WHEN MATCHED` is
unconditional and over-capturing. Fix the guard, re-run, confirm silence. Silence is *the strongest
guarantee* precisely because it is the **absence of an event** you'd otherwise have to trust.

## The WHY

For reference data, **"the values match" is not the same as "the redeploy was silent."** An
unconditional MERGE *looks* idempotent (the final values are right) but rewrites every row on every
deploy — and on a CDC-tracked table that is the loud failure: the feed reports the whole table as
phantom changes. So you prove a load by what it *doesn't do* on the second run, and treat a
non-silent redeploy as a bug even when the data ends up correct.

## The ops this governs (and their distinguishing note)

- **create-static-seed** — the CREATE is declarative (schema slot); the seed is the guarded MERGE
  (data slot); explicit IDs. Ships as one release — the schema change, then the post-deployment
  MERGE that runs after it lands; any team member can review it, since it is additive and the
  running application is unaffected.
- **edit-seed** — add a value (`WHEN NOT MATCHED`) or change a label (**one** guarded `WHEN MATCHED`
  row — prove the branch touches exactly 1 row, not the table size).
- **delete-seed-value** — **deactivate, don't delete**; refuse the hard DELETE that orphans fact
  rows.
- **extract-to-lookup (seed leg only)** — seeding the new lookup with the distinct existing values
  is idempotent-MERGE work; the *rest* of extract-to-lookup (the FK backfill + old-column drop) is
  multi-phase, see `../multi-phase/SKILL.md`.
- **data-plane row ops** — the four row fates (insert / guarded update / unchanged / careful
  delete) under one null-safe MERGE; a bulk DELETE of populated rows removes data irreversibly, so a
  principal must review it.

## The CDC-silence face (pointer)

The "0 CDC captures on redeploy" limb of the proof, the over-capturing-MERGE failure mode, and the
isolation rule all live in `../cdc/SKILL.md`. This concern owns the *seed*; the CDC index owns the
*capture* consequence. Point there, don't restate it.

## Prove it (pointer, not a re-scaffold)

For the content-hash check (identical-before-and-after) and the guarded-branch rowcount probe, see
`../../talk-to-local-sql/SKILL.md`; for the deploy-twice loop that demonstrates the silent redeploy,
see `../../prove-on-dacpac/SKILL.md`.

## Handbook

Cite by **filename**: **07-Idempotency-101.md** (the idempotent-MERGE discipline) and
**06-Pre-Deployment-and-Post-Deployment-Scripts.md** (where seed data rides in the deploy).
