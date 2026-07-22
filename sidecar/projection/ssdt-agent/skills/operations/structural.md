# Operations — Structural reshapes (FAMILY INDEX)

> **This file is now an INDEX.** The op specifics live in the per-op skills under `../op/`; the
> shared reasoning lives in `../_index/`. Nothing here restates a guard or how an op flips.
> The AUTHORED-HERE recipe (merge-entities §17.7) has moved its full body into the per-op skill
> (`../op/merge-tables/`) with the AUTHORED-HERE notice preserved verbatim.

**Family framing.** These are the heavy ops — one entity becoming two, two becoming one, a field
crossing entities, or Auto-Number toggled. None is a single CREATE edit; most **cannot** be one
declarative destination because they move *data* between shapes while the app keeps reading. That
is why almost everything here **ships across several releases (multi-PR)** by default — so the
running application keeps working while the change is in flight — and why proving on a disposable
copy of Dev matters most: the risk is not in the DDL, it is in the gap between "old table still
has the data" and "new table now owns it." Translate back for the developer — a split is *one
entity becoming two*, a merge is *two becoming one*, "Auto Number" is `IDENTITY`.

## Ops in this family

| Op | Per-op skill | What it is / how it flips |
|---|---|---|
| split-table | `../op/split-table/SKILL.md` | one entity → two; ships across releases, multi-PR (empty source ⇒ a single in-place schema change); dropping the old columns is what BlockOnPossibleDataLoss blocks |
| merge-tables | `../op/merge-tables/SKILL.md` | two entities → one; ships across releases, multi-PR; **prove cardinality (1:1) BEFORE copy** or 1:many rows drop silently *(AUTHORED-HERE §17.7)* |
| move-attribute | `../op/move-attribute/SKILL.md` | one column crossing entities; ships across releases, multi-PR when populated; prove the join is 1:1; a move is copy-then-drop, never a rename |
| identity-swap | `../op/identity-swap/SKILL.md` | add/remove IDENTITY (Auto Number); a **table rebuild** (shadow + IDENTITY_INSERT + FK drop/recreate), not an alter |

## Shared concerns for this family
- **`../_index/multi-phase/SKILL.md`** — additive→cutover→subtractive coexistence, the totality
  proof that licenses the drop, BlockOnPossibleDataLoss as the licensing gate that blocks it.
  (Governs split, merge, move-attribute.)
- **`../_index/identity-and-refactorlog/SKILL.md`** — identity is separate from name; a cross-table
  move / a rebuild that loses the mapping re-mints keys the way a rename with no refactorlog entry
  loses a column. (Governs move-attribute and identity-swap.)

> Handbook offset reminder (+3): file `14` = §17 (`§17.3` identity, `§17.6` split, `§17.7`
> merge-AUTHORED), `16` = §19 (anti-patterns). Cite by filename.

## Connector points
- The hand-authored `proving-ground/SampleCatalog` can be replaced by the F# engine's
  `SqlprojEmitter`/`DacpacEmitter`/`PostDeployEmitter` output from a real catalog (see
  `CONNECTORS.md`) — the multi-phase copy/hash/drop loop is unchanged; only the source schema
  becomes real.
- The §17.7 authored recipe (now in `../op/merge-tables/`) is the natural first candidate to push
  **back into the handbook** once its bodies are completed.
