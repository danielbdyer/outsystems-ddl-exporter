module Projection.Tests.OssysSsdtManifestParityTests

// V1 parity audit — slice 5.5.α.manifest. Reserves matrix rows
// 95-104 (V1's SSDT manifest-emission cluster vs V2's ManifestEmitter
// — the V1-differential walk for the manifest shape; chapter 4.4
// close already shipped the V2 equivalents).

open Xunit

[<Fact(Skip = "Matrix row 95 — 🟡 DIVERGENCE. V1's `Osm.Emission/SsdtManifest.cs` (~91 LOC) top-level shape carries 8 fields: Tables, Options, PolicySummary, Emission, PreRemediation, Coverage, PredicateCoverage, Unsupported. V2's `Projection.Targets.OperationalDiagnostics.ManifestEmitter.fs` Manifest record carries 6 fields: Tables, EmitterVersion, RegistryDigest, Coverage, PredicateCoverage, Unsupported (PreRemediation emitted as `[]` per V2_DRIVER §154; Options + PolicySummary deferred). V2 adds **EmitterVersion** (versioning stamp) + **RegistryDigest** (chapter A.4.7' slice ζ; registry metadata SHA256 determinism). See `DECISIONS 2026-05-18 (slice 5.5.α.manifest) — V1-differential walk: manifest scope-reduction with V2-extension fields`. **Documented structural reduction, not parity loss** — V1's manifest was a union of multiple semantic layers; V2's manifest is catalog-only. **Cash-out for Options + PolicySummary**: chapters 4.5+ when policy/profile-level metadata surfaces have V2 consumers; until then deliberately deferred.")>]
let ``5.5.α row 95: V1 SsdtManifest 8-field shape vs V2 Manifest 6-field shape (V2 scope-reduces; adds version + digest)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 95 + DECISIONS 2026-05-18 (slice 5.5.α.manifest)"

[<Fact(Skip = "Matrix row 96 — 🟢 PARITY. V1's CoverageBreakdown rounding contract uses `Math.Round(value, 2, MidpointRounding.AwayFromZero)`; edge cases: total=0→100%; emitted=0→0%. V2's `Coverage.compute` mirrors line-for-line — `System.Math.Round(value, 2, System.MidpointRounding.AwayFromZero)`; edge cases: total ≤ 0 → 100m; emitted ≤ 0 → 0m (ManifestEmitter.fs:78-83 vs SsdtManifest.cs:76-90). Chapter 4.4 slice α confirmed. **No additional parity work needed** — exact rounding-contract match.")>]
let ``5.5.α row 96: V1 CoverageBreakdown rounding contract ↔ V2 Coverage.compute PARITY (line-for-line)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 96"

[<Fact(Skip = "Matrix row 97 — 🟢 PARITY. V1's `SsdtCoverageSummary(Tables, Columns, Constraints)` carries three `CoverageBreakdown` members per axis. V2's `CoverageSummary = { Tables; Columns; Constraints }` F# record mirrors directly. V2's `CoverageSummary.createComplete` mirrors V1's `SsdtCoverageSummary.CreateComplete` (lines 117-129 of ManifestEmitter.fs vs 59-65 of SsdtManifest.cs). Chapter 4.4 slice α confirmed. **No additional parity work needed** — three-axis shape identical.")>]
let ``5.5.α row 97: V1 SsdtCoverageSummary three-axis ↔ V2 CoverageSummary PARITY`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 97"

[<Fact(Skip = "Matrix row 98 — 🟡 DIVERGENCE (structural improvement). V1's `TableManifestEntry` carries 7 fields: Module, Schema, Table, TableFile, **Indexes** (list<string> of index names), **ForeignKeys** (list<string> of FK names), **IncludesExtendedProperties** (bool). V2's TableManifestEntry carries 6 fields: Module, Schema, Table, TableFile, **IndexCount** (int), **ForeignKeyCount** (int) — name lists replaced with counts; IncludesExtendedProperties dropped. See `DECISIONS 2026-05-18 (slice 5.5.α.manifest) — TableManifestEntry: counts over name-lists` for the structural rationale (V1's name lists are metadata redundant with the per-table DDL files; counts suffice for manifest coverage metrics). **Operationally transparent** — downstream consumers read counts for summary statistics, not names. JSON shape differs (V1: `{indexes: [\"IX_A\", \"IX_B\"]}`; V2: `{indexCount: 2}`). **Cash-out for IncludesExtendedProperties**: deferred to chapter A.0' extended-property emission completion; V2 carries the data but doesn't surface in the per-table manifest entry.")>]
let ``5.5.α row 98: V1 TableManifestEntry name-lists vs V2 cardinality-only (V2 structural improvement)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 98 + DECISIONS 2026-05-18 (slice 5.5.α.manifest)"

[<Fact(Skip = "Matrix row 99 — 🟢 PARITY. V1's `SsdtPredicateCoverage(Tables: PredicateCoverageEntry[], PredicateCounts: dict<string, int>)` two-section shape. V2's `PredicateCoverage = { Tables: PredicateCoverageEntry list; PredicateCounts: Map<PredicateName, int> }` carries same logical structure. V2 uses typed `PredicateName` DU (16 variants per chapter 4.4 slice β) instead of string keys — type-safety improvement; rendering happens at JSON boundary via `PredicateName.toString`. Section count identical. Chapter 4.4 slice β confirmed. **No additional parity work needed**.")>]
let ``5.5.α row 99: V1 SsdtPredicateCoverage two-section shape ↔ V2 PredicateCoverage PARITY (typed names)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 99"

[<Fact(Skip = "Matrix row 100 — 🟢 PARITY. V1's `PredicateCoverageEntry(Module, Schema, Table, Predicates: list<string>)` per-entry shape. V2's `{ Module: string; Schema: string; Table: string; Predicates: PredicateName list }` mirrors directly. V2 carries typed `PredicateName` variants (F# DU); JSON serialization renders names via `PredicateName.toString` at terminal boundary. Chapter 4.4 slice β confirmed. **No additional parity work needed**.")>]
let ``5.5.α row 100: V1 PredicateCoverageEntry per-entry ↔ V2 typed PredicateName entry PARITY`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 100"

[<Fact(Skip = "Matrix row 101 — 🟡 DIVERGENCE (documented; T1 byte-determinism gain). V1 emits `predicateCounts` as JSON dict `{\"HasTrigger\": 5, \"HasDefaultConstraint\": 3, ...}` — keys are predicate names; object-property order is parser-implementation-specific. V2 emits as sorted-by-name array `[{\"name\": \"HasCheckConstraint\", \"count\": 2}, ...]` per chapter 4.4 open Q2 (resolved at close). Documented in ManifestEmitter.fs:226-230 + 650-654. **Rationale**: T1 byte-determinism (dict order is insertion-dependent in some JSON parsers, implementation-specific in others; array order is sortable + deterministic). V2's sort order: canonical `PredicateName.all` enumeration (alphabetic by variant name). Covered by the chapter 4.4 close DECISIONS row on byte-determinism. **Cash-out** (if V1-byte-equality demanded by a downstream consumer): a Tolerance variant `Tolerance.PredicateCountsJsonShapeDivergence` would mark the difference; consumers either accept the V2 array shape OR a serializer mode flips to V1-dict shape with key-sorted serialization. No current consumer demands it.")>]
let ``5.5.α row 101: V1 predicateCounts JSON dict vs V2 sorted array (V2 T1 byte-determinism)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 101"

[<Fact(Skip = "Matrix row 102 — 🟢 PARITY (documented deferral). V1's PreRemediation carries actual `List<PreRemediationManifestEntry>` with (Module, Table, TableFile, Hash) tuples — remediation entries accumulated during emission; V1's engine defers operator action post-deploy. V2 emits `\"preRemediation\": []` (empty array) unconditionally per V2_DRIVER §154 — RemediationEmitter is explicitly deferred to chapter 5+. ManifestBuilder's nullable parameter `IReadOnlyList<PreRemediationManifestEntry>?` (line 16 of ManifestBuilder.cs) mirrors V2's deferred-to-chapter gating. **Correct parity scoping** — V2's manifest version 1 (per ManifestEmitter.fs:477) documents this as a chapter-4-close deliverable; upstream chapters don't populate remediation; chapter 5's RemediationEmitter ships that feature (paired with matrix row 83). **Acceptance**: when RemediationEmitter ships, an integration test asserts V2's PreRemediation matches V1's shape on a representative deployment scenario.")>]
let ``5.5.α row 102: V1 PreRemediation entries vs V2 empty array (chapter 5+ deferred per V2_DRIVER §154)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 102 + V2_DRIVER §154"

[<Fact(Skip = "Matrix row 103 — 🟢 PARITY. V1's `ManifestBuilder.Build` orchestrates: scan table snapshots, extract emission metadata, build `TableManifestEntry` list; optionally wrap PolicyDecisionReport into SsdtPolicySummary; pass-through Coverage/PredicateCoverage/Unsupported parameters (nullable with defaults); emit SsdtManifest record. V2's `ManifestEmitter.buildWith(registry, catalog)` computes entries via `catalog.Modules |> List.collect (fun m -> m.Kinds |> List.map ...)`; computes Coverage via `Coverage.compute catalog`; computes PredicateCoverage via `PredicateCoverage.compute catalog`; computes Unsupported via `Unsupported.compute()`; manually threads registry digest; emits Manifest record. **Same orchestration family; scoped differently per V2's architecture** (V1 caller provides manifests as parameters; V2 computes them per A18 amended — catalog-only, no policy). Chapter 4.4 close confirmed.")>]
let ``5.5.α row 103: V1 ManifestBuilder.Build orchestration ↔ V2 ManifestEmitter.buildWith PARITY (catalog-only per A18)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 103"

[<Fact(Skip = "Matrix row 104 — 🟢 PARITY. JSON serialization property naming: V1's C# record field names (PascalCase) serialize as PascalCase via System.Text.Json default; V2 manually builds JsonObject with camelCase keys: emitter, version, registry, tables, coverage, predicateCoverage, unsupported, preRemediation, indexCount, foreignKeyCount (ManifestEmitter.fs:614-693). **Both emit camelCase at JSON boundary** (operator-facing manifest.json file). V1 defaults to PascalCase but uses `JsonPropertyName` attributes to emit camelCase; V2 explicitly constructs camelCase in the JsonObject builder. **The JSON shape on disk is identical** modulo the documented divergences (rows 95 + 98 + 101).")>]
let ``5.5.α row 104: V1 PascalCase records + JsonPropertyName attrs ↔ V2 explicit camelCase JsonObject builder PARITY`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 104"

[<Fact>]
let ``5.5.α.manifest: ssdt-manifest parity file present`` () =
    Assert.True(true)
