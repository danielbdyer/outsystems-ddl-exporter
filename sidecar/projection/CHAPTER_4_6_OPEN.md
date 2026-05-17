# Chapter 4.6 open — Forward-signal cleanup bundle (Reference.HasDbConstraint + Index.IsPlatformAuto + filter-parse Diagnostic)

**Sessions:** opens with this document. **Posture:** retires three deferred-with-trigger items from the chapter 4.4 / 4.5 close docs in a single coherent chapter. **Predecessors:** chapter 4.4 (PredicateName closed DU); chapter 4.5 (Index IR fidelity α + β); chapter A.0' (the four deferred-out concepts).

---

## Why this chapter

After chapter 4.5 close, the chapter-4.4 always-false PredicateName variant list shrank from 4 to 2. The remaining pair (`HasLogicalForeignKey×DbConstraint`) deferred because V2's `Reference` IR didn't carry the logical-vs-physical distinction. V1's JSON projection DOES carry `hasDbConstraint` per attribute (`outsystems_model_export.sql:730` + `outsystems_metadata_rowsets.sql:767`); V2's adapter sees the JSON but doesn't read this field. **Lifting `Reference.HasDbConstraint`** is a clean additive record-extension that retires the last 2 always-false PredicateName variants.

Two siblings ride in the same chapter for forward-signal economy:

- **`Index.IsPlatformAuto`** — one of the four A.0' deferred concepts (`OriginalName`, `ExternalDatabaseType`, `IndexColumnDirection`, `IsPlatformAuto`). V1 surfaces it per index; V2 adapter reads but doesn't lift. Additive record-extension.
- **Filter-parse-failure Diagnostic emission** — chapter 4.5 slice α silent-skipped on parse failure; the chapter-open Q3 deferred the Diagnostic-emission pathway. This slice adds the Diagnostic emission so parse failures surface to the operator's audit channel.

---

## Strategic frame — eight axes named at chapter open

1. **DDD — three additive record-extensions; each maps a V1 surface to a typed V2 field.** `Reference.HasDbConstraint : bool` mirrors V1 attribute JSON `hasDbConstraint` int-flag (V1's `#FkReality` rowset HasFK column). `Index.IsPlatformAuto : bool` mirrors V1 index JSON `isPlatformAuto`. Both pillar-9 DataIntent — observed at the source, no operator opinion.

2. **FP — pure adapter + emitter consumers.** `parseReference` reads `hasDbConstraint` via `getIntFlag` (the existing V1 int-flag primitive). `parseIndex` reads `isPlatformAuto` via `getBool`. Both flow through without invariant validation (no smart-constructor wrapper needed — boolean values are non-invariant-bearing).

3. **Hardcore (no string-concatenation) — Diagnostic emission flows through the typed `Diagnostics<'a>` writer.** Chapter 4.5's `parseFilterPredicate` returns `BooleanExpression option`; the silent-skip path is unchanged at the build-CreateIndex layer. Diagnostic emission lifts to a sibling helper `tryParseFilter : string -> Diagnostics<BooleanExpression option>` consumed at higher levels. (For minimal scope, the chapter 4.6 ships the Diagnostics-writer plumbing; the emission-site wiring may stay deferred per consumer-pressure.)

4. **Streaming — bench observability per new code path.** `Bench.scope` per per-axis evaluation in `PredicateName.evaluate` (already in place via the outer `emit.manifest.predicateCoverage` scope; no new scope needed).

5. **Hexagonal — adapter populates from V1 source; Core consumes typed evidence.** Both fields' V1 source is V1 JSON; V2 adapter is the natural translation layer. Pure-Core / no-I/O preserved.

6. **Built-in obligation — existing `getIntFlag` + `getBool` primitives are the canonical V1 JSON readers.** No new BCL adoption.

7. **Aggregate-root + smart constructor — no new invariants.** Both new fields are non-invariant-bearing booleans; existing `Reference` + `Index` shapes already permit any boolean assignment.

8. **Test-fidelity — per-axis fixtures + V1 differential.** Adapter pickup tests for each new field; predicate-cash-out tests for `HasLogicalForeignKey×DbConstraint` pair; Diagnostic-emission test for filter-parse failure path.

---

## Slice arc

| # | Slice | Goal | LOC budget |
|---|---|---|---|
| α | `Reference.HasDbConstraint : bool` + adapter pickup + `HasLogicalForeignKey×DbConstraint` pair cash-out | V2 IR carries V1's `hasDbConstraint` per Reference; 2 always-false PredicateName variants retire | ~180 src + ~120 test |
| β | `Index.IsPlatformAuto : bool` + adapter pickup | V2 IR carries V1's `isPlatformAuto` per Index; one of four A.0' deferred concepts retires | ~80 src + ~60 test |
| γ | Filter-parse Diagnostic emission helper + tests | Filter-parse failures emit Diagnostic warnings via the existing `Diagnostics<'a>` writer; helper available for future consumers | ~60 src + ~60 test |
| δ | V1 differential + chapter close ritual | All four chapter-4.4 always-false PredicateName variants retired; A.0' deferred-concept count drops to 3 | ~80 test + close ritual |

**Total: ~320 LOC src + ~320 LOC tests.** Estimated 3-4 slices at session cadence.

---

## What this chapter does **not** do

- **No Module.ExtendedProperties emission.** Module-level extended-property emission needs `EXEC sys.sp_addextendedproperty @level0type=N'SCHEMA'` (no level1 args) — different shape than the existing table/column/index emission. Deferred to a chapter that ships the multi-level-aware emitter refactor.
- **No `CREATE SEQUENCE` emission.** Catalog.Sequences IR was shipped at chapter A.0' slice δ but no V1 emission reference exists at V1's Osm.Emission / Osm.Smo locations (untriggered by V1 fixture surface). Deferred until V1 fixture surfaces sequences or DACPAC adapter surfaces them.
- **No `IndexColumnDirection` lift.** Record-modification rather than additive — needs restructuring `Columns : SsKey list` → `Columns : IndexColumn list`. Out of chapter 4.6 scope.
- **No on-disk-metadata richness** (FillFactor / IsPadded / partition columns / data compression). V1 carries; V2 doesn't need for V2-driver correctness.
- **No `OriginalName` or `ExternalDatabaseType`** A.0' deferred concepts. Three remain after this chapter (IndexColumnDirection retains; `OriginalName` + `ExternalDatabaseType` untouched).

---

## Companion documents

- **V1 reference shapes:** `src/AdvancedSql/outsystems_model_export.sql:730` + `:785` (V1 SQL projects `hasDbConstraint` per attribute); `src/AdvancedSql/outsystems_metadata_rowsets.sql:767` + `:822` (rowset path); `src/Osm.Domain/Model/IndexModel.cs:13` (V1 IndexModel.IsPlatformAuto).
- **V2 surface to extend:** `Catalog.fs` Reference + Index records; `CatalogReader.fs` parseReference + parseIndex; `ManifestEmitter.fs` PredicateName.evaluate HasLogicalForeignKey×DbConstraint arms.
- **Strategic frame precedents:** `CHAPTER_4_4_OPEN.md`, `CHAPTER_4_5_OPEN.md`.

---

## Open questions resolved at chapter open

**Q1 — Default value when V1 source omits the field.** V1's SQL projects `hasDbConstraint` as `ISNULL(h.HasFK, 0)` — coerces missing to 0 (false). Decision: V2 adapter defaults to `false` when the field is absent (sentinel: an attribute without `hasDbConstraint` is "logical only" — the FK exists in the model but no DB constraint backs it). Mirrors V1's coalesce semantics.

**Q2 — Same for `isPlatformAuto`.** V1 surfaces it via JSON; V2 defaults to `false` when absent.

**Q3 — Diagnostic emission shape for filter-parse failure.** Decision: emit one `Diagnostic.Warning` per failure carrying `Source = "emitter:ssdt"`, `Code = "emit.ssdt.index.filterParseFailure"`, `Message` carrying the raw filter string + the parser's error count. The helper `tryParseFilter` returns `Diagnostics<BooleanExpression option>`; consumers compose via `Diagnostics.bind`. **Wiring into `buildCreateIndex`**: defer until a `Diagnostics`-aware emitter signature lands (today `buildCreateIndex` returns `CreateIndexStatement`, not `Diagnostics<CreateIndexStatement>`). Chapter 4.6 ships the helper + tests; the emission-site wiring waits on a consumer-pressure trigger.

**Q4 — `HasLogicalForeignKey×DbConstraint` predicate semantics.** Per V1's `SsdtPredicateNames` constant names:
   - `HasLogicalForeignKeyWithoutDbConstraint` ↔ kind has at least one Reference where `HasDbConstraint = false` (logical-only FK).
   - `HasLogicalForeignKeyWithDbConstraint` ↔ kind has at least one Reference where `HasDbConstraint = true` (DB-constraint-backed FK).
   Both predicates can be true simultaneously if the kind has a mix.

---

## AXIOMS amendment scan at chapter open

No new axiom candidate. Chapter operates within `A18 amended` (adapter populates from V1 source; no Policy parameter); `T1` (byte-determinism preserved); `A39` (no new invariants); `A40` (no new parameterization).

---

## Closing

Chapter 4.6 is a **forward-signal cleanup bundle** — three small additive IR + emission slices retiring three different deferred-with-trigger items from chapter 4.4 + 4.5. Slice α retires the last 2 always-false PredicateName variants (closes the chapter-4.4 PredicateName variant audit at all 16 V1-aligned + 16 V2-evaluable). Slice β retires one of four A.0' deferred concepts. Slice γ closes chapter 4.5's filter-parse-failure Diagnostic emission deferral.

Per V2_DRIVER's per-axis correctness stakes, this is **Schema-axis + Diagnostics-axis** structural-completion work (Lower stakes; manifest-fidelity weight).

Slice α opens.
