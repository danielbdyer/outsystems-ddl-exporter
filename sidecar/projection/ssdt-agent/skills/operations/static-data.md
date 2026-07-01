# Operations — Static Data (FAMILY INDEX)

> **This file is now an INDEX.** The op specifics live in the per-op skills under `../op/`; the
> shared reasoning lives in `../_index/`. Nothing here restates a guard or a flip mechanism.

**Family framing.** OutSystems "Static Entities" — the small, enumerated tables (Status, Country,
OrderType) whose rows are part of the model, not user input. In SSDT their rows ride in a
**post-deployment idempotent MERGE**: the schema project describes structure, seed *data* rides in
the deploy's data slot. The family's shared character is **silence-as-proof** — a no-op redeploy
must touch zero rows, keep an identical hash, and (if CDC-tracked) capture zero changes.

## Ops in this family

| Op | Per-op skill | What it is / how it flips |
|---|---|---|
| create-static-seed | `../op/create-static-seed/SKILL.md` | new lookup entity + idempotent MERGE seed; M2/Tier1, +1 if CDC-tracked, FK-target ⇒ parents-first |
| edit-seed | `../op/edit-seed/SKILL.md` | add/amend seed rows; M2/Tier1; label change must touch ONE row |
| extract-to-lookup | `../op/extract-to-lookup/SKILL.md` | promote a free-text column to a lookup + FK; M5 multi-PR/Tier3; prove total mapping before drop |
| delete-seed-value | `../op/delete-seed-value/SKILL.md` | retire a lookup value; **deactivate (`IsActive=0`), don't delete**; refuse hard DELETE if referenced |

## Shared concerns for this family
- **`../_index/idempotent-seed/SKILL.md`** — the guarded null-safe MERGE, explicit-IDs-not-IDENTITY,
  deactivate-don't-delete, and silence-is-the-proof. (Owns every seed discipline in this family.)
- **`../_index/cdc/SKILL.md`** — the CDC-silence / over-capture face when a seeded table (or its
  downstream consumer) is CDC-tracked.
- **`../_index/multi-phase/SKILL.md`** — the coexistence + total-mapping proof behind extract-to-lookup.

> Handbook offset reminder (+3): file `13` = §16, `14` = §17, `15` = §18, `16` = §19. `07` =
> Idempotency 101, `06` = Pre/Post-Deployment scripts. Cite by filename.

## Connector points
- The hand-authored `proving-ground/SampleCatalog` seed can be replaced by the F# engine's
  `PostDeployEmitter` output from a real OutSystems catalog (see `CONNECTORS.md`) — the
  idempotent-MERGE / silent-redeploy proving loop is unchanged; only the source data becomes real.
