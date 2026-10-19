# Chapter 4.5 close — Index IR fidelity (Filter + IncludedColumns) + chapter-4.4 predicate cash-outs

**Sessions:** chapter 4.5 opened + slices α + β shipped in one session arc (2026-05-17). Branch `claude/review-chapter-close-Rqo0x`. Slice commits: open `f7d5d8a` → α `59c19d8` → β `b9dc072` → γ (this commit).

This document discharges chapter 4.5's eight-item close ritual. Three of the four `chapter 4.4 fills` PredicateName always-false variants retire (`HasFilteredIndex` at α; `HasIncludedIndexColumns` at β). Only the `HasLogicalForeignKey×DbConstraint` pair remains, deferred until Tightening-decision-into-Reference flow lands.

---

## Why this close

Chapter 4.4 shipped `PredicateName` with 16 variants mirroring V1's `SsdtPredicateNames`; four variants emitted false unconditionally pending V2 IR refinement. Chapter 4.5 retired the two structural-index ones by lifting V2's `Index` IR under empirical pressure from V1's reference shapes (`Osm.Domain.Model.IndexOnDiskMetadata.FilterDefinition` + `IndexColumnModel.IsIncluded`). Both lifts are additive record-extensions; F# field-missing errors flagged the literal-construction sites for mechanical migration.

V2 now emits `CREATE INDEX … INCLUDE (...)` and `CREATE INDEX … WHERE …` statements matching V1's emission surface for filtered + covering indexes. The OSSYS adapter no longer drops `isIncluded=true` columns at the boundary — the documented divergence is retired.

---

## What shipped (slice arc α + β)

### Slice α — `Index.Filter` + adapter pickup + WHERE emission + HasFilteredIndex (`59c19d8`)

- **`Index.Filter : string option`** IR field added. Mirrors V1's `IndexOnDiskMetadata.FilterDefinition` — raw filter-definition string preserved through to emit time. 13 Index literal sites migrated + `IRBuilders.mkIndex` defaults `Filter = None`.
- **`IndexDef.Filter`** at the realization layer (Statement.fs).
- **`ScriptDomBuild.parseFilterPredicate`**: parses raw filter via `TSql160Parser.ParseBooleanExpression` at emit time per chapter open Q1. Wraps result in `BooleanParenthesisExpression` for output readability (V1 IndexScriptBuilder.cs convention). Lifted to TSql160Parser from V1's TSql150Parser per supreme operating discipline pillar 4 + Sql160ScriptGenerator precedent.
- **`ScriptDomBuild.buildCreateIndex`** extended: emits `WHERE <predicate>` when Filter is Some; silent-skip on parse failure per chapter open Q3.
- **`CatalogReader.parseIndex`** captures V1 JSON `filterDefinition`; defaults to None for absent/whitespace.
- **`PredicateName.evaluate HasFilteredIndex`**: lifted to `k.Indexes |> List.exists (Option.isSome << _.Filter)`.
- 9 new tests across `IndexFilterTests.fs` (adapter pickup + None defaults + whitespace normalization; emission of FilterPredicate + silent-skip on malformed; T1 determinism; E2E rendered SQL contains WHERE) + `ManifestPredicateCoverageTests.fs` (HasFilteredIndex predicate lifts to real).

### Slice β — `Index.IncludedColumns` + adapter pickup + INCLUDE emission + HasIncludedIndexColumns (`b9dc072`)

- **`Index.IncludedColumns : SsKey list`** IR field added (additive sibling to `Columns`). Mirrors V1's `IndexColumnModel.IsIncluded` axis.
- **`IndexDef.IncludedColumns : string list`** at realization layer.
- **`CatalogReader.parseIndex`** partitions V1 JSON `columns[]` into key columns + included columns based on `isIncluded` flag. Preserves V1 ordinal ordering within each partition. Pre-slice-β the adapter dropped `isIncluded=true` entries per the documented ADMIRE divergence; **slice β retires that drop.**
- **`SsdtDdlEmitter`** resolves `idx.IncludedColumns` SsKeys to column names and populates `IndexDef.IncludedColumns`.
- **`ScriptDomBuild.buildCreateIndex`** emits INCLUDE columns when non-empty. `CreateIndexStatement.IncludeColumns` is `IList<ColumnReferenceExpression>` (bare column refs; no sort order per SQL Server INCLUDE semantic).
- **`PredicateName.evaluate HasIncludedIndexColumns`**: lifted to `k.Indexes |> List.exists (not << List.isEmpty << _.IncludedColumns)`.
- **`OsmCatalogReaderDifferentialTests` IX_USER_NAME expectation updated**: `EmailLower` attribute (isIncluded: true) now flows into `IncludedColumns` instead of being dropped at the boundary.
- 8 new tests across `IndexIncludedColumnsTests.fs` (adapter pickup; emission; E2E SQL contains INCLUDE; combined INCLUDE + WHERE; predicate cash-out).

---

## Eight-item chapter-close ritual

### 1. Active deferrals scan

| Deferral | Status |
|---|---|
| **`HasFilteredIndex` always-false predicate** | ✅ **Retired** at slice α — Index.Filter IR lifted; predicate consults real evidence. |
| **`HasIncludedIndexColumns` always-false predicate** | ✅ **Retired** at slice β — Index.IncludedColumns IR lifted; predicate consults real evidence. |
| **`HasLogicalForeignKey×DbConstraint` predicate pair** | Untriggered — requires Tightening-decision-into-Reference flow (separate chapter). |
| **OSSYS adapter `isIncluded=true` drop** | ✅ **Retired** at slice β — adapter now captures included columns. |
| **`IndexColumnDirection`** (ASC/DESC per column) | Untriggered (record-modification rather than additive; out of chapter 4.5 scope). |
| **`Index.IsPlatformAuto`** | Untriggered. |
| **`IndexOnDiskMetadata` rich fields** (FillFactor / IsPadded / etc.) | Untriggered. |
| **PreRemediation field population** | Untriggered (chapter 5+ RemediationEmitter per V2_DRIVER §154). |
| **Module.ExtendedProperties emission** | Untriggered (V1-confirmation gated). |
| **Sequence emission** | Untriggered (V1 fixture gated). |
| **Other deferred-with-trigger** items | Untriggered. |

One new deferral codified: **Filter-parse-failure Diagnostic emission** — chapter 4.5 silent-skips on parse failure; the Diagnostic-emission pathway is deferred until a real-cutover fixture surfaces a malformed filter.

### 2. Contract-vs-implementation walk

Chapter 4.5 open §1 named the contract: "V2 emits filtered indexes + INCLUDE-bearing indexes matching V1 emission fidelity; manifest predicates lift to real evaluation." Every contract clause is implemented:

- **Index.Filter** IR carriage + adapter pickup + ScriptDom WHERE emission + HasFilteredIndex evaluation real.
- **Index.IncludedColumns** IR carriage + adapter pickup + ScriptDom INCLUDE emission + HasIncludedIndexColumns evaluation real.
- **V1 differential** asserted via existing IndexFilter + IndexIncludedColumns tests + the OsmCatalogReader differential's `EmailLower` capture round-trip.

Contract = implementation across the slice arc.

### 3. CLAUDE.md staleness check

Operating-disciplines table current. No new disciplines warrant addition — chapter operates within pillar 1 / pillar 7 / pillar 8 / closed-DU expansion / structural-commitment-via-construction-validation.

### 4. README.md staleness check

Test baseline updates from 1313 to **1330 non-canary** (+17 across the chapter — 9 slice α + 8 slice β). README's "Status at chapter 4.4 close" section adds a sibling "Status at chapter 4.5 close" entry.

### 5. HANDOFF.md scope

New chapter-4.5 close prologue at this commit. Names load-bearing (Index.Filter / Index.IncludedColumns IR axes; ScriptDom WHERE + INCLUDE emission paths; parseFilterPredicate via TSql160Parser) + retained forward signals (HasLogicalForeignKey×DbConstraint pair; IndexColumnDirection; on-disk rich metadata; filter-parse Diagnostic emission).

### 6. Fresh-eye walk (cross-document drift)

- `V2_DRIVER.md` — chapter 4.5 not previously listed; folded as a sibling Phase-2-extension entry in the Executive backlog table at this close (not strictly required since 4.5 is structural-completion work, not a phase reframe).
- `BACKLOG.md` — adds Phase 5.6 section for chapter 4.5 (Index IR fidelity).
- Chapter 4.4 close doc's "PredicateName 4 always-false variants" forward signal: now 2 retired + 2 remain.

### 7. V1-input-envelope walk

V1's reference shapes walked at chapter 4.5 open:

- `src/Osm.Domain/Model/IndexModel.cs:6-72` — V1 IndexModel record (IndexName, IsUnique, IsPrimary, IsPlatformAuto, ImmutableArray<IndexColumnModel>, IndexOnDiskMetadata, ImmutableArray<ExtendedProperty>).
- `src/Osm.Domain/Model/IndexColumnModel.cs:1-27` — V1 IndexColumnModel per-column (AttributeName, ColumnName, Ordinal, IsIncluded, IndexColumnDirection).
- `src/Osm.Domain/Model/IndexOnDiskMetadata.cs:1-64` — V1 IndexOnDiskMetadata (Kind, IsDisabled, IsPadded, FillFactor, IgnoreDuplicateKey, AllowRowLocks, AllowPageLocks, NoRecomputeStatistics, FilterDefinition, DataSpace, PartitionColumns, DataCompression).
- `src/Osm.Smo/PerTableEmission/IndexScriptBuilder.cs:131-145` — V1 ApplyIndexMetadata for FilterPredicate (the emit-side ParsePredicate precedent).
- `src/Osm.Smo/PerTableEmission/IndexScriptBuilder.cs:403-419` — V1 ParsePredicate (TSql150Parser.ParseBooleanExpression).

V2 inherits two axes (Filter + IsIncluded); the rest (Direction, IsPlatformAuto, on-disk-metadata, partition/compression) are deferred-with-trigger per the chapter open's out-of-scope list. No carbon-copy event in this chapter — V2's adapter populates from V1 JSON; V2's emitter consumes typed evidence; the implementation is V2-native.

### 8. AXIOMS.md amendment cash-out

No new amendments earned at chapter 4.5 close. The chapter operates within:

- **A18 amended** — buildCreateIndex consumes IndexDef (catalog-derived; no Policy parameter); parseFilterPredicate is a pure helper.
- **T1** — byte-determinism: ScriptDom emission of WHERE + INCLUDE clauses is deterministic per ScriptDom contract; TSql160Parser parse + render round-trip is also deterministic.
- **A39** — no new smart-constructor invariants needed; Filter and IncludedColumns are both non-invariant-bearing (any string option is valid; any SsKey list is valid).
- **A40** — buildCreateIndex parameterizes over Filter + IncludedColumns extensions cleanly.

Per the chapter open's AXIOMS amendment scan, no placeholder was scheduled.

---

## Test count

- **1330 non-canary tests passing** (was 1313 at chapter 4.4 close; +17 across this chapter — 9 slice α + 8 slice β).
- **~16 Docker-dependent canary tests** (skip-if-no-Docker).
- **Lint clean** across 27 rules.
- **Build clean** under `TreatWarningsAsErrors=true` everywhere.

---

## What's load-bearing going forward

- **`TSql160Parser.ParseBooleanExpression` as the canonical filter-parse primitive** — when a future emitter needs to parse a SQL boolean expression at emit time (e.g., CHECK constraint emission via DACPAC adapter, partial-index rewriting), reach for `parseFilterPredicate` or extract its body to a shared helper at the second consumer.
- **Adapter-side `isIncluded` partition** — when V2 grows additional per-column-axis IR fields (Direction in a future chapter), the same partition-by-flag approach scales.
- **Closed-DU empirical-test discipline holds at slice scope** — record-extension propagation surfaces only at literal-construction sites; semantic-interpretation sites unaffected. Pattern confirmed at chapter A.0' (XXXXL) + chapter 4.4 (slice α) + chapter 4.5 (slices α + β).

---

## What's deferred (with explicit triggers)

### `HasLogicalForeignKey×DbConstraint` predicate pair

V2's `Reference` doesn't carry a logical-vs-physical distinction; the Tightening-pass `ForeignKeyOutcome.{EnforceFk, DoNotEnforce}` decision lives in lineage, not in Reference. Trigger to cash out: a future chapter that flows the tightening decision into Reference (or a parallel field). Then both predicates lift to real evaluation in `PredicateName.evaluate`.

### `IndexColumnDirection` (ASC/DESC per column)

V1 carries Direction per IndexColumnModel; V2's flat `Columns : SsKey list` doesn't. Lifting would require restructuring to `Columns : IndexColumn list` (per-column record), which is record-modification (not record-extension). Trigger to cash out: emission demands per-column sort direction (likely DACPAC adapter or canary fixture surfaces a non-ASC index).

### `Index.IsPlatformAuto`

Adapter-derivable from V1 but no V2 consumer demands it. Trigger: V1 fixture surfaces an emit-relevant case where platform-auto indexes need distinguishing.

### On-disk rich metadata (FillFactor / IsPadded / etc.)

V1 carries; V2 emission doesn't need for V2-driver correctness. Trigger: storage/perf tuning becomes V2-cutover-relevant.

### Filter-parse-failure Diagnostic emission

V2 silently skips filter on parse failure; the Diagnostic-emission pathway is deferred until a real fixture surfaces a parse failure. Per chapter open Q3.

---

## What this close enables

- **Cutover-fidelity emission** for filtered indexes + covering indexes at the SSDT layer. Indexes that V1 emits with `WHERE` + `INCLUDE` clauses now have V2 parity.
- **Manifest `PredicateCoverage` tightens** — kinds carrying filtered or INCLUDE-bearing indexes flag `HasFilteredIndex` / `HasIncludedIndexColumns = true` per `PredicateCounts`.
- **PhysicalSchema's `IndexesUnreflected` Tolerance variant** is one structural step closer to retirement — V2's emit surface gained the index axes V1 carries. The variant retires when PhysicalSchema's diff surface gains non-PK index reflection (separate forward signal).

---

## Closing

Chapter 4.5 is **structural-fidelity emission work** — V2's Index IR + emission gained parity with V1's IndexModel for two structural axes (Filter + IsIncluded). The chapter cashed out two of chapter 4.4's four always-false `PredicateName` forward signals.

Per V2_DRIVER's per-axis correctness stakes, this is **Schema-axis work** (High stakes; structural cutover-fidelity weight). The chapter's slice scope was correspondingly contained (~17 new tests; ~280 LOC src across α + β).

— Chapter 4.5 closed (2026-05-17).
