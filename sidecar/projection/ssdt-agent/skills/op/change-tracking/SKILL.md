---
name: change-tracking
description: Use when the developer says "I just need to know which rows changed since last sync", "change tracking for the mobile sync", "a lightweight what's-new since timestamp X", "tell me which records moved, I don't need the old values" — the light sibling of CDC. SSDT destination = an operational ALTER script (Mechanism 4 Script-Only), NOT declarative, with none of CDC's standing tax.
---

# Enable change tracking (CDC-vs-change-tracking conflation trap)

> **Default (provisional — the data decides).** Mechanism 4 Script-Only, single-PR script, Tier 2 — much lighter than CDC: no capture instances, no per-feed management, all editions.

## OutSystems phrasing
"I just need to know *which* rows changed since last sync", "change tracking for the mobile sync", "a lightweight 'what's new' since timestamp X".

## SSDT meaning
`ALTER DATABASE … SET CHANGE_TRACKING = ON` + `ALTER TABLE … ENABLE CHANGE_TRACKING`. Lighter than CDC — it records **which rows / which columns changed** and a version number, but **not the changed values** (no historical data). All editions, much lower overhead, but still **not declarative** — it's an operational `ALTER`, outside the dacpac model. This is a script.

## The named trap
**Confusing it with CDC** — developers say "track changes" for both. Change tracking gives "row 42 changed" (sync-oriented); CDC gives "row 42 went from X to Y" (a full change feed). If the developer needs the old values, change tracking is the wrong tool; reaching for CDC when change tracking would do takes on the whole CDC tax for nothing. The intent-naming discipline is owned by `../../_index/cdc/SKILL.md` (change-tracking is its lighter sibling); do not re-derive the CDC weight here.

## How it flips (the specifics only)
- enable change tracking → **M4 Script-Only**, single-PR, Tier 2.
- retention/cleanup configuration → operational, lives in DB settings, flag as job-owned.
- it does **not** carry CDC's +1-tier standing consequence — that is the whole point of preferring it when values aren't needed.

## Prove it
Confirm the enable is an operational `ALTER` the dacpac does not own (declarative attempt produces nothing), run it on the isolated DB, and prove `CHANGETABLE(CHANGES …)` reports changed row keys but **not** old values — so the developer sees the boundary between change tracking and CDC concretely. Isolation still applies (see `../../_index/cdc/SKILL.md`, `talk-to-local-sql`). On the sample, `dbo.CdcCandidate` is the target (AUD-06).

## Verdict to the developer
"If you only need to know *which* rows changed for a sync — not what they changed from — change tracking is the light option: all editions, far less overhead, none of CDC's standing tax. It's still a script, not a schema edit. If you actually need the old values, that's CDC, and that's the heavier road."

## Teach it (the graduation)
Match the mechanism's weight to the requirement — "do you need the old values, or only which keys moved?" is the discriminator, and the cheaper answer is usually enough; the fail mode avoided is over-buying CDC (and its +1-forever tax) for a mobile sync. Full WHY (name your intent, pick the lightest tool): `../../_index/cdc/SKILL.md`.
