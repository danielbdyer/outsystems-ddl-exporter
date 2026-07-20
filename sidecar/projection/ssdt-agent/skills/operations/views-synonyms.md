# Operations — Views & synonyms (FAMILY INDEX)

> **This file is now an INDEX.** The op specifics live in the per-op skills under `../op/`; the
> shared reasoning lives in `../_index/`. Nothing here restates a guard or the deploy-time specifics.
> The AUTHORED-HERE backward-compat-view recipe (§17.8) has moved its full body into
> `../op/compat-view/SKILL.md` with the AUTHORED-HERE notice preserved verbatim.

> **⚠️ Views are PRINCIPAL-ONLY for this team.** `create-view`, `compat-view`, and `indexed-view` are
> **out of the developer catalog on purpose** — the team does not author views at the outset; if the
> org adopts them, a **principal** authors them. Their skills stay as the principal's reference.
> **`synonym` is the family's developer op** — it authors no view (a cross-database pointer). Route a
> view request up to a principal; do not hand a developer a view op.

**Family framing.** Views, synonyms, and their materialized cousins are the *indirection layer*.
Most are benign changes applied in place — a view is a saved SELECT with no data to lose. The
danger here is rarely data loss; it is **stale shape** (a `SELECT *` view that silently drifts as
its base changes), the **backward-compat view** that keeps an old entity name readable through a
rename/split, and the **binding cost** of materialization. Name each view in terms of the entity it
stands in for (an OutSystems "External Entity" / "Advanced Query" is often a view underneath).

## Ops in this family

| Op | Per-op skill | What it is / how it flips |
|---|---|---|
| create-view | `../op/create-view/SKILL.md` | **PRINCIPAL-ONLY** (see banner) — a saved SELECT; ships in place, no data touched; **enumerate columns** — `SELECT *` is a latent defect (the SELECT-* trap) |
| compat-view | `../op/compat-view/SKILL.md` | **PRINCIPAL-ONLY** (see banner) — a view bearing the OLD name after a rename/split; ships in place as one step of a multi-PR program; the dependency scope reaches outside the dacpac, to consumers the model cannot see still using the old name; temporary, enumerated *(AUTHORED-HERE §17.8)* |
| synonym | `../op/synonym/SKILL.md` | **the family's developer op** — a runtime-resolved alias to an external object (authors no view); ships in place, any team member can review one inside the project — a dev lead when it points outside it; target NOT validated at publish |
| indexed-view | `../op/indexed-view/SKILL.md` | **PRINCIPAL-ONLY** (see banner) — SCHEMABINDING + unique clustered index; stores data, binds base columns; flips to a staged, multi-release change as the base grows |

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
