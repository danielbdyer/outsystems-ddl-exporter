module Projection.Tests.AdapterRegistrationsTests

open Xunit
open Projection.Core
open Projection.Adapters.Osm

// ---------------------------------------------------------------------------
// Chapter A.4.7 slice δ — OSSYS adapter rule registration witnesses.
//
// Slice δ ships `CatalogReader.registeredMetadata` — a single
// `RegisteredTransformMetadata` value with Sites enumerating the
// adapter's transformative rules. Per the chapter A.4.7 open's
// pillar-8 deviation: the spec's "every transformative rule gets a
// separate RegisteredTransform entry" pattern is honored via Sites
// (intra-adapter classification fidelity per Q11) rather than via
// N separately-callable function extractions.
//
// All adapter rules classify as DataIntent. The adapter is a
// translation layer; operator opinion enters downstream of the
// adapter (passes that re-apply Selection / Tightening / etc.).
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7 slice δ: CatalogReader.registeredMetadata is at the Adapter stage`` () =
    let rt = CatalogReader.registeredMetadata
    Assert.Equal("ossysCatalogReader", rt.Name)
    Assert.Equal(Schema, rt.Domain)
    Assert.Equal(Adapter, rt.StageBinding)
    Assert.Equal(Active, rt.Status)

[<Fact>]
let ``A.4.7 slice δ: CatalogReader.registeredMetadata enumerates six transformative-rule categories`` () =
    let rt = CatalogReader.registeredMetadata
    let siteNames = rt.Sites |> List.map (fun s -> s.SiteName) |> Set.ofList
    let expected =
        Set.ofList [
            "identitySynthesis"
            "typeTranslation"
            "jsonAggregateParsing"
            "rowsetAggregateParsing"
            "isActiveCarryThrough"
            "tableIdCatalogRead"
        ]
    Assert.Equal<Set<string>>(expected, siteNames)

[<Fact>]
let ``A.4.7 slice δ: every CatalogReader site classifies as DataIntent (adapter is translation, not overlay)`` () =
    let rt = CatalogReader.registeredMetadata
    for site in rt.Sites do
        Assert.Equal(DataIntent, site.Classification)

[<Fact>]
let ``A.4.7 slice δ: every CatalogReader site carries non-empty Rationale (pillar 9 harvest discipline)`` () =
    let rt = CatalogReader.registeredMetadata
    for site in rt.Sites do
        Assert.False (System.String.IsNullOrWhiteSpace site.Rationale)

[<Fact>]
let ``A.4.7 slice δ: CatalogReader.registeredMetadata validates through TransformRegistry.create`` () =
    match TransformRegistry.create [ CatalogReader.registeredMetadata ] with
    | Ok entries -> Assert.Equal(1, List.length entries)
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        Assert.Fail(sprintf "Expected adapter metadata to validate; got errors: %s" codes)
