# Chapter 4.8 close — IRBuilders Attribute sweep + on-disk Index metadata + isPlatformAuto emitter toggle

**Sessions:** chapter 4.8 opened + slices α + β + γ shipped in one session arc (2026-05-17). Branch `claude/review-chapter-close-Rqo0x`. Slice commits: open `628e1f0` → α `c0becab` → β `3676ce7` → γ `0a0d2ea` → δ (this commit).

This document discharges chapter 4.8's eight-item close ritual. The chapter ships three orthogonal cash-outs leveraging chapter 4.7's IRBuilders sweep + sibling-wrapper discipline; one slice α scope reduction (Kind / Module / Catalog sweeps deferred due to F# offside-rule failures with the current Python pass).

---

## Why this close

The chapter 4.6 close shortlist named 10 remaining forward-signal items. Re-evaluation at chapter 4.8 open identified three where the post-4.7 cost dropped meaningfully OR the operational value justified execution now. Each shipped as its own slice + tests + adapter scaffolding.

The Attribute sweep validates the chapter 4.7 "85%+ cost reduction" claim on a second IR type (108 literal migrations); the on-disk Index metadata bundle demonstrates the post-sweep ~6-site cost for 5 new fields; the isPlatformAuto emitter toggle wires up the IR field that's been sitting unused since chapter 4.6 slice β.

---

## What shipped (slice arc α + β + γ)

### Slice α — IRBuilders Attribute sweep (`c0becab`)

- **108 Attribute literals** across 21 test files migrated via Python pass to `{ IRBuilders.mkAttribute ssKey name ptype with Column = <c>; <non-default-fields> }` syntax.
- Column field treated as always-override (mkAttribute's Column default is name-dependent; real fixtures use V1 physical names like `"ID"` while default would compute `"Id"` from Name).
- -431 net LOC (literals collapsed via builder shorthand).
- Future Attribute field additions touch ~2 sites (IRBuilders.mkAttribute default + per-test override) instead of ~108 literal sites.

### Slice α scope reduction — Kind / Module / Catalog sweeps deferred

- Initial 3-pass attempt (54 Kind + 44 Module + 64 Catalog literals) triggered F# offside-rule failures. Long single-line replacements pushed subsequent tokens past their expected indentation column; 100 build errors. Reverted.
- Pattern: the Python pass works robustly on smaller types (Index 9 fields, Reference 7 fields, Attribute 17 fields with Column-always-override) where the collapsed single-line replacement stays compact enough to not break offside-rule context. Larger types with deeper nesting (Kind containing Attribute/Reference/Index lists; Module containing Kind list; Catalog containing Module list) need an indentation-preserving Python pass to handle multi-line surrounding context correctly.
- **Deferred-with-trigger codified at this close.** Trigger: agent willing to invest time-budget for an indentation-preserving Python pass + verify. Leverage if cashed: cheap future Kind / Module / Catalog field additions.

### Slice β — On-disk Index metadata bundle (`3676ce7`)

- 5 additive Index IR fields: `FillFactor : int option`, `IsPadded : bool`, `AllowRowLocks : bool`, `AllowPageLocks : bool`, `NoRecomputeStatistics : bool`. All five mirror V1's `IndexOnDiskMetadata` fields exactly.
- `IRBuilders.mkIndex` defaults updated with V1's `IndexOnDiskMetadata.Empty` values (FillFactor=None, IsPadded=false, AllowRowLocks=true, AllowPageLocks=true, NoRecomputeStatistics=false).
- 4 non-IRBuilders test Index literals + 5 IndexDef literal sites updated via Python pass.
- `CatalogReader.parseIndex` defaults to V1 values (V1's JSON projection does not currently surface IndexOnDiskMetadata fields; future DACPAC adapter or rowset slice surfaces per V1-fixture pressure).
- `SsdtDdlEmitter` populates IndexDef from Index; `ScriptDomBuild.buildCreateIndex` emits typed ScriptDom `IndexOptions` (IndexStateOption for ON/OFF; IndexExpressionOption for FILLFACTOR) only when each field deviates from V1 default. SQL Server's CREATE INDEX omits the WITH (…) clause when all defaults hold.
- 8 new tests in `IndexOnDiskMetadataTests.fs`.
- **Validates the 85%+ cost-reduction claim**: 5 new Index fields shipped at ~6 touches (IRBuilders default + 4 non-migrated literal sites + IndexDef literal sites + emitter logic). Pre-sweep this would have been ~30+ literal sites × 5 fields = 150+ touches.

### Slice γ — EmissionPolicy.IncludePlatformAutoIndexes + filter-Catalog projection (`0a0d2ea`)

- `EmissionPolicy.IncludePlatformAutoIndexes : bool` field (default true; V1 parity with `SsdtManifestOptions.IncludePlatformAutoIndexes`). All `EmissionPolicy.create` / `empty` / `schemaOnly` / `dataOnly` / `combined` constructors default the new axis to true.
- `EmissionPolicy.withIncludePlatformAutoIndexes` setter preserving other axes.
- `EmissionPolicy.filterPlatformAutoIndexes : EmissionPolicy -> Catalog -> Catalog` Catalog-projection helper. Sibling to `SelectionPolicy.filterCatalog`. Per A18 amended: filter lives at composition layer; emitters consume the filtered Catalog (no Policy parameter).
- 5 new tests in `IsPlatformAutoEmitterToggleTests.fs`.
- Composer/Pipeline integration deferred-with-trigger — today the toggle is operationally available via `EmissionPolicy + filterPlatformAutoIndexes`; callers invoke the filter explicitly when needed. Trigger: an operator workflow that wires the toggle into the SSDT bundle composition.

---

## Eight-item chapter-close ritual

### 1. Active deferrals scan

| Deferral | Status |
|---|---|
| **On-disk rich Index metadata** (chapter 4.6 close shortlist item #3) | ✅ **Retired** at slice β — 5 fields shipped. |
| **isPlatformAuto emitter consumption** (chapter 4.6 close shortlist item #4) | ✅ **Retired** at slice γ — EmissionPolicy axis + filter shipped. |
| **Attribute IRBuilders sweep** (chapter 4.7 deferred) | ✅ **Retired** at slice α — 108 literals migrated. |
| **Kind / Module / Catalog IRBuilders sweep** | Untriggered — Python pass needs indentation-preserving rewrite. New deferral codified at this close. |
| **Composer/Pipeline wiring of IncludePlatformAutoIndexes** | Untriggered — toggle is operationally available via explicit caller invocation; pipeline-wired auto-application deferred. |
| **PreRemediation field population** | Untriggered (V2_DRIVER §154; RemediationEmitter chapter 5+). |
| **Module.ExtendedProperties emission** | Untriggered (multi-level-aware emitter refactor). |
| **Sequence emission** | Untriggered (V1 fixture gated). |
| **OSSYS catalog producer carbon-copy** | Untriggered (Phase 8 / chapter 5+). |
| **IndexColumnDirection** | Untriggered (record-modification; Attribute sweep precedent suggests the indentation-preserving Python pass would unlock). |
| **OriginalName + ExternalDatabaseType** | Untriggered (Attribute field additions; now cheap post-slice-α). |
| **Diagnostics-aware emitter signatures for other ScriptDomBuild builders** | Untriggered (no Diagnostics source emerged). |
| **WithDiagnostics emitter signature lift** | Pattern established at chapter 4.7 slice β; consumes only as Diagnostics sources emerge. |

### 2. Contract-vs-implementation walk

Chapter open §1 named the contract: "three orthogonal cash-outs reducing per-item touch cost for the chapter-4.6-close shortlist's 10 remaining forward-signal items." Three of three slices shipped substantive deliverables; slice α achieved partial scope (Attribute sweep done; Kind/Module/Catalog deferred with explicit rationale).

### 3. CLAUDE.md staleness check

Operating-disciplines table current. No new disciplines warrant addition at this close.

### 4. README.md staleness check

Test baseline 1354 → **1367 non-canary** (+13 net across the chapter — slice α added 0 (semantics-preserving migration); slice β added 8; slice γ added 5).

### 5. HANDOFF.md scope

New chapter-4.8 close prologue at this commit. Names load-bearing (Attribute IRBuilders sweep precedent; on-disk Index metadata IR + emission; IncludePlatformAutoIndexes axis + filter) + retained forward signals.

### 6. Fresh-eye walk (cross-document drift)

- `V2_DRIVER.md` — chapter 4.8 not previously listed; folded as Phase-5.9 entry in BACKLOG.
- `BACKLOG.md` — adds Phase 5.9 section.

### 7. V1-input-envelope walk

V1 references walked: `src/Osm.Domain/Model/IndexOnDiskMetadata.cs:1-64` (5 fields lifted exactly); `src/Osm.Emission/SsdtManifest.cs:25-29` (SsdtManifestOptions.IncludePlatformAutoIndexes default + semantics). No carbon-copy event; mirrors at V2 layer only.

### 8. AXIOMS.md amendment cash-out

No new amendments. Chapter operates within A18 amended (emitter consumes filtered Catalog; Policy at composition layer); T1 (byte-determinism preserved); A39 (smart-constructor invariants — EmissionPolicy.create's "at least one artifact family enabled" invariant preserved through the new field).

---

## Test count

- **1367 non-canary tests passing** (was 1354 at chapter 4.7 close; +13 net across this chapter).
- **~16 Docker-dependent canary tests** (skip-if-no-Docker).
- **Lint clean** across 27 rules.
- **Build clean** under `TreatWarningsAsErrors=true` everywhere.

---

## What's load-bearing going forward

- **Attribute IRBuilders sweep precedent** — future Attribute field additions touch ~2 sites instead of ~108. `mkAttribute` defaults absorb new fields without cascading literal-site updates.
- **5 on-disk Index metadata fields with V1 parity** — V2's SSDT emit gained `FILLFACTOR` / `PAD_INDEX` / `ALLOW_ROW_LOCKS` / `ALLOW_PAGE_LOCKS` / `STATISTICS_NORECOMPUTE` option emission.
- **IncludePlatformAutoIndexes axis + filter** — operator-toggle V2 parity with V1's manifest options. Filter sits at composition layer (A18 amended preserved).
- **Slice α scope reduction documents the Python pass limitation** — Kind / Module / Catalog sweeps need indentation-preserving rewrite; named failure mode codified.

---

## What's deferred (with explicit triggers)

### Kind / Module / Catalog IRBuilders sweep

The chapter 4.7 / 4.8 Python pass produces collapsed single-line replacements; F# offside-rule context-sensitivity means long replacements break subsequent code's indentation. Trigger to cash out: agent willing to write an indentation-preserving variant (multi-line output with overrides on their own lines matching the original literal's indentation; round-trip-test against a representative file before mass application). Leverage if cashed: ~150 literal sites migrated; cheap future Kind/Module/Catalog field additions.

### Composer/Pipeline wiring of `IncludePlatformAutoIndexes`

Slice γ ships the toggle + filter primitive but doesn't wire it into the SSDT bundle composition. Trigger: an operator workflow that demands platform-auto-index filtering applied automatically at Pipeline.run / Compose.project. Wiring is straightforward (~10 LOC; call `EmissionPolicy.filterPlatformAutoIndexes policy.Emission catalog` before invoking SsdtDdlEmitter); deferred per IR-grows-under-evidence.

### Remaining chapter-4.6-close shortlist items (unchanged-cost)

- PreRemediation / RemediationEmitter (V2_DRIVER §154 chapter 5+)
- Module.ExtendedProperties multi-level emitter refactor
- Sequence emission (V1-fixture-gated)
- OSSYS catalog producer carbon-copy (Phase 8)
- Phase 8 pragmatic close (cutover-window-relative)

---

## What this close enables

- **5 cutover-fidelity Index storage options** emit at V1 parity. V2's SSDT bundle now carries the same WITH (…) clauses V1's SsdtEmitter produces.
- **Operator-toggle for platform-auto indexes** — V2 can filter the SSDT bundle to exclude platform-auto-generated indexes via the EmissionPolicy axis.
- **Attribute field additions are now ~2 sites** — `OriginalName` + `ExternalDatabaseType` (chapter 4.6 close shortlist item #2) become trivial to ship when triggered.

---

## Closing

Chapter 4.8 is **leverage-realizing infrastructure work** — three orthogonal cash-outs validating the chapter 4.7 cost-reduction promise. Slice α (Attribute sweep) demonstrates the IRBuilders pattern at scale on a second IR type. Slice β (on-disk Index metadata) demonstrates the post-sweep ~6-site cost for 5 new fields. Slice γ (IncludePlatformAutoIndexes) closes the IR-shipped-but-unused gap for `Index.IsPlatformAuto`.

The slice α scope reduction (Kind/Module/Catalog sweeps deferred) is honest scoping: the encountered F# offside-rule failures need an indentation-preserving Python pass; the chapter close codifies the trigger.

Per V2_DRIVER's per-axis correctness stakes, this is **Schema-axis polish + cross-cutting infrastructure work**. Cutover-fidelity weight is real (V2's SSDT bundle gains V1 parity on storage knobs + operator toggles); per-axis stakes are lower-than-blocking.

— Chapter 4.8 closed (2026-05-17).
