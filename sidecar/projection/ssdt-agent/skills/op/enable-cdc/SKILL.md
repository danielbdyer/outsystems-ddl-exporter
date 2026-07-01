---
name: enable-cdc
description: Use when the developer says "turn on Change Data Capture for Customer", "we need a change feed for the ETL", "track every insert/update/delete so the warehouse can pick it up", "enable CDC on this entity" — the CDC tripwire. SSDT destination = a post-deploy / operational SCRIPT (Mechanism 4 Script-Only) — CDC is NOT in the dacpac model.
---

# Enable CDC — THE TRIPWIRE (CDC Surprise trap)

> **Default (provisional — the data decides).** Mechanism 4 Script-Only, single-PR script, Tier 3 baseline (+1 → Tier 4 for a first-time CDC enablement on the estate). The danger drives the tier, not the release count.

## OutSystems phrasing
"turn on Change Data Capture for Customer", "we need a change feed for the ETL", "track every insert/update/delete so the warehouse can pick it up".

## SSDT meaning
`sys.sp_cdc_enable_table` (and `sp_cdc_enable_db` at the database level) — this creates `sys`-owned change tables, a capture instance, and SQL Agent capture/cleanup jobs. **None of this is in the dacpac model.** SSDT cannot CREATE it, diff it, or publish it — CDC enablement lives in a post-deploy / operational script, full stop. Never write ALTER; never try to express it declaratively.

## The named trap
**CDC Surprise** (handbook 16 = §19) with three faces: (1) it's outside the declarative model — a declarative attempt is silently ignored; (2) it's Enterprise/Standard-licensed — the wrong edition fails at *runtime*, after deploy; (3) the standing consequence — every future schema change on this table now needs capture-instance management (+1 Tier forever). This whole concern is owned by `../../_index/cdc/SKILL.md`; do not re-derive the three faces here.

## How it flips (the specifics only)
CDC barely "flips" — it is heavy from the start:
- enable CDC, no no-gap requirement → **M4 Script-Only**, single-PR script. **Tier 3** baseline, **+1 → Tier 4** for a first-time enablement on the estate.
- enable CDC on a wrong-edition target → does not flip the mechanism; it **fails at runtime** — the proving ground must catch this.
- any co-occurring schema change on a now-CDC table → see `../recreate-capture-instance/SKILL.md`; that change becomes Multi-Phase, multi-PR if no-gap is required.

## Prove it
(a) Prove attempting CDC declaratively produces **nothing** in the SSDT delta (the dacpac ignores it) — so the developer sees it must be a script. (b) Run the enable script against the throwaway DB and confirm the change tables + capture instance appear. (c) **Isolation is mandatory**: `sp_cdc_enable_db` flips instance-wide state — do CDC proving on a disposable isolated DB, **never** the shared warm container (CLAUDE.md survival rule 1; PROTOCOL §8; see `../../_index/cdc/SKILL.md` and `talk-to-local-sql`). (d) Confirm the edition supports CDC before claiming success. On the sample, `dbo.CdcCandidate` is the isolated-DB CDC target (AUD-04).

## Verdict to the developer
"Turning on CDC isn't a schema edit — SSDT can't describe it, so it lives in a script (I proved the dacpac ignores the declarative attempt). It needs Enterprise or Standard edition, and here's the part that lasts: once CDC is on this entity, every future change to it has to manage the capture instance, or the change feed silently drops records. That's why I'm rating this higher than the one-line script makes it look."

## Teach it (the graduation)
When a change isn't in the model, ask what *standing* obligation it imposes on all future changes, not just what it does today — CDC is +1 Tier on everything it touches forever after; the fail mode avoided is being ambushed months later when a column silently drops out of the warehouse feed. Full WHY: `../../_index/cdc/SKILL.md`.
