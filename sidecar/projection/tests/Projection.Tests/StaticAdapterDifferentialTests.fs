module Projection.Tests.StaticAdapterDifferentialTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Adapters.Sql

// ---------------------------------------------------------------------------
// Differential test for the EntitySeedDeterminizer migration.
//
// The contract: V2's StaticAdapter + NormalizeStaticPopulations together
// produce row ordering equivalent to V1's EntitySeedDeterminizer when run
// against the same input. The V1 fixture
// `tests/Fixtures/static-data/static-entities.edge-case.json` is the
// canonical input; its content is embedded below as a string constant
// (hermetic — no file-system dependency, no path resolution). The
// embedded copy is the V2 contract; if the trunk's V1 fixture is ever
// changed, this test fails until the V2 expectation is updated, surfacing
// the divergence as a contract conversation rather than a silent drift.
//
// V1 source: /home/user/outsystems-ddl-exporter/tests/Fixtures/static-data/static-entities.edge-case.json
// V1 schema: { "tables": [ { "schema", "table", "rows": [ {col: value, ...} ] } ] }
// ---------------------------------------------------------------------------

let private v1FixtureContent : string =
    """{
  "tables": [
    {
      "schema": "dbo",
      "table": "OSUSR_DEF_CITY",
      "rows": [
        { "ID": 1, "NAME": "Lisbon", "ISACTIVE": true },
        { "ID": 2, "NAME": "Porto", "ISACTIVE": true },
        { "ID": 3, "NAME": "Madrid", "ISACTIVE": false }
      ]
    }
  ]
}"""

// ---------------------------------------------------------------------------
// V2 catalog template — declares the City kind that the V1 fixture
// populates. The catalog template's job is to provide structural
// metadata (PK, column-to-attribute mapping); the adapter fills in row
// data.
// ---------------------------------------------------------------------------

let private mkKey s = SsKey.original s |> Result.value
let private mkName s = Name.create s |> Result.value

let private cityKey       = mkKey "OS_KIND_City"
let private cityIdKey     = mkKey "OS_ATTR_City_Id"
let private cityNameKey   = mkKey "OS_ATTR_City_Name"
let private cityActiveKey = mkKey "OS_ATTR_City_IsActive"

let private cityKind : Kind =
    { SsKey    = cityKey
      Name     = mkName "City"
      Origin   = OsNative
      Modality = [ Static [] ]   // empty populations; adapter fills these in
      Physical = { Schema = "dbo"; Table = "OSUSR_DEF_CITY" }
      Attributes = [
          { SsKey        = cityIdKey
            Name         = mkName "Id"
            Type         = Integer
            Column       = { ColumnName = "ID"; IsNullable = false }
            IsPrimaryKey = true; IsMandatory = false }
          { SsKey        = cityNameKey
            Name         = mkName "Name"
            Type         = Text
            Column       = { ColumnName = "NAME"; IsNullable = false }
            IsPrimaryKey = false; IsMandatory = false }
          { SsKey        = cityActiveKey
            Name         = mkName "IsActive"
            Type         = Boolean
            Column       = { ColumnName = "ISACTIVE"; IsNullable = false }
            IsPrimaryKey = false; IsMandatory = false }
      ]
      References = []; Indexes = [] }

let private cityCatalog : Catalog =
    { Modules = [
        { SsKey = mkKey "OS_MOD_Cities"
          Name  = mkName "Cities"
          Kinds = [ cityKind ] } ] }

let private extractCityRows (c: Catalog) : StaticRow list =
    Catalog.tryFindKind cityKey c
    |> Option.get
    |> fun k ->
        k.Modality
        |> List.choose (function Static rows -> Some rows | _ -> None)
        |> List.head

// ---------------------------------------------------------------------------
// The contract test — V2's adapter + normalizer produces V1-equivalent
// row ordering.
// ---------------------------------------------------------------------------

[<Fact>]
let ``V1 contract: V1 fixture round-trips through adapter and normalizer`` () =
    // 1. Adapter ingests V1 JSON, attaches populations to the catalog template.
    match Static.attachStaticPopulations cityCatalog v1FixtureContent with
    | Failure errors ->
        Assert.Fail(sprintf "Adapter failed: %A" errors)
    | Success populated ->
        // 2. Normalize the populated catalog.
        let normalized = (NormalizeStaticPopulations.run populated).Value
        // 3. Verify rows are present in PK order — the V1 contract.
        let rows = extractCityRows normalized
        Assert.Equal(3, rows.Length)
        // PK 1 (Lisbon), PK 2 (Porto), PK 3 (Madrid) — V1's deterministic order.
        Assert.Equal("Lisbon", Map.find (mkName "Name") rows.[0].Values)
        Assert.Equal("Porto",  Map.find (mkName "Name") rows.[1].Values)
        Assert.Equal("Madrid", Map.find (mkName "Name") rows.[2].Values)

[<Fact>]
let ``V1 contract: row identifiers derive deterministically from PK values`` () =
    let populated = Static.attachStaticPopulations cityCatalog v1FixtureContent |> Result.value
    let normalized = (NormalizeStaticPopulations.run populated).Value
    let rows = extractCityRows normalized
    // Identifier root-traces back to the parent kind's SsKey + the
    // PK value (per A5 deterministic derivation).
    Assert.Equal("OS_ROW_OS_KIND_City_1", SsKey.rootOriginal rows.[0].Identifier)
    Assert.Equal("OS_ROW_OS_KIND_City_2", SsKey.rootOriginal rows.[1].Identifier)
    Assert.Equal("OS_ROW_OS_KIND_City_3", SsKey.rootOriginal rows.[2].Identifier)

[<Fact>]
let ``V1 contract: non-PK column values cross the boundary as canonical strings`` () =
    let populated = Static.attachStaticPopulations cityCatalog v1FixtureContent |> Result.value
    let rows = extractCityRows populated
    // Booleans coerce to "true"/"false" (V1's invariant-culture form).
    Assert.Equal("true",  Map.find (mkName "IsActive") rows.[0].Values)
    Assert.Equal("true",  Map.find (mkName "IsActive") rows.[1].Values)
    Assert.Equal("false", Map.find (mkName "IsActive") rows.[2].Values)
    // Integers coerce via raw text (matches Convert.ToString(invariantCulture)).
    Assert.Equal("1", Map.find (mkName "Id") rows.[0].Values)
    Assert.Equal("2", Map.find (mkName "Id") rows.[1].Values)
    Assert.Equal("3", Map.find (mkName "Id") rows.[2].Values)

// ---------------------------------------------------------------------------
// The differential heart: rows in the JSON in arbitrary order produce
// the same V2 output as rows in PK order, after normalization. This
// mirrors V1's EntitySeedDeterminizer contract that the unordered
// fetch produces a totally ordered emission.
// ---------------------------------------------------------------------------

let private shuffledFixture : string =
    """{
  "tables": [
    {
      "schema": "dbo",
      "table": "OSUSR_DEF_CITY",
      "rows": [
        { "ID": 3, "NAME": "Madrid", "ISACTIVE": false },
        { "ID": 1, "NAME": "Lisbon", "ISACTIVE": true },
        { "ID": 2, "NAME": "Porto", "ISACTIVE": true }
      ]
    }
  ]
}"""

[<Fact>]
let ``differential: shuffled-input output matches sorted-input output`` () =
    let direct =
        Static.attachStaticPopulations cityCatalog v1FixtureContent
        |> Result.value
        |> NormalizeStaticPopulations.run
        |> fun lineage -> lineage.Value
    let viaShuffled =
        Static.attachStaticPopulations cityCatalog shuffledFixture
        |> Result.value
        |> NormalizeStaticPopulations.run
        |> fun lineage -> lineage.Value
    Assert.Equal(direct, viaShuffled)

// ---------------------------------------------------------------------------
// Adapter validation — the boundary catches malformed inputs and
// surfaces them as Result.Failure, never as exceptions across the seam.
// ---------------------------------------------------------------------------

[<Fact>]
let ``adapter: malformed JSON returns Failure`` () =
    let result = Static.attachStaticPopulations cityCatalog "not json{"
    Assert.True(Result.isFailure result)
    let codes = Result.errors result |> List.map (fun e -> e.Code)
    Assert.Contains("staticAdapter.json.parse", codes)

[<Fact>]
let ``adapter: missing 'tables' returns Failure`` () =
    let result = Static.attachStaticPopulations cityCatalog """{ "other": [] }"""
    Assert.True(Result.isFailure result)
    let codes = Result.errors result |> List.map (fun e -> e.Code)
    Assert.Contains("staticAdapter.json.tables.missing", codes)

[<Fact>]
let ``adapter: 'tables' not an array returns Failure`` () =
    let result = Static.attachStaticPopulations cityCatalog """{ "tables": {} }"""
    Assert.True(Result.isFailure result)
    let codes = Result.errors result |> List.map (fun e -> e.Code)
    Assert.Contains("staticAdapter.json.tables.notArray", codes)

[<Fact>]
let ``adapter: row missing PK column returns Failure`` () =
    let badRow =
        """{ "tables": [
            { "schema": "dbo", "table": "OSUSR_DEF_CITY",
              "rows": [ { "NAME": "Lisbon", "ISACTIVE": true } ] } ] }"""
    let result = Static.attachStaticPopulations cityCatalog badRow
    Assert.True(Result.isFailure result)
    let codes = Result.errors result |> List.map (fun e -> e.Code)
    Assert.Contains("staticAdapter.pk.missing", codes)

[<Fact>]
let ``adapter: kind without IsPrimaryKey returns Failure when populated`` () =
    let pkLessKind =
        { cityKind with
            Attributes =
                cityKind.Attributes
                |> List.map (fun a -> { a with IsPrimaryKey = false; IsMandatory = false }) }
    let pkLessCatalog =
        { cityCatalog with
            Modules =
                cityCatalog.Modules
                |> List.map (fun m -> { m with Kinds = [ pkLessKind ] }) }
    let result = Static.attachStaticPopulations pkLessCatalog v1FixtureContent
    Assert.True(Result.isFailure result)
    let codes = Result.errors result |> List.map (fun e -> e.Code)
    Assert.Contains("staticAdapter.kind.noPk", codes)

// ---------------------------------------------------------------------------
// The adapter is structure-preserving — it never invents kinds, never
// drops kinds, never modifies attributes or references.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A4: adapter neither invents nor drops kind SsKeys`` () =
    let inputKeys =
        Catalog.allKinds cityCatalog
        |> List.map (fun k -> k.SsKey)
        |> Set.ofList
    let outputKeys =
        Static.attachStaticPopulations cityCatalog v1FixtureContent
        |> Result.value
        |> Catalog.allKinds
        |> List.map (fun k -> k.SsKey)
        |> Set.ofList
    Assert.Equal<Set<SsKey>>(inputKeys, outputKeys)

[<Fact>]
let ``adapter: unrelated tables in JSON pass through silently`` () =
    // The JSON has an OSUSR_DEF_OTHER table not represented in the
    // catalog — the adapter ignores it (the catalog's selection is
    // the contract, not the JSON's).
    let extraRows =
        """{ "tables": [
            { "schema": "dbo", "table": "OSUSR_DEF_CITY",
              "rows": [
                { "ID": 1, "NAME": "Lisbon", "ISACTIVE": true },
                { "ID": 2, "NAME": "Porto", "ISACTIVE": true },
                { "ID": 3, "NAME": "Madrid", "ISACTIVE": false } ] },
            { "schema": "dbo", "table": "OSUSR_DEF_OTHER",
              "rows": [ { "ID": 99, "NAME": "Ignored" } ] } ] }"""
    let result = Static.attachStaticPopulations cityCatalog extraRows
    Assert.True(Result.isSuccess result)
    let rows = extractCityRows (Result.value result)
    Assert.Equal(3, rows.Length)

// ---------------------------------------------------------------------------
// Determinism (T1) — same JSON input, same V2 output, byte-identical.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: adapter is deterministic`` () =
    let r1 = Static.attachStaticPopulations cityCatalog v1FixtureContent |> Result.value
    let r2 = Static.attachStaticPopulations cityCatalog v1FixtureContent |> Result.value
    Assert.Equal(r1, r2)
