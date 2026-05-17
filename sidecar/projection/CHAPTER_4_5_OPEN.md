# Chapter 4.5 open — Index IR fidelity + emission (Filter + IncludedColumns) + chapter-4.4 predicate cash-outs

**Sessions:** opens with this document. **Posture:** retires 2 of the 4 always-false `PredicateName` variants from chapter 4.4 — `HasFilteredIndex` and `HasIncludedIndexColumns`. **Predecessors:** chapter A.0' (IR fidelity lifts established the additive-record-extension precedent); chapter 4.4 (PredicateName closed DU with forward-signal docstrings naming the IR refinements this chapter delivers); chapter 4.1.A close arc (ScriptDom typed-AST emission as the canonical SQL surface).

This is the chapter-open document per the strategic-frame-at-chapter-open discipline.

---

## Why this chapter

Chapter 4.4 shipped `PredicateName` with 16 variants mirroring V1's `SsdtPredicateNames`. Four variants always emit `false` pending V2 IR refinement (forward signals carried in DU docstrings):

- `HasFilteredIndex` — V2's `Index` doesn't carry a filter expression.
- `HasIncludedIndexColumns` — V2's `Index.Columns` is a flat SsKey list; no key/included split.
- `HasLogicalForeignKeyWithoutDbConstraint` — V2's `Reference` doesn't distinguish logical-only from DB-constraint-backed.
- `HasLogicalForeignKeyWithDbConstraint` — same shape.

This chapter retires the first two by lifting two IR fields under empirical pressure from V1's source shapes (`Osm.Domain.Model.IndexModel.OnDisk.FilterDefinition` + `IndexColumnModel.IsIncluded`). Both lifts are additive record-extensions — F# field-missing errors at literal-construction sites only (per the closed-DU expansion empirical-test record-extension generalization at chapter 3.2 close).

The third/fourth variant pair (Logical FK × DB-constraint) requires a different IR shape — a Tightening-pass decision flowing into Reference. Deferred to a future chapter when that integration is needed.

**Operator-facing payoff.** V2 can now emit `CREATE INDEX … INCLUDE (...)` and `CREATE INDEX … WHERE …` statements for indexes that need them, matching V1 emission fidelity. The manifest's `predicateCoverage.tables` entries gain real per-table `HasFilteredIndex` / `HasIncludedIndexColumns` flags, surfacing structural index features operators audit at the manifest layer.

**Out of scope.**

- `IndexColumnDirection` (ASC/DESC per column) — one of the four A.0' deferred-out concepts. Lifting requires restructuring `Index.Columns : SsKey list` → `Index.Columns : IndexColumn list` (per-column record with direction), which is record-modification (not record-extension) and has wider blast radius. Deferred to a future chapter; can run as a sibling slice when emission demands per-column sort direction.
- `Index.IsPlatformAuto` — another A.0' deferred concept. Adapter-derivable from V1 but no V2 consumer demands it yet (the canary surface already handles platform-auto indexes through other paths).
- `IndexOnDiskMetadata` rich fields (IsPadded / FillFactor / AllowRowLocks / DataCompression / etc.) — V1 carries them; V2's emission doesn't need them for V2-driver-mode correctness. Storage/performance tuning is out of scope.
- `HasLogicalForeignKey×DbConstraint` predicate pair — see above.

---

## Strategic frame — eight axes named at chapter open

1. **DDD — IR refinement is record-extension, not modification.** `Index.Filter : string option` adds an optional field carrying V1's `FilterDefinition` semantic. `Index.IncludedColumns : SsKey list` adds a sibling list to `Columns` (the existing field carries key columns only; the new field carries included columns). Both are additive; sets V2's IR up to mirror V1's IndexModel + IndexColumnModel split without restructuring existing literals.

2. **FP — emitter consumes additive evidence pure-functionally.** `ScriptDomBuild.buildCreateIndex` extends to render `FilterPredicate` (when `Filter = Some _`) + `IncludeColumns` (when `IncludedColumns` is non-empty). Pure function of the Index value; T1 byte-determinism preserved.

3. **Hardcore (no string-concatenation) — predicate expression flows through ScriptDom's typed AST.** V1's `FilterDefinition` is a raw SQL string at the boundary (V1 captures it from `sys.indexes.filter_definition`). V2 needs to render it back as a WHERE-clause expression. ScriptDom's `FilterPredicate` property accepts a `BooleanExpression`; the raw filter text needs to be parsed into a BooleanExpression. **Per pillar 1: parse the raw string via `TSql160Parser` at emit time**, producing a typed BooleanExpression. (V1's `IndexOnDiskMetadata.Create` already validates the filter via `Microsoft.SqlServer.TransactSql.ScriptDom`; V2 inherits.) Forward signal: if parsing-at-emit-time proves a perf concern at scale, move parsing to adapter ingestion.

4. **Streaming — Bench scope per emit path.** `Bench.scope "emit.ssdt.createIndex.filter"` records the parse + render overhead; `emit.ssdt.createIndex.included` records the INCLUDE clause assembly. Iterator-logging-as-first-class-outcome.

5. **Hexagonal — adapter populates from V1 JSON; Core consumes typed evidence.** V2's `parseIndex` extends to read `filterDefinition` (V1 JSON path) and to STOP dropping `isIncluded=true` columns (currently filtered out at the boundary per the documented divergence). The boundary becomes more faithful to V1's emission surface.

6. **Built-in obligation — `TSql160Parser` is the canonical parse primitive.** V2 already uses ScriptDom for emission; the parser is its sibling. Per pillar 7 + the text-builder-as-first-instinct discipline: use the typed parser; don't hand-roll boolean-expression construction.

7. **Aggregate-root + smart constructor — `Index.create` (if it exists) preserves invariants.** Today `Index` is a plain record with no smart constructor. The two new fields are non-invariant-bearing (any string option is valid; any SsKey list is valid). No new validation needed; record extension is purely additive.

8. **Test-fidelity — per-axis fixtures + V1 differential.** Three test surfaces:
   - **IR fidelity**: adapter populates `Index.Filter` from V1 JSON `filterDefinition`; `Index.IncludedColumns` from `isIncluded=true` entries. Per-fixture tests.
   - **Emission fidelity**: ScriptDom renders WHERE + INCLUDE clauses; parse round-trip preserves filter expression.
   - **Predicate cash-out**: `PredicateName.evaluate HasFilteredIndex` returns true iff `Index.Filter.IsSome`; same shape for `HasIncludedIndexColumns`. Existing chapter-4.4 always-false-variants test updates to assert non-trivial output on fixture catalogs.

---

## Slice arc

| # | Slice | Goal | LOC budget |
|---|---|---|---|
| α | `Index.Filter : string option` + adapter pickup + ScriptDom WHERE clause + `HasFilteredIndex` predicate cash-out | V2 emits filtered indexes; predicate lifts to real evaluation | ~180 src + ~120 test |
| β | `Index.IncludedColumns : SsKey list` + adapter pickup (stop dropping `isIncluded=true`) + ScriptDom INCLUDE clause + `HasIncludedIndexColumns` predicate cash-out | V2 emits CREATE INDEX with INCLUDE; predicate lifts | ~180 src + ~120 test |
| γ | V1 differential update + chapter-close eight-item ritual | ChapterCloseRitual: 4 always-false → 2 always-false; ToleratedDivergence may shrink (`isIncluded`-dropped divergence retires) | ~80 test + close ritual |

**Total: ~360 LOC src + ~320 LOC tests.** Estimated 3 sessions at session cadence.

---

## What this chapter does **not** do

- **No `IndexColumnDirection` lift.** Per pillar 8 + the IR-grows-under-evidence discipline: ASC/DESC per column requires `Columns : IndexColumn list` restructure. Deferred until emission demands per-column sort direction (likely DACPAC adapter or canary at a fixture that diverges).
- **No on-disk-metadata richness** (FillFactor, IsPadded, AllowRowLocks, etc.). V1 carries them but V2 emission doesn't need them for V2-driver correctness.
- **No `IsPlatformAuto` flag.** Adapter-derivable but no V2 consumer demands it.
- **No logical-vs-physical Reference distinction.** Separate chapter when Tightening-decision flow surfaces it.

---

## Companion documents

- **V1 reference shapes:** `src/Osm.Domain/Model/IndexModel.cs` (V1 record); `src/Osm.Domain/Model/IndexColumnModel.cs` (per-column with IsIncluded + Direction); `src/Osm.Domain/Model/IndexOnDiskMetadata.cs` (carries FilterDefinition).
- **V2 surface to extend:** `sidecar/projection/src/Projection.Core/Catalog.fs` `Index` record (~line 533); `sidecar/projection/src/Projection.Targets.SSDT/ScriptDomBuild.fs` `buildCreateIndex` (~line 645); `sidecar/projection/src/Projection.Adapters.Osm/CatalogReader.fs` `parseIndex` (~line 816); `sidecar/projection/src/Projection.Targets.SSDT/ManifestEmitter.fs` `PredicateName.evaluate` HasFilteredIndex / HasIncludedIndexColumns arms.
- **V1 differential precedent:** `tests/Projection.Tests/ManifestV1DifferentialTests.fs` (chapter 4.4 slice δ).
- **Strategic frame precedent:** `CHAPTER_4_4_OPEN.md`.

---

## Open questions resolved at chapter open

**Q1 — Parse filter expression at adapter or emit time?** Decision: emit time. V1 captures the filter as a raw string from `sys.indexes.filter_definition`; V2's adapter stores it as `string option` (no parse at boundary). At emit time, `ScriptDomBuild.buildCreateIndex` parses via `TSql160Parser.ParseExpression` to produce ScriptDom's `BooleanExpression`. Parse failures propagate as `EmitError` (deterministic at emission rather than ingestion; ingestion-time parsing would couple adapter to ScriptDom unnecessarily). Forward signal: if parsing perf becomes a bottleneck at canary scale (300 tables × many filtered indexes), move parsing to adapter and cache.

**Q2 — `Index.IncludedColumns` ordering.** V1's `IndexColumnModel` carries `Ordinal : int` per column; V2's `Columns : SsKey list` already preserves source order via the adapter's `List.sortBy fst` after ordinal extraction. Decision: `IncludedColumns` mirrors the same shape — `SsKey list` in source-ordinal-sorted order. Adapter applies the same sort logic.

**Q3 — Backward compatibility of `Filter` parse failures.** V1's data may carry malformed `filterDefinition` strings (defensively quoted, or with non-SQL prefix/suffix from V1 storage). Decision: parse failures emit a `Diagnostic` warning + skip the filter (emit unfiltered index); V2 doesn't fail the manifest emission on a single bad filter. Codified via lineage event on the emit path. Trigger to widen: a real-cutover fixture surfaces a failure mode (then the strategy refines).

**Q4 — `ToleratedDivergence` retirement.** Chapter 4.4 slice γ shipped Unsupported emitting `IndexesUnreflected` among 4 variants. That variant documented "non-PK indexes not reflected in PhysicalSchema's comparison surface." Chapter 4.5 doesn't retire it (the comparison surface still doesn't reflect non-PK indexes; this chapter only extends emission). The variant retires when PhysicalSchema gains index-reflection — a separate forward signal. Chapter 4.5 might add a new `ToleratedDivergence` variant: `IncludedColumnsDroppedAtAdapter` retires here (V2's adapter no longer drops `isIncluded=true` columns at slice β; if that divergence was named, it retires; if unnamed, no entry change). **Today no such variant exists**, so slice β's change is purely additive at the divergence surface.

---

## AXIOMS amendment scan at chapter open

Per the `Amendments scheduled (chapter close)` scaffolding discipline: chapter 4.5 has **no new axiom candidate**. The chapter operates within existing axioms — `A18 amended` (no Policy in emitters; new emitter paths consume Catalog + parsed-filter result, never Policy); `T1` (byte-determinism; parse-then-render of filter is deterministic per ScriptDom contract); `A39` (smart-constructor invariants — no new invariants on Index beyond existing); `A40` (harmonization-via-parameterization — `buildCreateIndex` parameterizes over `Filter` + `IncludedColumns` extensions, same algorithm shape). No amendment placeholder lands at this open.

---

## Closing

Chapter 4.5 is **structural-fidelity emission work** — V2's Index IR + emission gains parity with V1's `IndexModel` + ScriptDom emission for two structural index features (filtered indexes; included columns). The chapter cashes out two of chapter 4.4's four always-false `PredicateName` forward signals.

Per V2_DRIVER's per-axis correctness stakes table, this is **Schema-axis work** (High stakes; not the highest-leverage single deliverable but real cutover-fidelity weight). The chapter's slice scope is correspondingly contained (~360 LOC src + ~320 LOC tests across 3 slices).

Slice α opens.
