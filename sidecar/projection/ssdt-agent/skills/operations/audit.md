# Operations — Audit & history (FAMILY INDEX)

> **This file is now an INDEX.** The op specifics live in the per-op skills under `../op/`; the
> shared reasoning lives in `../_index/`. Nothing here restates a guard or how an op flips.

**Family framing.** These add a record of the past to an entity: audit columns that stamp who
created or last touched a row, and system-versioned **temporal** tables that keep every prior
version of a row for point-in-time history. Audit columns are a plain additive change (a `DEFAULT`
of `SYSUTCDATETIME()` / `SYSTEM_USER`); temporal is a design commitment — a paired history table that
grows with every write. Translate back for the developer — audit columns are the "Created On / Created
By" stamps; temporal is "show me what this row looked like last Tuesday." A row-level *change feed*
for a downstream consumer is a different mechanism, handled outside this agent — settle which one the
developer means at intake.

## Ops in this family

| Op | Per-op skill | What it is / how it flips |
|---|---|---|
| audit-columns | `../op/audit-columns/SKILL.md` | add CreatedAt/CreatedBy/UpdatedAt columns; additive with a `DEFAULT` for new rows; on a populated table the backfill is a post-deploy step |
| temporal-new | `../op/temporal-new/SKILL.md` | system versioning on a NEW entity; a single in-place schema change — the system-versioned CREATE publishes clean |
| temporal-convert | `../op/temporal-convert/SKILL.md` | convert an EXISTING table to temporal; the period columns need sensible historical defaults and the change stages across releases |

## Shared concerns for this family
- **`../_index/tightening-class/SKILL.md`** — the row-presence guard behind an audit-column backfill on a
  populated table (the same guard as make-mandatory / narrow).
- **`../_index/multi-phase/SKILL.md`** — additive→cutover→subtractive coexistence, for the
  temporal-convert backfill-then-enable sequence across releases.

> Handbook: the Audit and Temporal reference (Operation-Reference 16.8). Cite by filename.

## Connector points
- The hand-authored `proving-ground/SampleCatalog` can be replaced by the F# engine's emitter output
  from a real catalog (see `CONNECTORS.md`) — the audit and temporal ops are unchanged; only the
  source schema becomes real.
