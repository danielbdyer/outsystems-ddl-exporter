module Twin.Tests.ScenarioCompilerTests

open Xunit
open Projection.Core
open Projection.Tests.Fixtures
open Projection.Tests.IRBuilders
open Twin.Core

// ---------------------------------------------------------------------------
// THE_TWIN.md §7 — the scenario compiler: every field lowers to an engine
// input or refuses by name; a scenario only rewrites evidence, volumes,
// corrections, and pins.
// ---------------------------------------------------------------------------

let private ok (r: Result<'a>) : 'a =
    match r with
    | Ok v -> v
    | Error es -> failwithf "expected success, got: %A" (es |> List.map (fun e -> e.Code, e.Metadata))

let private codes (r: Result<'a>) : string list =
    match r with Ok _ -> [] | Error es -> es |> List.map (fun e -> e.Code)

let private name (s: string) : Name = Name.create s |> Result.value

let private attr (key: SsKey) (logical: string) (ptype: PrimitiveType) (isPk: bool) (nullable: bool) : Attribute =
    { Attribute.create key (name logical) ptype with
        Column       = ColumnRealization.create logical nullable |> Result.value
        IsPrimaryKey = isPk }

let private custKey = kindKey ["C"]
let private ordKey  = kindKey ["O"]

let private customer : Kind =
    { Kind.create custKey (name "Customer") (mkTableId "dbo" "Customer")
        [ attr (attrKey ["C"; "Id"]) "Id" Integer true false
          attr (attrKey ["C"; "Name"]) "Name" Text false false
          attr (attrKey ["C"; "Email"]) "Email" Text false true ] with
        Modality = [] }

let private order : Kind =
    { Kind.create ordKey (name "Order") (mkTableId "dbo" "Order")
        [ attr (attrKey ["O"; "Id"]) "Id" Integer true false
          attr (attrKey ["O"; "CustomerId"]) "CustomerId" Integer false false
          attr (attrKey ["O"; "Channel"]) "Channel" Text false false
          attr (attrKey ["O"; "PlacedOn"]) "PlacedOn" DateTime false false
          attr (attrKey ["O"; "Total"]) "Total" Decimal false false ] with
        References = [ Reference.create (refKey ["O"; "Customer"]) (name "Customer") (attrKey ["O"; "CustomerId"]) custKey ] }

let private index =
    CatalogIndex.ofCatalog
        (Catalog.create [ mkModule (modKey "M") (name "M") [ customer; order ] ] [] |> Result.value)

let private coord (t: string) : TableCoordinate = TableCoordinate.parse t |> Result.value

let private emptyOverride : TableOverride = { Rows = None; Columns = []; PerParent = [] }

let private scenario (nameText: string) (tables: (TableCoordinate * TableOverride) list) (pins: Pin list) : ScenarioIr =
    { Name = nameText; Extends = None; Scale = None; Seed = None; Tables = tables; Pins = pins }

let private defaultVolumeOf (_: SsKey) : int = 100

[<Fact>]
let ``weights compile to a preserved categorical rewrite`` () =
    let s =
        scenario "s"
            [ coord "dbo.Order", { emptyOverride with Columns = [ "Channel", Weights [ "Web", 8; "Store", 2 ] ] } ] []
    let compiled = ok (ScenarioCompiler.compile index defaultVolumeOf [ s ])
    Assert.Contains("Channel", compiled.ForcePreserve)
    let overlaid = compiled.Overlay Profile.empty
    match Profile.tryFindCategorical (attrKey ["O"; "Channel"]) overlaid with
    | Some cat ->
        Assert.Equal(2L, cat.DistinctCount)
        Assert.Equal<Set<string>>(Set.ofList [ "Web"; "Store" ], cat.Frequencies |> List.map fst |> Set.ofList)
    | None -> failwith "the weighted column carries no categorical distribution"

[<Fact>]
let ``weights on a non-text column refuse by name`` () =
    let s =
        scenario "s" [ coord "dbo.Order", { emptyOverride with Columns = [ "Total", Weights [ "1", 1 ] ] } ] []
    Assert.Contains("twin.scenario.weightsOnNonText", codes (ScenarioCompiler.compile index defaultVolumeOf [ s ]))

[<Fact>]
let ``a date window compiles to ordered tick percentiles; skews order correctly`` () =
    let windowed skew =
        let s =
            scenario "s"
                [ coord "dbo.Order", { emptyOverride with Columns = [ "PlacedOn", Between ("2026-01-01", "2026-03-31", skew) ] } ] []
        let compiled = ok (ScenarioCompiler.compile index defaultVolumeOf [ s ])
        match Profile.tryFindNumeric (attrKey ["O"; "PlacedOn"]) (compiled.Overlay Profile.empty) with
        | Some n -> n
        | None -> failwith "no numeric distribution compiled"
    let uniform = windowed SkewUniform
    Assert.True(uniform.Min < uniform.P25 && uniform.P25 < uniform.P50 && uniform.P50 < uniform.P99 && uniform.P99 < uniform.Max)
    Assert.Equal(decimal (System.DateTime(2026, 1, 1).Ticks), uniform.Min)
    Assert.Equal(decimal (System.DateTime(2026, 3, 31).Ticks), uniform.Max)
    let early = windowed SkewEarly
    let late = windowed SkewLate
    Assert.True(early.P50 < uniform.P50, "early skew concentrates mass near the lower bound")
    Assert.True(late.P50 > uniform.P50, "late skew concentrates mass near the upper bound")

[<Fact>]
let ``a window on a text column refuses; an inverted window refuses`` () =
    let onText =
        scenario "s" [ coord "dbo.Order", { emptyOverride with Columns = [ "Channel", Between ("2026-01-01", "2026-02-01", SkewUniform) ] } ] []
    Assert.Contains("twin.scenario.windowOnUnsupported", codes (ScenarioCompiler.compile index defaultVolumeOf [ onText ]))
    let inverted =
        scenario "s" [ coord "dbo.Order", { emptyOverride with Columns = [ "PlacedOn", Between ("2026-03-01", "2026-01-01", SkewUniform) ] } ] []
    Assert.Contains("twin.scenario.windowInverted", codes (ScenarioCompiler.compile index defaultVolumeOf [ inverted ]))

[<Fact>]
let ``a numeric window compiles for a decimal column`` () =
    let s =
        scenario "s" [ coord "dbo.Order", { emptyOverride with Columns = [ "Total", Between ("10", "500", SkewUniform) ] } ] []
    let compiled = ok (ScenarioCompiler.compile index defaultVolumeOf [ s ])
    match Profile.tryFindNumeric (attrKey ["O"; "Total"]) (compiled.Overlay Profile.empty) with
    | Some n -> Assert.Equal(10m, n.Min); Assert.Equal(500m, n.Max)
    | None -> failwith "no numeric distribution compiled"

[<Fact>]
let ``perParent derives the child volume from the parent's resolved rows`` () =
    let s =
        scenario "s"
            [ coord "dbo.Customer", { emptyOverride with Rows = Some 40 }
              coord "dbo.Order", { emptyOverride with PerParent = [ coord "dbo.Customer", 3.5m ] } ] []
    let compiled = ok (ScenarioCompiler.compile index defaultVolumeOf [ s ])
    Assert.Equal(Some (VolumeTarget.Absolute 40), Map.tryFind custKey compiled.Volumes)
    Assert.Equal(Some (VolumeTarget.Absolute 140), Map.tryFind ordKey compiled.Volumes)

[<Fact>]
let ``perParent falls back to the parent's default volume`` () =
    let s = scenario "s" [ coord "dbo.Order", { emptyOverride with PerParent = [ coord "dbo.Customer", 2.0m ] } ] []
    let compiled = ok (ScenarioCompiler.compile index defaultVolumeOf [ s ])
    Assert.Equal(Some (VolumeTarget.Absolute 200), Map.tryFind ordKey compiled.Volumes)

[<Fact>]
let ``perParent naming an unrelated parent refuses`` () =
    // Customer carries no FK to Order.
    let s = scenario "s" [ coord "dbo.Customer", { emptyOverride with PerParent = [ coord "dbo.Order", 2.0m ] } ] []
    Assert.Contains("twin.scenario.perParentNoReference", codes (ScenarioCompiler.compile index defaultVolumeOf [ s ]))

[<Fact>]
let ``rows and perParent on one table refuse as a conflict`` () =
    let s =
        scenario "s"
            [ coord "dbo.Order", { emptyOverride with Rows = Some 10; PerParent = [ coord "dbo.Customer", 2.0m ] } ] []
    Assert.Contains("twin.scenario.volumeConflict", codes (ScenarioCompiler.compile index defaultVolumeOf [ s ]))

// -- Pins -------------------------------------------------------------------

let private pin (table: string) (rows: (string * PinValue) list list) : Pin =
    { Table = coord table; Rows = rows }

[<Fact>]
let ``a valid pin renders per attribute type and exposes its pool key`` () =
    let s =
        scenario "s" []
            [ pin "dbo.Customer" [ [ "Id", PinNumber "1"; "Name", PinText "Canonical"; "Email", PinNull ] ] ]
    let compiled = ok (ScenarioCompiler.compile index defaultVolumeOf [ s ])
    let p = List.exactlyOne compiled.Pins
    Assert.Equal(custKey, p.Kind)
    Assert.Equal<string list>([ "1" ], p.PoolKeys)
    let row = List.exactlyOne p.Rows
    Assert.Equal(Some "Canonical", Map.tryFind (name "Name") row.Values |> Option.flatten)
    // The pinned NULL is present as an explicit None cell.
    Assert.True(Map.containsKey (name "Email") row.Values)
    Assert.True((Map.find (name "Email") row.Values).IsNone)

[<Fact>]
let ``pin refusals: unknown column, missing key, missing mandatory, null on mandatory`` () =
    let unknown = scenario "s" [] [ pin "dbo.Customer" [ [ "Id", PinNumber "1"; "Ghost", PinText "x" ] ] ]
    Assert.Contains("twin.scenario.pinColumnUnknown", codes (ScenarioCompiler.compile index defaultVolumeOf [ unknown ]))
    let noKey = scenario "s" [] [ pin "dbo.Customer" [ [ "Name", PinText "x" ] ] ]
    Assert.Contains("twin.scenario.pinNeedsKey", codes (ScenarioCompiler.compile index defaultVolumeOf [ noKey ]))
    let missingMandatory = scenario "s" [] [ pin "dbo.Customer" [ [ "Id", PinNumber "1" ] ] ]
    Assert.Contains("twin.scenario.pinMissingMandatory", codes (ScenarioCompiler.compile index defaultVolumeOf [ missingMandatory ]))
    let nullMandatory = scenario "s" [] [ pin "dbo.Customer" [ [ "Id", PinNumber "1"; "Name", PinNull ] ] ]
    Assert.Contains("twin.scenario.pinNullOnMandatory", codes (ScenarioCompiler.compile index defaultVolumeOf [ nullMandatory ]))

[<Fact>]
let ``a malformed pin value refuses with the expected shape`` () =
    let bad = scenario "s" [] [ pin "dbo.Order" [ [ "Id", PinNumber "1"; "CustomerId", PinNumber "1"; "Channel", PinText "Web"; "PlacedOn", PinText "soon"; "Total", PinNumber "1" ] ] ]
    Assert.Contains("twin.scenario.pinValueMalformed", codes (ScenarioCompiler.compile index defaultVolumeOf [ bad ]))

[<Fact>]
let ``applyPins: the pin displaces a colliding minted row and lands first`` () =
    let mintedRow (id: string) : StaticRow =
        { Identifier = kindKey [ "R"; id ]
          Values = Map.ofList [ name "Id", Some id; name "Name", Some "minted" ] }
    let dataset = Map.ofList [ custKey, [ mintedRow "1"; mintedRow "2" ] ]
    let s = scenario "s" [] [ pin "dbo.Customer" [ [ "Id", PinNumber "1"; "Name", PinText "Canonical" ] ] ]
    let compiled = ok (ScenarioCompiler.compile index defaultVolumeOf [ s ])
    let merged = ScenarioCompiler.applyPins compiled.Pins dataset
    let rows = merged.[custKey]
    Assert.Equal(2, rows.Length)
    Assert.Equal(Some "Canonical", Map.tryFind (name "Name") rows.Head.Values |> Option.flatten)
    Assert.Equal(Some "2", Map.tryFind (name "Id") rows.[1].Values |> Option.flatten)

// -- Chain merge ------------------------------------------------------------

[<Fact>]
let ``the chain merges base-first with the nearer scenario winning per field`` () =
    let baseScenario =
        { scenario "base"
            [ coord "dbo.Order", { emptyOverride with Rows = Some 10; Columns = [ "Channel", Weights [ "Web", 1 ] ] } ]
            [ pin "dbo.Customer" [ [ "Id", PinNumber "1"; "Name", PinText "FromBase" ] ] ] with Extends = None }
    let child =
        { scenario "child"
            [ coord "dbo.Order", { emptyOverride with Rows = Some 99 } ] [] with Extends = Some "base" }
    let tables, pins = ScenarioCompiler.mergeChain [ baseScenario; child ]
    let _, merged = tables |> List.find (fun (c, _) -> TableCoordinate.text c = "dbo.Order")
    Assert.Equal(Some 99, merged.Rows)
    Assert.Equal(1, List.length merged.Columns)
    Assert.Equal(1, List.length pins)
