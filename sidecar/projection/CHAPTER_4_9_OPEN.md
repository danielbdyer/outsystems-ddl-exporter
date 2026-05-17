# Chapter 4.9 open — Big-batch forward-signal close-out + WithDiagnostics extensions

**Sessions:** opens with this document. **Posture:** the last in-scope chapter before pivoting to the live SQL slice (Phase 8 / chapter 5+ OSSYS catalog producer carbon-copy). Ships six forward-signal items + close.

---

## Why this chapter

After chapter 4.8, the chapter 4.6 close shortlist's remaining items broke into two cost classes: items cheaply unlocked by chapter 4.7's IRBuilders sweep + 4.8's Attribute sweep precedent (OriginalName / ExternalDatabaseType; on-disk Index fields proved easy), and items still moderate-to-high cost (IRBuilders Kind/Module/Catalog sweep; IndexColumnDirection record-modification; Module.ExtendedProperties multi-level emitter; etc.). This chapter ships the moderate-cost batch in one coherent close-out before pivoting to the live SQL slice.

Six items:

1. **IRBuilders sweep continuation** (Kind / Module / Catalog) — needs indentation-preserving Python pass. ~150 literal sites; unlocks cheap future Kind/Module/Catalog field additions.
2. **OriginalName + ExternalDatabaseType** — Attribute additive fields; cheap via chapter 4.8 slice α precedent.
3. **IndexColumnDirection** — record-modification of `Index.Columns : SsKey list` → `IndexColumn list`. Touches Index literal sites + emitter + ScriptDom ColumnWithSortOrder.
4. **IncludePlatformAutoIndexes Composer wiring** — chapter 4.8 slice γ deferred the Pipeline integration; this slice wires the filter into `Compose.project`.
5. **Module.ExtendedProperties emission** — multi-level-aware emitter refactor for SCHEMA-level `sp_addextendedproperty`.
6. **WithDiagnostics extensions** — Diagnostics-bearing canonical signatures for `buildCreateTable` / `buildSetExtendedProperty` / `buildMergeStatement` / `buildUpdateStatement`. Pattern established for future Diagnostics sources.

After chapter 4.9 closes: **Phase 8 — OSSYS catalog producer carbon-copy** (live SQL slice).

---

## Slice arc

| # | Slice | Goal | Scope |
|---|---|---|---|
| α | IRBuilders sweep continuation (Kind/Module/Catalog) | ~150 literals migrated via indentation-preserving Python pass | ~500 test-file touches |
| β | `Attribute.OriginalName` + `Attribute.ExternalDatabaseType` IR + adapter pickup | Retires 2 of 4 A.0' deferred concepts | ~100 src + ~60 test |
| γ | `IndexColumn` DU + `IndexColumnDirection` DU + Index.Columns reshape + ScriptDom emission | Cutover-fidelity for DESC indexes | ~200 src + ~100 test |
| δ | `EmissionPolicy.filterPlatformAutoIndexes` wired into `Compose.project` | Pipeline-level toggle | ~30 src + ~30 test |
| ε | Multi-level `buildSetExtendedProperty` + module-level emission | V1 parity on `@level0type = N'SCHEMA'` extprops | ~150 src + ~80 test |
| ζ | Diagnostics-bearing canonical signatures for 4 ScriptDomBuild builders | Pattern established | ~120 src + ~60 test |
| η | V1 differential + chapter close ritual | 8-item ritual | close ritual |

---

## Open questions resolved at chapter open

**Q1 — `IndexColumn` shape.** `IndexColumn = { Attribute: SsKey; Direction: IndexColumnDirection }`. `IndexColumnDirection = Ascending | Descending`. Closed DU.

**Q2 — Migration for existing Index.Columns sites.** `IRBuilders.mkIndexColumn ssKey direction` helper + `IRBuilders.mkIndexColumns (keys: SsKey list)` shorthand for all-Ascending columns.

**Q3 — Module.ExtendedProperties emission semantics.** Emit per distinct schema in the Module's kinds; `EXECUTE sp_addextendedproperty @name=..., @value=..., @level0type=N'SCHEMA', @level0name=N'<schema>'`.

**Q4 — WithDiagnostics for builders with no Diagnostics source.** Ship the canonical signature returning `Diagnostics.ofValue stmt`; future Diagnostics sources absorb without per-call-site changes.

**Q5 — IRBuilders sweep indentation-preserving pass.** Rewrite preserves original literal's opening-brace column for closing-brace placement; overrides emit on new lines at original-indent + 4 spaces.

---

## AXIOMS amendment scan

No new axiom candidate. Chapter operates within A18 amended + T1 + A39 + A40.

---

## Closing

Chapter 4.9 is the last in-scope before live SQL. After it closes, Phase 8 / OSSYS catalog producer carbon-copy opens.

Slice α opens.
