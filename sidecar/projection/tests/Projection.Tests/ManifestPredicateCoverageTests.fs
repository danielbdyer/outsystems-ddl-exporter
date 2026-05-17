module Projection.Tests.ManifestPredicateCoverageTests

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

// ---------------------------------------------------------------------------
// Chapter 4.4 slice β — PredicateName closed-DU coverage.
//
// V1's SsdtPredicateNames lists 17 named manifest predicates; V2 lifts
// them as a closed DU. 13 have V2 IR evidence; 4 always emit false
// pending IR refinement (HasFilteredIndex / HasIncludedIndexColumns /
// HasLogicalForeignKeyWithoutDbConstraint /
// HasLogicalForeignKeyWithDbConstraint). The closed-DU empirical-test
// discipline ensures no variant goes unhandled.
// ---------------------------------------------------------------------------

[<Fact>]
let ``PredicateName.all enumerates 16 variants (matches V1 SsdtPredicateNames count)`` () =
    // V1's SsdtPredicateNames.cs declares 16 string constants; V2's
    // closed DU mirrors them 1:1 in canonical sorted order.
    Assert.Equal (16, List.length PredicateName.all)

[<Fact>]
let ``PredicateName.all is alphabetically sorted (canonical order for emit)`` () =
    let names = PredicateName.all |> List.map PredicateName.toString
    let sorted = names |> List.sort
    Assert.Equal<string list> (sorted, names)

[<Fact>]
let ``PredicateName.toString round-trips through PredicateName.all`` () =
    // Every variant in `all` produces a unique non-empty name.
    let names = PredicateName.all |> List.map PredicateName.toString
    Assert.Equal (List.length names, List.length (List.distinct names))
    Assert.All (names, fun n -> Assert.False (System.String.IsNullOrWhiteSpace n))

// ---------------------------------------------------------------------------
// PredicateName.evaluate: per-predicate IR-field consult.
// ---------------------------------------------------------------------------

[<Fact>]
let ``PredicateName.evaluate is deterministic`` () =
    let enriched = enrich sampleCatalog
    for k in Catalog.allKinds enriched do
        for p in PredicateName.all do
            let r1 = PredicateName.evaluate p k
            let r2 = PredicateName.evaluate p k
            Assert.Equal (r1, r2)

// All chapter-4.4 always-false PredicateName variants have been retired
// across chapters 4.5 (α/β) + 4.6 (α). The chapter-4.4 always-false test
// is removed; per-axis cash-out tests in IndexFilterTests +
// IndexIncludedColumnsTests + ReferenceHasDbConstraintTests now witness
// real evaluation for every variant.

[<Fact>]
let ``Chapter 4.5 slice α: HasFilteredIndex returns true when any Index.Filter is Some`` () =
    // Build a small fixture: one kind with a filtered index, one without.
    let mkIdx (filterRaw: string option) =
        {
            SsKey = SsKey.synthesized "test" (sprintf "Idx:%s" (defaultArg filterRaw "Unfiltered")) |> Result.value
            Name = Name.create "IX" |> Result.value
            Columns = []
            IsUnique = false
            IsPrimaryKey = false
            ExtendedProperties = []
            Filter = filterRaw
            IncludedColumns = []
        }
    let mkKindWith (label: string) (idx: Index) : Kind =
        let baseKind =
            IRBuilders.mkKind
                (SsKey.synthesized "test" (sprintf "K:%s" label) |> Result.value)
                (Name.create label |> Result.value)
                (TableId.create "dbo" (sprintf "T_%s" label) |> Result.value)
                []
        { baseKind with Indexes = [idx] }
    let unfiltered = mkKindWith "Unfiltered" (mkIdx None)
    let filtered = mkKindWith "Filtered" (mkIdx (Some "[IsActive] = 1"))
    Assert.False (PredicateName.evaluate PredicateName.HasFilteredIndex unfiltered)
    Assert.True  (PredicateName.evaluate PredicateName.HasFilteredIndex filtered)

// ---------------------------------------------------------------------------
// PredicateCoverage.satisfiedBy: kind-level aggregation.
// ---------------------------------------------------------------------------

[<Fact>]
let ``PredicateCoverage.satisfiedBy returns predicates in canonical sorted order`` () =
    let enriched = enrich sampleCatalog
    for k in Catalog.allKinds enriched do
        let sat = PredicateCoverage.satisfiedBy k
        let satOrder = sat |> List.map PredicateName.toString
        let sorted = satOrder |> List.sort
        Assert.Equal<string list> (sorted, satOrder)

// ---------------------------------------------------------------------------
// PredicateCoverage.compute: catalog-level coverage.
// ---------------------------------------------------------------------------

[<Fact>]
let ``PredicateCoverage.compute: T1 byte-determinism`` () =
    let enriched = enrich sampleCatalog
    let pc1 = PredicateCoverage.compute enriched
    let pc2 = PredicateCoverage.compute enriched
    Assert.Equal<PredicateCoverage> (pc1, pc2)

[<Fact>]
let ``PredicateCoverage.compute: Tables count matches Catalog.allKinds`` () =
    let enriched = enrich sampleCatalog
    let pc = PredicateCoverage.compute enriched
    let allKinds = Catalog.allKinds enriched
    Assert.Equal (List.length allKinds, List.length pc.Tables)

[<Fact>]
let ``PredicateCoverage.compute: PredicateCounts equals per-predicate Tables filtering`` () =
    // Aggregation property: count(predicates containing P) =
    // PredicateCounts[P] for every P.
    let enriched = enrich sampleCatalog
    let pc = PredicateCoverage.compute enriched
    for p in PredicateName.all do
        let countFromTables =
            pc.Tables
            |> List.filter (fun e -> List.contains p e.Predicates)
            |> List.length
        let countFromMap = pc.PredicateCounts |> Map.tryFind p |> Option.defaultValue 0
        Assert.Equal (countFromTables, countFromMap)

[<Fact>]
let ``PredicateCoverage.empty has zero tables and zero counts`` () =
    Assert.Empty PredicateCoverage.empty.Tables
    Assert.Empty PredicateCoverage.empty.PredicateCounts

// ---------------------------------------------------------------------------
// Manifest emission: typed PredicateCoverage flows through to JSON shape.
// ---------------------------------------------------------------------------

let private requireChild (label: string) (n: JsonNode | null) : JsonNode =
    match Option.ofObj n with
    | Some node -> node
    | None      -> Assert.Fail (sprintf "expected %s child" label); Unchecked.defaultof<JsonNode>

[<Fact>]
let ``Manifest predicateCoverage.tables emits one entry per kind`` () =
    let enriched = enrich sampleCatalog
    let json = ManifestEmitter.toJson (ManifestEmitter.emit enriched)
    let root = requireChild "root" (JsonNode.Parse(json))
    let pc = requireChild "predicateCoverage" root.["predicateCoverage"]
    let tables = requireChild "tables" pc.["tables"]
    Assert.Equal (List.length (Catalog.allKinds enriched), tables.AsArray().Count)

[<Fact>]
let ``Manifest predicateCounts emits all 16 predicate variants in canonical sorted order`` () =
    let enriched = enrich sampleCatalog
    let json = ManifestEmitter.toJson (ManifestEmitter.emit enriched)
    let root = requireChild "root" (JsonNode.Parse(json))
    let pc = requireChild "predicateCoverage" root.["predicateCoverage"]
    let counts = requireChild "predicateCounts" pc.["predicateCounts"]
    let countsArr = counts.AsArray()
    Assert.Equal (16, countsArr.Count)
    // Verify sorted-by-name order (matches PredicateName.all order)
    let expectedNames = PredicateName.all |> List.map PredicateName.toString
    let actualNames =
        [ for i in 0 .. countsArr.Count - 1 ->
            let entry = requireChild "predicateCounts.entry" countsArr.[i]
            let nameNode = requireChild "predicateCounts.entry.name" entry.["name"]
            nameNode.GetValue<string>() ]
    Assert.Equal<string list> (expectedNames, actualNames)

[<Fact>]
let ``T1: ManifestEmitter.toJson with PredicateCoverage is byte-deterministic`` () =
    let enriched = enrich sampleCatalog
    let json1 = ManifestEmitter.toJson (ManifestEmitter.emit enriched)
    let json2 = ManifestEmitter.toJson (ManifestEmitter.emit enriched)
    Assert.Equal<string> (json1, json2)
