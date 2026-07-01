# Operations — Views & synonyms (FAMILY INDEX)

> **This file is now an INDEX.** The op specifics live in the per-op skills under `../op/`; the
> shared reasoning lives in `../_index/`. Nothing here restates a guard or a flip mechanism.
> The AUTHORED-HERE backward-compat-view recipe (§17.8) has moved its full body into
> `../op/compat-view/SKILL.md` with the AUTHORED-HERE notice preserved verbatim.

**Family framing.** Views, synonyms, and their materialized cousins are the *indirection layer*.
Most are benign **Pure Declarative** changes — a view is a saved SELECT with no data to lose. The
danger here is rarely data loss; it is **stale shape** (a `SELECT *` view that silently drifts as
its base changes), the **backward-compat view** that keeps an old entity name readable through a
rename/split, and the **binding tax** of materialization. Name each view in terms of the entity it
stands in for (an OutSystems "External Entity" / "Advanced Query" is often a view underneath).

## Ops in this family

| Op | Per-op skill | What it is / how it flips |
|---|---|---|
| create-view | `../op/create-view/SKILL.md` | a saved SELECT; M1/Tier1; **enumerate columns** — `SELECT *` is a latent defect (the SELECT-* trap) |
| compat-view | `../op/compat-view/SKILL.md` | a view bearing the OLD name after a rename/split; M1 view inside a multi-PR program, Tier3; temporary, enumerated *(AUTHORED-HERE §17.8)* |
| synonym | `../op/synonym/SKILL.md` | a runtime-resolved alias to an external object; M1/Tier1 (Tier3 external); target NOT validated at publish |
| indexed-view | `../op/indexed-view/SKILL.md` | SCHEMABINDING + unique clustered index; stores data, binds base columns; flips to Multi-Phase as the base grows |

## Shared concerns for this family
- **`../_index/identity-and-refactorlog/SKILL.md`** — the refactorlog `sp_rename` that makes a
  rename safe *inside* the model, and why its reach stops at the model boundary (the reason
  compat-view exists).
- **The SELECT-\* trap is NOT lifted to an index** — it recurs only in create-view and compat-view,
  below the N≥3 bar; it lives inline in those two per-op skills.
- **The shadow-table-rebuild mechanic** (which indexed-view brushes against) is owned by
  `../op/identity-swap/SKILL.md`; indexed-view cross-references it rather than restating it.

> Handbook offset reminder (+3): file `14` = §17 (`§17.8` compat-view-AUTHORED), `16` = §19
> (anti-patterns, incl. the SELECT-\* View). Cite by filename.

## Connector points
- The §17.8 backward-compat-view recipe (now in `../op/compat-view/`) is a prime candidate to push
  **back into the handbook**; flag it in any review packet that ships a rename-with-compat-view.
- The hand-authored `proving-ground` views can come from the F# engine's `SqlprojEmitter` output
  against a real catalog (see `CONNECTORS.md`) — the view/synonym proving loop is unchanged.
