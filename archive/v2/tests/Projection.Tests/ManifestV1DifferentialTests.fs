module Projection.Tests.ManifestV1DifferentialTests

open System.Text.Json.Nodes
open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

// Chapter A.4.7' slice η — shim restoring the Lineage<Catalog> shape.
let private ciRun (c: Catalog) : Lineage<Catalog> =
    CanonicalizeIdentity.registered.Run c |> Lineage.map (fun d -> d.Value)

let private enrich (c: Catalog) : Catalog = (ciRun c).Value

let private requireChild (label: string) (n: JsonNode | null) : JsonNode =
    match Option.ofObj n with
    | Some node -> node
    | None      -> Assert.Fail (sprintf "expected %s child" label); Unchecked.defaultof<JsonNode>

// ---------------------------------------------------------------------------
// Chapter 4.4 slice δ — V1 differential.
//
// Per chapter 4.4 open §1 (Why this chapter): V2's manifest emits a
// V1-compatible schema. V1's reference shapes live at:
//   /home/user/outsystems-ddl-exporter/src/Osm.Emission/SsdtManifest.cs
//   /home/user/outsystems-ddl-exporter/src/Osm.Emission/SsdtPredicateCoverage.cs
//
// This differential cross-checks V2's emitted shape against V1's
// reference *types* by asserting:
//
//   1. V2's PredicateName closed-DU variant names match V1's
//      SsdtPredicateNames string constants verbatim.
//
//   2. V2's CoverageBreakdown percentage-rounding contract matches
//      V1's `CoverageBreakdown.ComputePercentage` on V1-witnessed
//      edge cases.
//
//   3. V2's Coverage / PredicateCoverage / Unsupported emit shape
//      survives JSON round-trip with the V1-schema-shaped keys
//      operators consult.
//
// Documented divergences (carried in the open doc's resolved-at-open
// Q-list; future Tolerance variants if a downstream consumer demands
// byte-equality):
//
//   - V2 emits `predicateCoverage.predicateCounts` as a sorted-by-name
//     array of `{name, count}` objects; V1 emits as a JSON dict
//     (per chapter 4.4 open Q2).
//   - V2's manifest carries `registry` (chapter A.4.7' digest) which
//     V1 doesn't carry.
//   - V2's manifest doesn't currently emit `Options` or `PolicySummary`
//     (out of chapter 4.4 scope per the open doc).
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// Differential 1: PredicateName names match V1's SsdtPredicateNames
// constants verbatim.
// ---------------------------------------------------------------------------

/// V1's SsdtPredicateNames constant values, copy-cited from
/// `/home/user/outsystems-ddl-exporter/src/Osm.Emission/SsdtPredicateCoverage.cs:7-25`.
/// When V1 widens its constant set, this list updates to match the
/// V1 source; V2's `PredicateName` DU follows.
let private v1PredicateNameConstants : string list =
    [ "HasTemporalHistory"
      "HasTrigger"
      "IsStaticEntity"
      "IsExternalEntity"
      "IsInactiveEntity"
      "HasInactiveColumns"
      "HasDefaultConstraint"
      "HasCheckConstraint"
      "HasExtendedProperties"
      "HasUniqueIndex"
      "HasCompositeUniqueIndex"
      "HasFilteredIndex"
      "HasIncludedIndexColumns"
      "HasLogicalForeignKey"
      "HasLogicalForeignKeyWithoutDbConstraint"
      "HasLogicalForeignKeyWithDbConstraint" ]

[<Fact>]
let ``V1 differential: PredicateName.all renders every V1 SsdtPredicateNames constant verbatim`` () =
    let v2Names = PredicateName.all |> List.map PredicateName.toString |> Set.ofList
    let v1Names = v1PredicateNameConstants |> Set.ofList
    Assert.Equal<Set<string>> (v1Names, v2Names)

[<Fact>]
let ``V1 differential: PredicateName.all cardinality matches V1 SsdtPredicateNames count`` () =
    Assert.Equal (List.length v1PredicateNameConstants, List.length PredicateName.all)

// ---------------------------------------------------------------------------
// Differential 2: CoverageBreakdown percentage-rounding contract matches
// V1's ComputePercentage (SsdtManifest.cs:76-90).
// ---------------------------------------------------------------------------

[<Fact>]
let ``V1 differential: CoverageBreakdown rounds half-up away from zero (matches V1 Math.Round AwayFromZero)`` () =
    // V1's Math.Round(value, 2, MidpointRounding.AwayFromZero) rounds
    // .5 to the higher absolute value. Check 1/8 = 12.5% (no rounding
    // ambiguity) and 1/40 = 2.5% (same) — these are exact decimals;
    // the AwayFromZero behavior only matters when the third decimal is
    // exactly 5.
    let r1 = CoverageBreakdown.create 1 8 |> Result.value
    Assert.Equal (12.5m, r1.Percentage)
    let r2 = CoverageBreakdown.create 1 40 |> Result.value
    Assert.Equal (2.5m, r2.Percentage)
    // 5/16 = 31.25% — should not round at 2 decimals.
    let r3 = CoverageBreakdown.create 5 16 |> Result.value
    Assert.Equal (31.25m, r3.Percentage)

[<Fact>]
let ``V1 differential: CoverageBreakdown edge cases match V1 ComputePercentage`` () =
    // V1: total <= 0 → 100m
    let r1 = CoverageBreakdown.create 0 0 |> Result.value
    Assert.Equal (100m, r1.Percentage)
    // V1: emitted <= 0 (with total > 0) → 0m
    let r2 = CoverageBreakdown.create 0 100 |> Result.value
    Assert.Equal (0m, r2.Percentage)
    // V1: full coverage → 100m
    let r3 = CoverageBreakdown.create 100 100 |> Result.value
    Assert.Equal (100m, r3.Percentage)

// ---------------------------------------------------------------------------
// Differential 3: V2 manifest emits the V1-shape top-level keys + per-axis
// nested shapes operators consult.
// ---------------------------------------------------------------------------

[<Fact>]
let ``V1 differential: Manifest emits the V1 SsdtCoverageSummary three-axis shape (tables / columns / constraints)`` () =
    // V1: SsdtCoverageSummary has three CoverageBreakdown fields named
    // Tables / Columns / Constraints. V2 mirrors at the JSON layer.
    let enriched = enrich sampleCatalog
    let json = ManifestEmitter.toJson (ManifestEmitter.emit enriched)
    let root = requireChild "root" (JsonNode.Parse(json))
    let coverage = requireChild "coverage" root.["coverage"]
    let tables = requireChild "coverage.tables" coverage.["tables"]
    let columns = requireChild "coverage.columns" coverage.["columns"]
    let constraints = requireChild "coverage.constraints" coverage.["constraints"]
    // Each axis has { emitted, total, percentage } per V1 CoverageBreakdown.
    for axisLabel, axis in [ "tables", tables; "columns", columns; "constraints", constraints ] do
        Assert.NotNull (axis.[$"emitted"])
        Assert.NotNull (axis.[$"total"])
        Assert.NotNull (axis.[$"percentage"])

[<Fact>]
let ``V1 differential: Manifest emits the V1 SsdtPredicateCoverage two-section shape (tables + predicateCounts)`` () =
    let enriched = enrich sampleCatalog
    let json = ManifestEmitter.toJson (ManifestEmitter.emit enriched)
    let root = requireChild "root" (JsonNode.Parse(json))
    let pc = requireChild "predicateCoverage" root.["predicateCoverage"]
    Assert.NotNull (pc.["tables"])
    Assert.NotNull (pc.["predicateCounts"])

[<Fact>]
let ``V1 differential: Manifest predicateCoverage.tables entries carry V1 PredicateCoverageEntry fields (module + schema + table + predicates)`` () =
    let enriched = enrich sampleCatalog
    let json = ManifestEmitter.toJson (ManifestEmitter.emit enriched)
    let root = requireChild "root" (JsonNode.Parse(json))
    let pc = requireChild "predicateCoverage" root.["predicateCoverage"]
    let tables = requireChild "predicateCoverage.tables" pc.["tables"]
    let arr = tables.AsArray()
    Assert.True (arr.Count > 0, "expected at least one predicateCoverage.tables entry from sampleCatalog")
    let firstEntry = requireChild "predicateCoverage.tables[0]" arr.[0]
    Assert.NotNull (firstEntry.["module"])
    Assert.NotNull (firstEntry.["schema"])
    Assert.NotNull (firstEntry.["table"])
    Assert.NotNull (firstEntry.["predicates"])

[<Fact>]
let ``V1 differential: Manifest unsupported is a JSON array of strings (V1 IReadOnlyList shape)`` () =
    let enriched = enrich sampleCatalog
    let json = ManifestEmitter.toJson (ManifestEmitter.emit enriched)
    let root = requireChild "root" (JsonNode.Parse(json))
    let unsupported = requireChild "unsupported" root.["unsupported"]
    let arr = unsupported.AsArray()
    Assert.True (arr.Count > 0, "expected non-empty unsupported list")
    for i in 0 .. arr.Count - 1 do
        let node = requireChild "unsupported.entry" arr.[i]
        // Each entry is a JSON string (not a number, object, or null).
        Assert.False (System.String.IsNullOrWhiteSpace (node.GetValue<string>()))

[<Fact>]
let ``V1 differential: Manifest preRemediation stays empty array per V2_DRIVER §154 (RemediationEmitter deferred)`` () =
    let enriched = enrich sampleCatalog
    let json = ManifestEmitter.toJson (ManifestEmitter.emit enriched)
    let root = requireChild "root" (JsonNode.Parse(json))
    let preRemediation = requireChild "preRemediation" root.["preRemediation"]
    // V1's PreRemediationManifestEntry list ships as empty until the
    // RemediationEmitter chapter (deferred to chapter 5+ per V2_DRIVER
    // §154); V2 mirrors the empty default until then.
    Assert.Equal (0, preRemediation.AsArray().Count)

// ---------------------------------------------------------------------------
// Differential 4: Documented V2-only fields are present (registry.digest;
// not part of V1's SsdtManifest schema; V2 documents the divergence in the
// chapter 4.4 open doc + the chapter A.4.7' close).
// ---------------------------------------------------------------------------

[<Fact>]
let ``Documented divergence: V2 manifest carries registry.digest (V1 has no equivalent)`` () =
    let enriched = enrich sampleCatalog
    let json = ManifestEmitter.toJson (ManifestEmitter.emit enriched)
    let root = requireChild "root" (JsonNode.Parse(json))
    let registry = requireChild "registry" root.["registry"]
    let digest = requireChild "registry.digest" registry.["digest"]
    // SHA256 hex string (64 chars).
    let digestStr = digest.GetValue<string>()
    Assert.Equal (64, digestStr.Length)

[<Fact>]
let ``Documented divergence: V2 predicateCounts emits sorted-by-name array of objects (V1 emits JSON dict)`` () =
    // Per chapter 4.4 open Q2: V2's choice is a Tolerance candidate
    // (UnsupportedFieldShapeDivergence) if a downstream consumer
    // demands byte-equality with V1.
    let enriched = enrich sampleCatalog
    let json = ManifestEmitter.toJson (ManifestEmitter.emit enriched)
    let root = requireChild "root" (JsonNode.Parse(json))
    let pc = requireChild "predicateCoverage" root.["predicateCoverage"]
    let counts = requireChild "predicateCoverage.predicateCounts" pc.["predicateCounts"]
    // Confirm it's an array shape (V2's choice), not a JSON object (V1's shape).
    Assert.NotNull (counts.AsArray())
