# Chapter 4.4 open — Manifest diagnostic fields (retire the `null` defaults)

**Sessions:** opens with this document. **Posture:** retires the four `chapter 4.4 fills` deferrals codified in `ManifestEmitter.fs:32-33,180-183`. **Predecessors:** chapter 4.1.A (slice 9 shipped `ManifestEmitter.fs` with the four deferred fields emitted as `null` / empty); chapter A.0' (lifted IR fields the `PredicateCoverage` slice depends on); chapter 4.1.A slice 8 reopen (sp_addextendedproperty emission; ExtendedProperties IR pickup); chapter 4.3 (three-channel Diagnostics; the Routing primitive that `Unsupported` may consume).

This is the chapter-open document per the strategic-frame-at-chapter-open discipline (`DECISIONS 2026-05-15 — Strategic frame for the OSSYS implementation chapter`; multi-session chapters earn this discipline at chapter open).

---

## Why this chapter

`Projection.Targets.SSDT.ManifestEmitter` ships the V2 manifest file (`<outDir>/manifest.json`) mirroring V1's `SsdtManifest` schema (`src/Osm.Emission/SsdtManifest.cs:6-14`) per the V1↔V2 ubiquitous-language commitment. Four fields currently emit as `null` / empty arrays with the docstring deferral "chapter 4.4 fills":

```fsharp
doc.Add("coverage", null)                              // ManifestEmitter.fs:180
doc.Add("predicateCoverage", null)                     // ManifestEmitter.fs:181
doc.Add("preRemediation", JsonArray() :> JsonNode)     // ManifestEmitter.fs:182
doc.Add("unsupported", JsonArray() :> JsonNode)        // ManifestEmitter.fs:183
```

Three of the four are reachable from V2's existing evidence (Catalog + IR + Tolerance taxonomy + Diagnostics writer); the fourth (`PreRemediation`) requires `RemediationEmitter` which `V2_DRIVER.md` §154 explicitly defers ("deferred under V2-driver KPI; revisit at chapter 5+ if remediation is operator-needed"). This chapter retires the three reachable deferrals; `PreRemediation` stays empty-array under the same V2_DRIVER deferral framing.

V1's reference shapes — `SsdtCoverageSummary` (Tables / Columns / Constraints CoverageBreakdown), `SsdtPredicateCoverage` (per-table Predicates list + PredicateCounts dictionary), and `Unsupported : IReadOnlyList<string>` — provide the structural target. V2 inherits the field semantics and emits typed values; the JSON shape stays V1-compatible.

**Operator-facing payoff.** The manifest stops emitting "this field is intentionally null" and starts carrying real evidence about per-axis coverage. An operator inspecting the manifest sees: how many tables / columns / constraints emitted vs the catalog total; which predicates each table satisfies; which tolerated V1↔V2 divergences are in play. This is operational diagnostics at the V1-shape surface, not new algebra.

**Out of scope.**

- **`PreRemediation` field.** Requires RemediationEmitter; deferred-with-trigger per V2_DRIVER §154 + per the existing `CHAPTER_4_PRESCOPE_DIAGNOSTICS_AND_REMEDIATION.md` Part 2 framing. The current empty-array emission is structurally correct ("no remediation context yet"); the chapter that ships RemediationEmitter will populate the field naturally.
- **`Options : SsdtManifestOptions`** (V1's `IncludePlatformAutoIndexes` / `EmitBareTableOnly` / `SanitizeModuleNames` / `ModuleParallelism`). V2's manifest doesn't currently emit this field at all; whether to add it lives in a separate slice or chapter when an operator workflow demands flag echo. Out of chapter 4.4's named scope.
- **`PolicySummary : SsdtPolicySummary?`** (V1's tightened-column / unique-index / FK / rationale-distribution rollups). V2 doesn't emit this either; the rollup data is reachable via Lineage trail + Diagnostics writer, but the V1 shape is large and operator-facing rather than algebraically-load-bearing. Out of chapter 4.4's named scope; revisit at chapter 5+ if operator-needed.
- **`Emission : SsdtEmissionMetadata` (Algorithm + Hash).** V2 emits `emitter` + `version` + `registry.digest` instead — same role (downstream consumer detects manifest-schema changes), different shape. The V1-shape divergence is documented at chapter 4.1.A slice 9; chapter 4.4 doesn't re-litigate.

---

## Strategic frame — eight axes named at chapter open

Per the OSSYS / chapter-4.1.A / chapter-4.1.B / chapter-4.2 / chapter-4.3 precedent.

1. **DDD — typed value objects per field; concept-shaped per pillar 8.** `CoverageSummary { Tables; Columns; Constraints }` with `CoverageBreakdown { Emitted; Total; Percentage }`; `PredicateCoverage { PerTable : PredicateCoverageEntry list; PredicateCounts : Map<PredicateName, int> }`; `PredicateName` as a closed DU (the 17 V1-named predicates from `SsdtPredicateNames`). Each type IS the structural carrier of its axis. Pillar 8 four-question analysis: each name is concept-shaped (`CoverageSummary` IS the summary; not `Summarize`); generic-suffix smell test clean (no Helper / Util / Manager).

2. **FP — pure functions of Catalog (+ Tolerance for slice γ).** `Coverage.compute : Catalog -> CoverageSummary`; `PredicateCoverage.compute : Catalog -> PredicateCoverage`; `Unsupported.compute : Tolerance -> string list`. A18 amended preserved structurally — no Policy parameter; the functions are pure CataIog-→-evidence. Each call sets a `Bench.scope` for iterator-logging-as-first-class-outcome.

3. **Hardcore (no string-concatenation) — predicates dispatch via closed-DU pattern match; JSON via typed JsonNode.** `PredicateName` is a closed DU (17 variants per V1's `SsdtPredicateNames`); `PredicateName.evaluate : PredicateName -> Kind -> bool` is an exhaustive match over the DU plus IR-field consult. The match's per-arm consults are typed (`not (List.isEmpty k.Triggers)`; `match k.Modality with ModalityMark.Static _ -> true | _ -> false`; etc.) — no string conversion. The JSON serialization continues to flow through the existing `JsonObject` / `JsonArray` typed-tree path established at chapter 4.1.A slice 9 (ManifestEmitter's `toNode`).

4. **Streaming — bench scopes per axis, per traversal.** `emit.manifest.coverage` / `emit.manifest.predicateCoverage` / `emit.manifest.unsupported` Bench scopes wrap each computation. Per-table iteration uses `Bench.iterMap "manifest.predicateCoverage.table"` to surface per-table predicate-evaluation cost in the rollup table.

5. **Hexagonal — pure-Core computation; no I/O; no Profile, no Policy.** Each computation lives in `Projection.Targets.SSDT.ManifestEmitter` (or in a sibling helper if the file grows beyond comfort — argue at the second consumer). Adapters at the boundary are unchanged; the Catalog already carries every IR field the 17 predicates need (chapter A.0' lifted Triggers / Sequences / DefaultValue / Computed / ColumnChecks / ExtendedProperties / Temporal; chapter A.0' slice ι confirmed L3-Boundary-NoSilentDrop).

6. **Built-in obligation — `Utf8JsonWriter` for JSON; existing precedent.** No new BCL adoption; `JsonObject.Add` / `JsonArray.Add` / `JsonValue.Create` carry the slice work through to the typed tree. The terminal `Utf8JsonWriter` writes once.

7. **Aggregate-root + smart constructor — `CoverageBreakdown.create` mirrors V1's percentage-rounding contract.** V1's `CoverageBreakdown.Create(emitted, total)` computes `Math.Round(value, 2, MidpointRounding.AwayFromZero)` (`SsdtManifest.cs:72-89`). V2's smart constructor returns `Result<CoverageBreakdown>` with the same rounding rule (decimal-precision per the supreme operating discipline pillar 5; T1 byte-determinism rests on stable rounding). V1's edge cases (`total = 0` → 100%; `emitted = 0` → 0%) preserved structurally.

8. **Test-fidelity — per-axis property tests + V1 differential.** Per axis: idempotence (same Catalog → same value); determinism under shuffle (Catalog with permuted Modules/Kinds → same value modulo sorted ordering); exhaustiveness (every Kind contributes to PredicateCoverage). Plus a V1 differential test: V2's computed CoverageSummary / PredicateCoverage on a shared fixture round-trips through V1's `SsdtManifest.cs` shape via `JsonSerializer` and equals what V1's `ManifestBuilder.Create` would produce on the same input (modulo the V2-only `registry.digest` field which V1 doesn't carry).

---

## Slice arc

Per pre-scope discipline + the strategic frame above, the chapter's slices are ordered by IR-grows-under-evidence (each subsequent slice has at least one consumer for each new type it lands).

| # | Slice | Goal | LOC budget |
|---|---|---|---|
| α | `CoverageSummary` value type + `Coverage.compute` + manifest emission + property tests | Count tables / columns / constraints emitted vs catalog total; replace `null` with typed evidence | ~120 src + ~100 test |
| β | `PredicateCoverage` value type + `PredicateName` closed DU (17 variants) + `PredicateName.evaluate` exhaustive match + manifest emission + per-table property tests | Per-table predicate list + PredicateCounts dictionary; replaces `null` | ~250 src + ~150 test |
| γ | `Unsupported` field populated from `ToleratedDivergence.allKnown` rendered as string list + manifest emission + property test | Replaces empty array with the V1↔V2 divergences in play | ~60 src + ~60 test |
| δ | V1 differential test (against `Osm.Emission.ManifestBuilder.Create`) + chapter-close eight-item ritual + AXIOMS amendment scan | Confirms V1↔V2 ubiquitous-language commitment at the manifest surface | ~120 test + close ritual |

**Total: ~430 LOC source + ~430 LOC tests.** Estimated 4 sessions at session cadence (1 per slice).

This document opens with **slice α** ready to ship.

**Hard deferral preserved.** `PreRemediation` stays empty-array per `V2_DRIVER.md` §154. The chapter close ritual scans the Active deferrals index; RemediationEmitter remains a `proposed` carbon-copy candidate (per BACKLOG.md §IV) with no trigger fire.

---

## What this chapter does **not** do

Bounded by the strategic frame and V2_DRIVER's deferral discipline:

- **No RemediationEmitter.** Per V2_DRIVER §154 (free corollary; deferred under V2-driver KPI; revisit at chapter 5+ if remediation is operator-needed). Chapter 4.4's `PreRemediation` field stays empty-array; the rightful chapter is the one that ships RemediationEmitter, not this one.
- **No Options / PolicySummary / Emission fields.** V2's manifest omits these by design (Options + PolicySummary) or by V2-shape-divergence (Emission → registry-digest). Adding them is a separate scope decision; out of chapter 4.4.
- **No new IR fields.** Every predicate the chapter evaluates consults an existing IR field (chapter A.0' lifted what was missing).
- **No new pass.** The chapter is emitter-axis work — `ManifestEmitter` consumes Catalog (+ Tolerance for slice γ) and produces typed evidence; no pass surface change.
- **No CLI flag changes.** The manifest emission path through `Projection.Pipeline` is unchanged; the new fields show up automatically in the existing emit flow.

---

## Companion documents

- **Pre-scope (operational plan):** `CHAPTER_4_PRESCOPE_DIAGNOSTICS_AND_REMEDIATION.md` (Part 1 = chapter 4.3 closed; Part 2 = chapter 4.4 RemediationEmitter — preserved as the deferred-with-trigger framing per V2_DRIVER §154; this chapter is a smaller scope retiring the manifest's deferred fields, not the full RemediationEmitter pre-scope).
- **V1 reference shapes:** `/home/user/outsystems-ddl-exporter/src/Osm.Emission/SsdtManifest.cs` (8-field shape), `/home/user/outsystems-ddl-exporter/src/Osm.Emission/SsdtPredicateCoverage.cs` (17-predicate list + PredicateCoverageEntry shape), `/home/user/outsystems-ddl-exporter/src/Osm.Emission/ManifestBuilder.cs:17-72` (V1's build flow + defaults).
- **V2 surface to extend:** `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Targets.SSDT/ManifestEmitter.fs` (lines 32-33,76-94,176-183 are the deferral docstring + Manifest record + null emission sites; this is what the chapter modifies).
- **Strategic frame precedent:** `CHAPTER_4_1_A_OPEN.md` (sibling chapter; same eight-axis discipline).
- **V2-driver KPI:** `V2_DRIVER.md` (per-axis correctness stakes table places operational diagnostics axis as "Lower" stakes; the structural commitment ships the operator surface).

---

## Open questions resolved at chapter open

**Q1 — 17 predicates vs subset.** V1's `SsdtPredicateNames` lists 17 constants. Decision: V2 ships the closed DU with all 17 variants in slice β; per-arm evaluation cites the IR field consult; the closed-DU empirical-test discipline catches missing arms. Trigger to grow beyond 17: a real V1 fixture surfaces an 18th predicate, or V2 grows an IR axis that warrants a new predicate.

**Q2 — `PredicateCounts` dictionary order.** V1's `IReadOnlyDictionary<string, int> PredicateCounts` is unordered by .NET semantics; the JSON serialization must be deterministic. Decision: V2 emits `PredicateCounts` as a sorted-by-key list of `{ name, count }` objects (not as a JSON object with key:value pairs) to preserve T1 byte-determinism. The V1 differential test absorbs this as a documented divergence — V1 serializes as an unordered dict; V2 as a sorted array. (Forward signal: if the V1 differential needs JSON-shape equality, add a `Tolerance.UnsupportedFieldShapeDivergence` variant.)

**Q3 — `Unsupported` content semantics.** V1's `IReadOnlyList<string>` carries strings naming unsupported V1 features. Decision: V2 renders each `ToleratedDivergence` variant as `<discriminator-name>` (e.g., `"HeaderCommentsOmitted"`); the list is sorted by string comparison for T1 byte-determinism. Trigger to widen: an operator workflow demands per-divergence rationale strings rather than just names — then `Unsupported` widens to a typed record list (forward signal).

**Q4 — Slice α `Coverage` semantics.** V1's `CreateComplete(tables, columns, constraints)` produces a 100%-coverage summary when the emitter emits everything. V2 always emits every kind in the catalog (T11 keyset coverage holds structurally), so V2's `Coverage` is always `CreateComplete`. Decision: ship the typed value, not the always-100% optimization — the structural commitment IS the value, and a future emitter that selectively emits (per `EmissionPolicy.Selection`) will need the per-axis denominator. Smart constructor catches `Emitted > Total` (impossible by construction; smart-constructor guard).

---

## AXIOMS amendment scan at chapter open

Per the `Amendments scheduled (chapter close)` scaffolding discipline (`DECISIONS 2026-05-22 — Stage 0 foundation phase` S0.F): chapter 4.4 has **no new axiom candidate**. The chapter operates within existing axioms — `A18 amended` (no Policy in emitters; Coverage / PredicateCoverage / Unsupported consume Catalog [+ Tolerance for γ], never Policy); `T1` (byte-determinism; CoverageBreakdown rounding + Unsupported sort preserve it); `T11` (sibling-Π keyset coverage; trivially preserved — the manifest is a Π but its keyset doesn't grow). No amendment placeholder lands at this open.

---

## Closing

Chapter 4.4 is structural-completion work. The V2 manifest emits a V1-compatible schema today with three fields placeholder-shaped as `null` / `[]`; this chapter populates them from evidence V2 already carries. The chapter's signature deliverable is **the V1 differential test (slice δ)** — proves V2's manifest emission matches V1's `SsdtManifest` shape modulo documented divergences (registry.digest field; PredicateCounts as sorted-list-of-objects; PreRemediation as empty-array-until-RemediationEmitter).

Per V2_DRIVER's per-axis correctness stakes table, this chapter sits at the **Lower-stakes operational-diagnostics axis** — the structural commitment ships an operator surface, not a cutover-blocking property. The chapter's slice scope is correspondingly compact (~430 LOC src + ~430 LOC tests across 4 slices).

Slice α opens.
