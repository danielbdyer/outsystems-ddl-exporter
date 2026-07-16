---
name: cdc
description: Cross-cutting KNOWLEDGE — the tripwire that adds scrutiny to every change on a CDC-tracked table, and the standing capture-instance obligation. Shared by enable-cdc, recreate-capture-instance, change-tracking (the lighter sibling), and the added-scrutiny face of EVERY op on a CDC-tracked table. Owns why CDC is NOT in the dacpac model (it ships as a scripted change; the declarative attempt is silently ignored), the frozen capture instance that forces capture-instance management on every future change, the SILENCE failure mode (a missing column in the feed, no error), and the MANDATORY-ISOLATION rule (sp_cdc_enable_db flips instance-wide state — unique DB only, never the shared warm container). Per-op skills POINT here. The isolated-DB substrate lives in talk-to-local-sql.
---

# CDC — the cross-cutting tripwire and the standing obligation

> ⚠️ **CDC is the team's single biggest tripwire. Read this before touching any CDC-related
> change, and before touching ANY change on a table that is already CDC-tracked.** ⚠️
>
> Change Data Capture is **not expressible in the declarative dacpac model.** Every rule the rest
> of the tree relies on — "edit the CREATE, never write ALTER"; "SSDT computes the migration path" —
> **breaks** the moment a table is CDC-enabled. Every op that touches CDC, or touches a CDC-tracked
> table, points here so the obligation is named the same way, every time.

You are helping an **OutSystems-native developer** who said "track changes." Your first job is to
find out *which* kind of history they mean (CDC vs. change-tracking vs. temporal — see
disambiguation below); your second is to make the standing consequence **loud**, because it is the
thing that bites their whole team for months.

## Three faces of the CDC Surprise (handbook 16 = §19)

1. **It is outside the declarative model.** `sp_cdc_enable_table` (and `sp_cdc_enable_db` at the DB
   level) create `sys`-owned change tables, a capture instance, and SQL Agent capture/cleanup jobs.
   SSDT cannot CREATE, diff, or publish any of it. A declarative attempt produces **nothing** — the
   dacpac **silently ignores it**. CDC belongs in a post-deploy / operational script, full stop — it
   ships as a scripted change, because enabling CDC cannot be expressed as a table definition.
2. **It is Enterprise/Standard-licensed.** On the wrong edition the enable call **fails at runtime,
   after deploy** — not at build. The disposable copy of Dev must catch the edition before the
   change is called done.
3. **The standing obligation — the capture instance is FROZEN to the table's current shape.** Once
   CDC is on, *every* future schema change on that table — even a trivial nullable add — must
   recreate or dual-instance the capture, or the new column is **silently absent from the feed.**
   This is why every future change on a CDC-tracked table carries added scrutiny — the capture
   instance is frozen to the columns it was created for and needs deliberate handling — and why the
   *danger*, not the one-line script, drives that scrutiny.

## The WHY (specialize per op; do not restate the whole thing there)

CDC must ship as a scripted change because **the model is the schema, and CDC is not in the model.**
The moment a change has no declarative destination, it leaves the edit-the-CREATE world entirely. The
deeper why is the **standing obligation**: the capture instance freezes at the shape it was created
for, so the correct question about any change on a CDC table is not only "what does this edit do
today?" but "what obligation does the frozen capture instance impose on *this and every future*
change?"

## The failure mode is SILENCE (this is what makes CDC dangerous)

CDC's dangerous outcome is **not a loud refusal** — it is a **quiet gap.** Add a column to a
CDC-tracked table without recreating the capture instance and the new column's changes are simply
**absent from the feed. No error.** The warehouse silently misses a column until someone notices
weeks later. So the CDC proof is *inverted* from the rest of the tree: the existing instance is
proven **not** to surface the new column (`sys.sp_cdc_get_captured_columns` lacks it), then the dual
instance is proven to. And the *good* CDC result is also silence — a no-op redeploy that captures
**0** changes is the CDC-silence idempotency proof (see `../idempotent-seed/SKILL.md`).

## The dual-instance pattern (no-gap requirement)

When the downstream consumer must miss **nothing** (a no-gap requirement), a schema change on a CDC
table ships across multiple releases, each its own pull request, so the consumer keeps reading
without a gap while the change is in flight:

- Create a *second* capture instance (`Customer_v2`) for the new shape:
  `sp_cdc_enable_table @capture_instance = 'Customer_v2'`.
- Let consumers drain the old instance (`Customer_v1`).
- Cut consumers over to v2, then drop v1.

The two instances **coexist** across releases — the same coexistence shape as any multi-phase
change (see `../multi-phase/SKILL.md`). When no-gap is *not* required (consumers tolerate a brief
gap), the single instance is dropped and recreated in one release — still a scripted change, one
pull request, and a dev lead reviews it.

## The lighter sibling — change-tracking (match weight to need)

Developers say "track changes" for both CDC and change-tracking. **Change tracking** records *which*
rows/columns changed + a version number (sync-oriented, all editions, no capture instances, **no
standing capture-instance obligation**); **CDC** records *what the values changed from* (the full
feed, with all its weight). Reaching for CDC when change tracking would do takes on the entire CDC
obligation for nothing. Both are operational `ALTER`s outside the dacpac. The discriminator: **do
you need the old values, or only which keys moved?**

## MANDATORY ISOLATION (a hard boundary, not a preference)

`sp_cdc_enable_db` flips **instance-wide** state. CDC proving **must** run on a **unique, disposable,
isolated DB** — never the shared warm container. On the shared instance, that instance-wide flip
disrupts every other executor's work. This ties directly to **CLAUDE.md survival rule 1** (CDC test
classes always use an isolated fixture) and **self-test/PROTOCOL.md §8**. See
`../../talk-to-local-sql/SKILL.md` for the per-executor `PG_<testId>_<rand>` isolation the CDC ops
inherit.

## The ops this governs

- **enable-cdc** — the tripwire itself; ships as a scripted change (a post-deploy / operational
  script, not a table definition). A dev lead must review it, with added scrutiny when it is the
  first CDC enablement on the estate.
- **recreate-capture-instance** — the standing obligation realized; a scripted change, dual-instance
  across releases when the downstream consumer must miss nothing.
- **change-tracking** — the lighter sibling; also a scripted change, but it carries no standing
  capture-instance obligation and a lighter review need.
- **the added-scrutiny face of every other op** on a CDC-tracked table (add-optional, make-mandatory,
  rename, identity-swap, split…): each carries its own base review need **plus** the capture-instance
  obligation from here.

## Prove it (pointer, not a re-scaffold)

For the isolated disposable DB the CDC proof must run on, and the `docker exec` sqlcmd form, see
`../../talk-to-local-sql/SKILL.md`. For the loop that proves the declarative attempt yields an empty
delta (so it must be a script), see `../../prove-on-dacpac/SKILL.md`.

## Handbook

Cite by **filename**: **12-CDC-and-Schema-Evolution.md** (how CDC works + capture-instance
management), **27-CDC-Table-Registry.md** (which tables carry the obligation), and handbook **16**
(= §19; the CDC Surprise anti-pattern). recreate-capture-instance is handbook **14** (= §17.9).
