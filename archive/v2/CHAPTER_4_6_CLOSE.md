# Chapter 4.6 close — Forward-signal cleanup bundle (Reference.HasDbConstraint + Index.IsPlatformAuto + filter-parse Diagnostic)

**Sessions:** chapter 4.6 opened + slices α + β + γ shipped in one session arc (2026-05-17). Branch `claude/review-chapter-close-Rqo0x`. Slice commits: open `43c58fd` → α `75687e6` → β `be44e74` → γ `f2b2640` → δ (this commit).

This document discharges chapter 4.6's eight-item close ritual. Three forward signals retire: chapter 4.4 PredicateName always-false variants count drops from 2 to 0 (slice α); one of four A.0' deferred concepts retires (slice β); chapter 4.5 silent-skip Q3 deferral closes (slice γ).

---

## Why this close

After chapter 4.5 close, chapter 4.4's always-false PredicateName variant list had 2 remaining (`HasLogicalForeignKey×DbConstraint` pair). V1's JSON projection carries `reference_hasDbConstraint` per attribute (`outsystems_model_export.sql:730`); V2's adapter had not lifted the field. Slice α adds the lift; both variants retire. Sibling slices ship `Index.IsPlatformAuto` lift (β) + filter-parse Diagnostic emission helper (γ).

The chapter bundles three additive, complementary forward-signal cash-outs with low blast radius and clear V1 references.

---

## What shipped (slice arc α + β + γ)

### Slice α — `Reference.HasDbConstraint` + adapter + predicate cash-out (`75687e6`)

- **`Reference.HasDbConstraint : bool`** IR field added. Mirrors V1's `reference_hasDbConstraint` JSON projection. 24 Reference literal sites migrated via Python mechanical-edit + IRBuilders defaults; one differential test expectation tuned manually (V1 fixture sets `reference_hasDbConstraint: 1`).
- **`CatalogReader.parseReference`** (JSON path): captures `reference_hasDbConstraint` via `getIntFlag`; defaults to false when absent.
- **`CatalogReader.parseReferenceRowFor`** (rowset path): propagates `refRow.HasDbConstraint` from the existing `#FkReality` rowset HasFK column.
- **`SymmetricClosure`** pass: inverse Reference inherits HasDbConstraint from forward.
- **`ReadSide.fs`**: defaults `HasDbConstraint = true` (reads from `sys.foreign_keys` which by definition lists DB-constraint-backed references).
- **`PredicateName.evaluate HasLogicalForeignKeyWithoutDbConstraint`**: lifted to `k.References |> List.exists (fun r -> not r.HasDbConstraint)`.
- **`PredicateName.evaluate HasLogicalForeignKeyWithDbConstraint`**: lifted to `k.References |> List.exists (fun r -> r.HasDbConstraint)`.
- 10 new tests in `ReferenceHasDbConstraintTests.fs`.
- The chapter-4.4 "variants without V2 IR evidence always return false" test retired (all 16 V1-aligned PredicateName variants now evaluate against real IR).

### Slice β — `Index.IsPlatformAuto` + adapter pickup (`be44e74`)

- **`Index.IsPlatformAuto : bool`** IR field added. Mirrors V1's `IndexModel.IsPlatformAuto` (`src/Osm.Domain/Model/IndexModel.cs:13`). 9 Index literal sites migrated via Python mechanical-edit + IRBuilders defaults.
- **`CatalogReader.parseIndex`**: captures V1 JSON `isPlatformAuto` via `getBool`; defaults to false when absent.
- IR-only carriage in this slice; emitter consumption (operator-toggle for including platform-auto indexes) deferred to a future slice.
- No new dedicated test file — adapter pickup covered by existing OsmCatalogReaderDifferentialTests asserting V1↔V2 catalog equality.

### Slice γ — filter-parse Diagnostic emission helper (`f2b2640`)

- **`ScriptDomBuild.tryParseFilterWithDiagnostics : string -> Diagnostics<BooleanExpression option>`** — public helper. Empty/whitespace yields Some None with no diagnostics; valid parse yields Some expression with no diagnostics; malformed yields None with one Warning DiagnosticEntry per chapter 4.6 open Q3 shape.
- Diagnostic shape: `Source = "emitter:ssdt"`, `Code = "emit.ssdt.index.filterParseFailure"`, `Severity = Warning`, Metadata carries `raw` + `errorCount`.
- Existing private `parseFilterPredicate` (silent-skip path used by `buildCreateIndex`) preserved per chapter open Q3 — buildCreateIndex wiring waits on a Diagnostics-aware emitter signature trigger.
- 9 new tests in `FilterParseDiagnosticTests.fs` (empty/whitespace; valid; malformed with Warning; Source + Code; Metadata; SsKey = None; T1 determinism; Diagnostics.bind composability).

---

## Eight-item chapter-close ritual

### 1. Active deferrals scan

| Deferral | Status |
|---|---|
| **`HasLogicalForeignKey×DbConstraint` predicate pair** | ✅ **Retired** at slice α — Reference.HasDbConstraint lifted; both variants consult real IR. |
| **`Index.IsPlatformAuto`** A.0' deferred concept | ✅ **Retired** at slice β — IR carriage shipped; emitter consumption deferred to operator-toggle slice. |
| **Filter-parse Diagnostic emission** (chapter 4.5 open Q3) | ✅ **Retired** at slice γ — `tryParseFilterWithDiagnostics` helper ships; buildCreateIndex wiring stays silent-skip pending Diagnostics-aware emitter signature trigger. |
| **`IndexColumnDirection`** A.0' deferred concept | Untriggered (record-modification rather than additive). |
| **`OriginalName`** + **`ExternalDatabaseType`** A.0' deferred concepts | Untriggered. |
| **On-disk rich Index metadata** (FillFactor / IsPadded / etc.) | Untriggered. |
| **PreRemediation field population** | Untriggered (chapter 5+ RemediationEmitter per V2_DRIVER §154). |
| **Module.ExtendedProperties emission** | Untriggered (multi-level-aware emitter refactor gated). |
| **Sequence emission** | Untriggered (V1 fixture gated). |
| **`isPlatformAuto` emitter consumption** | NEW deferral codified at this close — when an operator workflow demands filtering out platform-auto indexes from the SSDT bundle, a sibling slice ships the toggle. |
| **Diagnostics-aware emitter signature** | NEW deferral codified at this close — wiring `tryParseFilterWithDiagnostics` into `buildCreateIndex` requires the emitter return type to grow Diagnostics. Trigger: a downstream consumer needs filter-parse failures to surface in the manifest. |

Two new deferrals codified; three retired.

### 2. Contract-vs-implementation walk

Chapter 4.6 open §1 named the contract: "three additive forward-signal cash-outs — HasLogicalForeignKey×DbConstraint pair lift; IsPlatformAuto lift; filter-parse Diagnostic helper." Every clause implemented:

- HasDbConstraint IR + adapter (JSON path + rowset path) + SymmetricClosure inheritance + ReadSide default + both predicates lifted.
- IsPlatformAuto IR + adapter JSON pickup.
- tryParseFilterWithDiagnostics helper with full Diagnostics integration + composability test.

Contract = implementation across the slice arc.

### 3. CLAUDE.md staleness check

Operating-disciplines table current. No new disciplines warrant addition.

### 4. README.md staleness check

Test baseline 1330 → **1348 non-canary** (+18 across the chapter — 10 slice α + 0 slice β + 9 slice γ; the chapter-4.4 always-false test removal nets to -1). README's "Status at chapter 4.5 close" section gains a sibling "Status at chapter 4.6 close" entry.

### 5. HANDOFF.md scope

New chapter-4.6 close prologue at this commit. Names load-bearing (Reference.HasDbConstraint axis; IsPlatformAuto axis; tryParseFilterWithDiagnostics helper) + retained forward signals (IndexColumnDirection; OriginalName + ExternalDatabaseType; on-disk rich metadata; isPlatformAuto emitter consumption; Diagnostics-aware emitter signature; PreRemediation; Module.ExtendedProperties; Sequence emission).

### 6. Fresh-eye walk (cross-document drift)

- `V2_DRIVER.md` — chapter 4.6 not previously listed; folded as a sibling Phase-5.7 entry in BACKLOG.
- `BACKLOG.md` — adds Phase 5.7 section.
- Chapter 4.4 close doc's "PredicateName 4 always-false variants" forward signal: all 4 now retired (slice α retires 2 here; chapter 4.5 retired 2).

### 7. V1-input-envelope walk

V1's reference shapes walked:
- `src/AdvancedSql/outsystems_model_export.sql:730` + `:785` (hasDbConstraint per attribute via ISNULL(h.HasFK, 0)).
- `src/AdvancedSql/outsystems_metadata_rowsets.sql:767` + `:822` (rowset path equivalent).
- `src/Osm.Domain/Model/IndexModel.cs:13` (IsPlatformAuto).
- `src/Osm.Smo/PerTableEmission/IndexScriptBuilder.cs:403-419` (ParsePredicate; the V1 silent-skip precedent + V1 BooleanParenthesisExpression wrap convention).

V2 mirrors all three at the V2 layer. No carbon-copy event.

### 8. AXIOMS.md amendment cash-out

No new amendments earned. Chapter operates within `A18 amended` (adapter populates from V1 source; no Policy parameter); `T1` (byte-determinism preserved); `A39` (no new invariants needed); `A40` (no new parameterization).

---

## Test count

- **1348 non-canary tests passing** (was 1330 at chapter 4.5 close; +18 net across this chapter — 10 slice α + 0 slice β + 9 slice γ minus 1 chapter-4.4-always-false test retired).
- **~16 Docker-dependent canary tests** (skip-if-no-Docker).
- **Lint clean** across 27 rules.
- **Build clean** under `TreatWarningsAsErrors=true` everywhere.

---

## What's load-bearing going forward

- **All 16 V1-aligned PredicateName variants evaluate against real V2 IR.** The chapter-4.4 always-false-pending-IR-refinement category is empty. Future predicate additions follow the same pattern: closed-DU widening + adapter pickup + emit-time evaluation.
- **`reference_hasDbConstraint` adapter primitive** — `getIntFlag` with COALESCE-to-false default for V1-projected boolean int-flags. Reusable for future similar V1 fields.
- **`tryParseFilterWithDiagnostics` helper** — the Diagnostics-aware parse primitive. Future emit-time parse consumers (CHECK constraint, partial-index rewriting, expression validation in DACPAC adapter) consume this surface.

---

## What's deferred (with explicit triggers)

### `isPlatformAuto` emitter consumption

V2 IR carries the flag but no emitter consumes it. Trigger: operator workflow demands filtering out platform-auto indexes from the SSDT bundle (a sibling slice ships a Policy.Emission toggle).

### Diagnostics-aware emitter signature

`tryParseFilterWithDiagnostics` is publicly available but `buildCreateIndex` consumes the silent-skip variant. Trigger: a downstream consumer needs filter-parse failures to surface in the manifest's Unsupported field or in a per-emit Diagnostics stream. The wiring expands buildCreateIndex's signature (or threads through Lineage<Diagnostics<_>>); not zero-friction at the emitter signature.

### Three remaining A.0' deferred concepts

`IndexColumnDirection` (record-modification; future slice when emission demands per-column sort direction); `OriginalName` (untriggered); `ExternalDatabaseType` (untriggered).

---

## What this close enables

- **Manifest `predicateCoverage` at full V1 parity.** All 16 V1 SsdtPredicateNames constants evaluate against real V2 IR; operators inspecting the manifest see actual flags for HasLogicalForeignKeyWithDbConstraint etc.
- **V2 IR carriage of `IsPlatformAuto`** ready for future emitter consumption (operator-toggle slice).
- **Diagnostics-aware parse primitive** ready for future emit-time consumers needing failure visibility (CHECK emission; DACPAC validation paths; etc.).

---

## Closing

Chapter 4.6 is **forward-signal cleanup work** — three independent, additive cash-outs in a single bundled chapter. The largest leverage was slice α retiring the chapter-4.4 always-false PredicateName pair; the other two are smaller IR + helper additions.

Per V2_DRIVER's per-axis correctness stakes, this is **Schema-axis + Diagnostics-axis** structural-completion work. The chapter's slice scope was correspondingly contained (~300 LOC src + ~140 LOC tests + the Python-pass migrations).

— Chapter 4.6 closed (2026-05-17).
