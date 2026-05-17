# Chapter 4.4 close — Manifest diagnostic fields retire chapter-4.4-fills deferrals

**Sessions:** chapter 4.4 opened + slices α + β + γ + δ shipped in one session (2026-05-17). Branch `claude/review-chapter-close-Rqo0x`. Slice arc commits: α `a6505e5` → β `09e71ce` → γ `b08c7dc` → δ (this commit).

This document discharges chapter 4.4's eight-item close ritual now that three of the four `chapter 4.4 fills` deferrals codified in `ManifestEmitter.fs:32-33,176-183` are retired and the V1 differential surface is operative.

---

## Why this close

Per chapter 4.4 open: `ManifestEmitter` shipped at chapter 4.1.A slice 9 with four placeholder-shaped manifest fields (Coverage / PredicateCoverage / PreRemediation / Unsupported) emitting as `null` / empty arrays under the comment "chapter 4.4 fills." This chapter retires the three reachable deferrals (Coverage / PredicateCoverage / Unsupported) using V2's existing IR + Tolerance evidence. PreRemediation stays empty per `V2_DRIVER.md` §154 (RemediationEmitter deferred to chapter 5+).

V2_DRIVER's per-axis correctness stakes table places operational-diagnostics as **Lower** stakes — this chapter ships an operator-facing surface, not a cutover-blocking property. The structural commitment is that the V2 manifest stops carrying placeholder defaults and starts carrying real evidence about per-axis coverage.

---

## What shipped (slice arc α → δ)

### Slice α — CoverageBreakdown + CoverageSummary + Coverage.compute (`a6505e5`)

- **`CoverageBreakdown`** value type mirroring V1's `Osm.Emission.CoverageBreakdown` (`SsdtManifest.cs:68-90`) — Emitted / Total / Percentage (decimal). Smart constructor `create` enforces non-negative + Emitted ≤ Total + V1's percentage-rounding contract (`Math.Round(value, 2, MidpointRounding.AwayFromZero)`; Total = 0 → 100m vacuous; Emitted = 0 → 0m).
- **`CoverageSummary`** with Tables / Columns / Constraints axes. `createComplete` mirrors V1's `SsdtCoverageSummary.CreateComplete`. Error aggregation across axes for diagnostic completeness.
- **`Coverage.compute : Catalog -> CoverageSummary`** pure function. Constraint count = PKs (one per kind with PK attrs) + non-PK unique indexes + FK references + CHECK constraints. T11 keyset coverage holds structurally — V2 emits every kind from the catalog so Emitted = Total per axis. Bench scope `emit.manifest.coverage`.
- **Manifest record extended** with `Coverage : CoverageSummary` field. `buildWith` populates; `toNode` emits as JsonObject (was `null`).
- 18 new tests in `tests/Projection.Tests/ManifestCoverageTests.fs`: smart-constructor invariants; V1-contract edge cases (0/0; 0/N; N/N); rounding (1/3 → 33.33; 2/3 → 66.67; 5/8 → 62.5); per-axis CreateComplete; error aggregation; Coverage.compute T1 determinism + sampleCatalog axis-count parity; FsCheck properties.

### Slice β — PredicateName closed DU + PredicateCoverage (`09e71ce`)

- **`PredicateName`** closed DU with 16 variants mirroring V1's `SsdtPredicateNames` constants verbatim (`SsdtPredicateCoverage.cs:7-25`). Concept-shaped per pillar 8; V1 ubiquitous language preserved.
- **`PredicateName.evaluate : PredicateName -> Kind -> bool`** exhaustive match. 12 variants have V2 IR evidence (HasTrigger, IsStaticEntity, IsExternalEntity, IsInactiveEntity, HasInactiveColumns, HasDefaultConstraint, HasCheckConstraint, HasExtendedProperties, HasUniqueIndex, HasCompositeUniqueIndex, HasLogicalForeignKey, HasTemporalHistory). 4 variants always emit false pending V2 IR refinement (HasFilteredIndex, HasIncludedIndexColumns, HasLogicalForeignKeyWithoutDbConstraint, HasLogicalForeignKeyWithDbConstraint) — forward signals in DU docstrings. Closed-DU empirical-test catches missing arms at compile time.
- **`PredicateName.all`** in canonical alphabetical order. Used at emit time for T1 byte-determinism of PredicateCounts array.
- **`PredicateCoverageEntry`** mirrors V1's shape (Module / Schema / Table / Predicates). Predicates carried as typed `PredicateName list`; rendered at JSON terminal.
- **`PredicateCoverage`** with `Tables` + `PredicateCounts : Map<PredicateName, int>`. PredicateCounts emits as sorted-by-name array of `{name, count}` objects per chapter open Q2 (T1 byte-determinism; documented divergence from V1's JSON-dict shape).
- **`PredicateCoverage.compute : Catalog -> PredicateCoverage`** pure function with per-kind iteration + count aggregation. Bench scope `emit.manifest.predicateCoverage`.
- **Manifest record extended** with `PredicateCoverage` field. Emit retires the prior `null` default.
- 14 new tests: closed-DU exhaustiveness; per-predicate determinism; always-false variants; satisfiedBy canonical sorted order; compute T1 + Tables count + PredicateCounts aggregation property; manifest emission JSON shape; full toJson T1.

### Slice γ — Unsupported field (`b08c7dc`)

- **`Unsupported.compute : unit -> string list`** pure function. Renders every empirically-known `ToleratedDivergence` variant as its V1-vocabulary discriminator name; sorted alphabetically. Bench scope `emit.manifest.unsupported`.
- **Manifest record extended** with `Unsupported : string list` field. Emit retires the prior empty-array default.
- 7 new tests in `tests/Projection.Tests/ManifestUnsupportedTests.fs`: cardinality matches `ToleratedDivergence.allKnown`; sorted order; T1 determinism; current-variant content audit (`HeaderCommentsOmitted` / `IndexesUnreflected` / `PostDeployForeignKeysSplit` / `StaticPopulationsUnreflected`); non-emptiness; manifest JSON shape (sorted array of strings); full toJson T1.

### Slice δ — V1 differential + chapter-close ritual (this commit)

- **`tests/Projection.Tests/ManifestV1DifferentialTests.fs`** (11 tests) — cross-checks V2's emitted shape against V1's reference types:
  - PredicateName names match V1's SsdtPredicateNames constants verbatim (set-equality test + cardinality).
  - CoverageBreakdown percentage rounding matches V1's `Math.Round AwayFromZero` contract on V1-witnessed cases (1/8 → 12.5; 1/40 → 2.5; 5/16 → 31.25) + edge cases (0/0 → 100m; 0/N → 0m; N/N → 100m).
  - Manifest emits V1-shape SsdtCoverageSummary three-axis structure (tables / columns / constraints each with emitted / total / percentage).
  - Manifest emits V1-shape SsdtPredicateCoverage two-section structure (tables + predicateCounts).
  - PredicateCoverageEntry carries V1's PredicateCoverageEntry fields (module / schema / table / predicates).
  - Unsupported is a JSON array of strings (V1 IReadOnlyList shape).
  - PreRemediation stays empty per V2_DRIVER §154.
  - V2-only divergences documented: `registry.digest` field (chapter A.4.7'); `predicateCoverage.predicateCounts` as sorted array of objects (V1 emits dict).

---

## Eight-item chapter-close ritual

Per `DECISIONS 2026-05-14 — Chapter-close ritual` + the V1-envelope-walk amendment (session 25).

### 1. Active deferrals scan

| Deferral | Status |
|---|---|
| **Module.ExtendedProperties emission** (chapter 4.1.A.8) | Untriggered (gated on V1 confirmation of module → schema convention) |
| **PreRemediation field population** (chapter 4.4 fills) | **Reaffirmed-as-deferred** — V2_DRIVER §154 explicitly defers RemediationEmitter to chapter 5+; the field stays empty-array structurally correct. |
| **Sequence emission** | Untriggered (V1 fixture has not surfaced sequences) |
| **PhysicalSchema extended-property reflection** | Untriggered (separate axis from emitter) |
| **V1↔V2 byte-equality for sp_addextendedproperty** | Untriggered (no consumer demands line-by-line diff with V1) |
| **Statement DU MERGE/UPDATE promotion** (chapter 4.1.B close) | Untriggered (third consumer hasn't surfaced) |
| **Sort-vs-data deferral predicate distinction** (chapter 4.1.B close) | Untriggered |
| **OSSYS adapter User-kind identification surface** (chapter 4.2 close) | Untriggered |
| **CSV adapter for ManualOverride** (chapter 4.2 close) | Untriggered |
| **Chapter 4.3 slice δ (CLI wire-up) + slice ε (V1 differential)** | Untriggered (no operator demand for one-command diagnostics emit; no chapter needs cross-version diagnostic-fidelity evidence) |
| **Chapter 3.x slices ε (modality marks → comments/extended properties) + ζ (byte-determinism via canonicalization) + per-Catalog parameterization** | Untriggered |
| **`ICatalogReader` interface lift** | Untriggered |
| **DacFx adoption in DacpacEmitter** | Retired at chapter 3.x close (dev-tooling reframe) |
| **Composition primitives `fallback` / `accumulate` / `wrap` / `lift`** | Untriggered |
| **`RequireQualifiedAccess` retrofit on KeepReason DUs** | Untriggered |
| **Strategy registry mechanism** | Retired at chapter A.4.7 close (TransformRegistry shipped) |

Three new deferrals codified at this close (see §below): **PredicateCounts JSON-shape divergence** (Tolerance candidate if V1↔V2 byte-equality demanded); **PredicateName 4-variant always-false** (HasFilteredIndex / HasIncludedIndexColumns / HasLogicalForeignKey×DbConstraint pair — IR refinement triggers); **Unsupported per-divergence rationale** (typed record list widening trigger).

### 2. Contract-vs-implementation walk

Chapter 4.4 open §1 named the contract: "V2's manifest stops emitting `this field is intentionally null` and starts carrying real evidence about per-axis coverage." Every contract clause is implemented:

- **Coverage** axis (slice α): typed value object; per-axis emit/total/percentage; T11 keyset coverage holds structurally (V2 emits every kind, so `Emitted = Total`).
- **PredicateCoverage** axis (slice β): 16-variant closed DU + per-table evaluation + cross-catalog aggregation; canonical sorted order for T1 byte-determinism.
- **Unsupported** axis (slice γ): rendered as sorted strings from `ToleratedDivergence.allKnown`.
- **PreRemediation** axis: preserved empty per V2_DRIVER §154 — RemediationEmitter chapter 5+ will populate.
- **V1 differential** (slice δ): structural correspondence test asserts the V2 emit shape mirrors V1's SsdtManifest schema for the three fields slice α/β/γ retired.

Contract = implementation across the slice arc.

### 3. CLAUDE.md staleness check

Operating-disciplines table current. No new disciplines warrant addition at this close — the chapter operates within existing pillar 1 / pillar 7 / pillar 8 / closed-DU expansion / structural-commitment-via-construction-validation disciplines.

### 4. README.md staleness check

Test baseline updates from 1262 to **1313 non-canary** (+18 slice α + 14 slice β + 8 slice γ + 11 slice δ = 51 new tests across the chapter). README's "Status at chapter A.4.7' close" section adds a sibling "Status at chapter 4.4 close" entry.

### 5. HANDOFF.md scope

New chapter 4.4 close prologue at this commit. Names load-bearing (Coverage/PredicateCoverage/Unsupported as typed values flowing through the manifest's JSON terminal; the 16-variant `PredicateName` closed DU; the V1 differential surface) + retained deferrals (PreRemediation; the 4 always-false PredicateName variants; per-divergence Unsupported rationale).

### 6. Fresh-eye walk (cross-document drift)

- `V2_DRIVER.md` — chapter 4.4 was not listed as a separate phase; it lived inside the Phase 6 (RemediationEmitter) framing as a free corollary. The 2026-05-17 doc-refresh entry already noted chapter 4.4 — Manifest diagnostic fields as the largest piece of named pending work; this close transitions that work to shipped state. Update at next chapter open if a follow-on chapter 4.4 RemediationEmitter ships separately.
- `BACKLOG.md` — added chapter 4.4 as a new closed phase section. Sequencing graph §VII shows chapter 4.4 retired from the "named pending" lane.

### 7. V1-input-envelope walk

V1's `Osm.Emission.SsdtManifest` (V1 source `src/Osm.Emission/SsdtManifest.cs:6-14`) + `SsdtPredicateCoverage` (`src/Osm.Emission/SsdtPredicateCoverage.cs:7-49`) + `ManifestBuilder.Create` (`src/Osm.Emission/ManifestBuilder.cs:11-74`) are the three empirical references. All three inform V2 algebra:

- V1's `SsdtCoverageSummary.CreateComplete` shape → V2's `CoverageSummary.createComplete` shape (1:1 mirror).
- V1's `CoverageBreakdown.ComputePercentage` contract (Math.Round AwayFromZero; total=0→100; emitted=0→0) → V2's `CoverageBreakdown.computePercentage` (verbatim mirror; differential test asserts).
- V1's `SsdtPredicateNames` 16 string constants → V2's `PredicateName` 16-variant closed DU + `toString` mirror (set-equality differential test).
- V1's `PredicateCoverageEntry` shape (Module / Schema / Table / Predicates) → V2's `PredicateCoverageEntry` shape (1:1 mirror).
- V1's `SsdtPredicateCoverage` shape (Tables + PredicateCounts dict) → V2's `PredicateCoverage` shape (Tables + PredicateCounts Map; JSON serialization diverges per chapter open Q2).
- V1's `Unsupported : IReadOnlyList<string>` shape → V2's `Unsupported : string list` (1:1 at the JSON layer).
- V1's `PreRemediationManifestEntry` shape → V2 reads the shape but defers content per V2_DRIVER §154.

No carbon-copy event in this chapter. The lifts are V2 mirrors of V1's reference types under empirical pressure from V1's source schema; the manifest emission code is V2-native.

### 8. AXIOMS.md amendment cash-out

No new amendments earned at chapter 4.4 close. The chapter operates within:

- **A18 amended** — Coverage / PredicateCoverage / Unsupported compute functions consume Catalog (+ unit for Unsupported), never Policy. Type-level enforcement preserved structurally.
- **T1** — byte-determinism preserved across the new emission paths (sorted PredicateCounts array; sorted Unsupported list; CoverageBreakdown rounding contract).
- **T11** — sibling-Π keyset coverage holds trivially — the manifest is a Π but its keyset doesn't grow (Tables count of TableManifestEntry already matched Catalog.allKinds at chapter 4.1.A slice 9 close).
- **A39** — `CoverageBreakdown.create` smart constructor with Emitted ≤ Total + non-negative invariant.

Per the chapter open's AXIOMS amendment scan, no placeholder was scheduled; no body required at close.

---

## Test count

- **1313 non-canary tests passing** (was 1262 at chapter A.4.7' close baseline; +51 across the chapter — 18 slice α + 14 slice β + 8 slice γ + 11 slice δ)
- **~16 Docker-dependent canary tests** (skip-if-no-Docker gated)
- **Lint clean** across 27 rules
- **Build clean** under `TreatWarningsAsErrors=true` everywhere

---

## What's load-bearing going forward

Chapter 4.4's structural commitments that future chapters inherit:

- **`CoverageBreakdown` smart constructor** — every future emit-with-tolerance manifest field uses this typed primitive with V1-rounding-contract preserved.
- **`PredicateName` closed DU** — when V2 IR grows the relevant evidence fields (Filter expression on Index; key/included column split on Index.Columns; logical-vs-physical Reference distinction), the 4 currently-always-false evaluate arms lift to real evaluation. The DU's empirical-test discipline ensures the lift surfaces.
- **`ToleratedDivergence` enumeration as the Unsupported source** — when a Tolerance variant retires (chapter widens IR + emitter consumption), the manifest's Unsupported list shortens automatically. The chapter that retires `IndexesUnreflected` (PhysicalSchema's non-PK index reflection) trips the assertion in `ManifestUnsupportedTests.fs` "current-variant content audit" — forcing the test to update with the new shorter list.

---

## What's deferred (with explicit triggers)

### PreRemediation field population

Per V2_DRIVER §154: RemediationEmitter is deferred under V2-driver KPI ("revisit at chapter 5+ if remediation is operator-needed"). Chapter 4.4's `preRemediation = []` is structurally correct under that deferral — V2 emits no remediation context because no remediation has occurred. **Trigger to cash out**: an operator workflow demands programmatic partial-state recovery (vs the current dev-tooling Docker image's "regenerate fresh + redeploy" pattern).

### PredicateName 4-variant always-false group

Four predicates (HasFilteredIndex / HasIncludedIndexColumns / HasLogicalForeignKeyWithoutDbConstraint / HasLogicalForeignKeyWithDbConstraint) emit false unconditionally because V2's IR doesn't carry the relevant evidence. **Trigger to cash out**:

- HasFilteredIndex → IR refinement adds `Index.Filter : string option` (likely a DACPAC-adapter-driven slice or V1-rowset extension).
- HasIncludedIndexColumns → IR refinement adds key/included column split to `Index.Columns` (same trigger family).
- HasLogicalForeignKey×DbConstraint pair → IR or Tightening pass surfaces the materialization decision into a typed flag accessible at manifest emit time.

Each variant's docstring carries the forward signal; the closed-DU empirical-test discipline surfaces the lift at compile time when the IR refinement lands.

### Unsupported per-divergence rationale

V2 currently emits Unsupported as a list of discriminator-name strings. **Trigger to widen**: a downstream consumer demands per-divergence rationale strings (e.g., dashboard rendering "HeaderCommentsOmitted: V2 emits without the /* Source: ... */ header block per …"). At that point Unsupported widens to a typed record list (per chapter open Q3 forward signal).

### V1↔V2 PredicateCounts JSON-shape divergence

V2 emits `predicateCounts` as a sorted-by-name array of `{name, count}` objects; V1 emits as a JSON dict. **Trigger to cash out**: a downstream consumer demands byte-equality with V1's manifest. Resolution: either add a `Tolerance.PredicateCountsShapeDivergence` variant (documented per-environment) or flip V2 to emit a JSON dict (T1 byte-determinism preserved by sorting keys at serialization time via `JsonOptions.indented()` — F# Map ordering is by key, so the dict shape is also deterministic).

### Options / PolicySummary / Emission fields (V2 doesn't emit)

V1's SsdtManifest carries three additional fields V2 doesn't emit: `Options` (SmoBuildOptions), `PolicySummary` (decision-report rollups), `Emission` (algorithm + hash; V2 emits `emitter + version + registry.digest` instead). **Trigger to cash out per field**: an operator workflow demands the manifest carry the field shape (e.g., dashboard inspects `PolicySummary.TightenedColumnCount`). Each field is a separate slice when its trigger fires; out of chapter 4.4 scope.

---

## What this close enables

- **Operator-facing manifest audit.** A V2-emitted manifest is now meaningfully diff-able against V1's reference shape at the Coverage / PredicateCoverage / Unsupported axes. Operators can audit per-axis evidence at the file surface.
- **Chapter 4.3 manifest integration.** Chapter 4.3's three-channel Diagnostics emission can extend the manifest's `Unsupported` (if it surfaces V1↔V2 diagnostic-emission divergences as `ToleratedDivergence` variants) without re-architecting the emitter.
- **Future RemediationEmitter (chapter 5+).** When RemediationEmitter ships, it populates `PreRemediation` via the established `Manifest.PreRemediation` field shape; no manifest schema change required.

---

## Closing

Chapter 4.4 is structural-completion work — the V2 manifest now carries real per-axis evidence instead of placeholder defaults at three of the four `chapter 4.4 fills` deferral sites. The chapter's signature deliverable is the **V1 differential surface (slice δ)** which proves V2's emission matches V1's SsdtManifest schema modulo documented divergences (registry.digest field; predicateCounts as sorted array of objects).

Per V2_DRIVER's per-axis correctness stakes, this is **Lower-stakes operational-diagnostics work** — the chapter sits below the cutover-blocking property axes. Its value compounds at the operator-facing layer: every V2 emit now produces a manifest that documents what V2 actually covered.

— Chapter 4.4 closed (2026-05-17).
