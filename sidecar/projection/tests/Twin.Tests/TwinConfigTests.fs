module Twin.Tests.TwinConfigTests

open Xunit
open Projection.Core
open Twin.Core

// ---------------------------------------------------------------------------
// THE_TWIN.md §config — the four surface laws, executable:
//   closed schema; located refusals; secret-free (D9); collision-free
//   sources (law 4). Plus scenario extends resolution and the canonical
//   renderings the fingerprint rides on.
// ---------------------------------------------------------------------------

let private ok (r: Result<'a>) : 'a =
    match r with
    | Ok v -> v
    | Error es -> failwithf "expected success, got: %A" (es |> List.map (fun e -> e.Code, e.Metadata |> Map.tryFind "path"))

let private codes (r: Result<'a>) : string list =
    match r with
    | Ok _ -> []
    | Error es -> es |> List.map (fun e -> e.Code)

let private pathOf (code: string) (r: Result<'a>) : string option =
    match r with
    | Ok _ -> None
    | Error es ->
        es
        |> List.tryFind (fun e -> e.Code = code)
        |> Option.bind (fun e -> e.Metadata |> Map.tryFind "path" |> Option.flatten)

let private minimal = """{ "estate": { "tables": "Modules/**/*.sql" } }"""

[<Fact>]
let ``a minimal config parses with documented defaults`` () =
    let c = ok (TwinConfig.parse minimal)
    Assert.Equal("Modules/**/*.sql", c.Estate.TablesPattern)
    Assert.Equal(TwinConfig.DefaultContainerName, c.Container.Name)
    Assert.Equal(TwinConfig.DefaultPort, c.Container.Port)
    Assert.Equal(TwinConfig.DefaultImage, c.Container.Image)
    Assert.Equal(1UL, c.Seed)
    Assert.Equal(1.0m, c.Scale)
    Assert.Equal(TwinConfig.DefaultRowsPerKind, c.DefaultRows)
    Assert.Equal(FlatVolumes, c.Volumes)
    Assert.Empty c.Scenarios

[<Fact>]
let ``closed schema: an unknown key refuses, named by its path`` () =
    let r = TwinConfig.parse """{ "estate": { "tables": "T/*.sql" }, "containr": {} }"""
    Assert.Contains("twin.config.unknownKey", codes r)
    Assert.Equal(Some "$.containr", pathOf "twin.config.unknownKey" r)

[<Fact>]
let ``closed schema holds inside nested sections`` () =
    let r = TwinConfig.parse """{ "estate": { "tables": "T/*.sql", "tabels": "x" } }"""
    Assert.Equal(Some "$.estate.tabels", pathOf "twin.config.unknownKey" r)

[<Fact>]
let ``a missing estate section refuses with the expected shape`` () =
    let r = TwinConfig.parse """{ }"""
    Assert.Contains("twin.config.required", codes r)
    Assert.Equal(Some "$.estate", pathOf "twin.config.required" r)

[<Fact>]
let ``errors aggregate: one pass reports every problem`` () =
    let r =
        TwinConfig.parse
            """{ "estate": { "tables": "T/*.sql" }, "container": { "port": 99999 }, "seed": -1, "unknown": 1 }"""
    let cs = codes r
    Assert.Contains("twin.config.range", cs)
    Assert.Contains("twin.config.unknownKey", cs)
    Assert.True(List.length cs >= 3)

[<Fact>]
let ``D9: an inline connection secret refuses at parse`` () =
    let r =
        TwinConfig.parse
            """{ "estate": { "tables": "T/*.sql" },
                 "evidence": { "sources": [ { "name": "uat", "rendition": "logical",
                   "conn": "Server=prod;User Id=sa;Password=hunter2", "tables": ["dbo.Customer"] } ] } }"""
    Assert.Contains("twin.config.secretInline", codes r)

[<Fact>]
let ``D9: an inline container password refuses at parse`` () =
    let r =
        TwinConfig.parse
            """{ "estate": { "tables": "T/*.sql" }, "container": { "password": "hunter2" } }"""
    Assert.Contains("twin.config.secretInline", codes r)

[<Fact>]
let ``law 4: a table claimed by two sources refuses naming both`` () =
    let r =
        TwinConfig.parse
            """{ "estate": { "tables": "T/*.sql" },
                 "evidence": { "sources": [
                   { "name": "uat",   "rendition": "logical",  "conn": "env:A", "tables": ["dbo.Customer"] },
                   { "name": "cloud", "rendition": "physical", "conn": "env:B", "tables": ["DBO.CUSTOMER"] } ] } }"""
    match r with
    | Ok _ -> failwith "expected the collision refusal"
    | Error es ->
        let collision = es |> List.find (fun e -> e.Code = "twin.config.evidence.collision")
        Assert.Equal(Some "dbo.Customer", collision.Metadata |> Map.tryFind "table" |> Option.flatten)
        let sources = collision.Metadata |> Map.tryFind "sources" |> Option.flatten |> Option.defaultValue ""
        Assert.Contains("uat", sources)
        Assert.Contains("cloud", sources)

[<Fact>]
let ``an unknown rendition refuses with the expected tokens`` () =
    let r =
        TwinConfig.parse
            """{ "estate": { "tables": "T/*.sql" },
                 "evidence": { "sources": [ { "name": "uat", "rendition": "cloudy", "conn": "env:A", "tables": ["dbo.C"] } ] } }"""
    Assert.Contains("twin.config.type", codes r)

[<Fact>]
let ``a scenario parses with tables, columns, perParent, and pins`` () =
    let json =
        """{ "estate": { "tables": "T/*.sql" },
             "scenarios": {
               "default": {},
               "quarter-end": {
                 "extends": "default",
                 "tables": {
                   "dbo.Order": { "rows": 500, "columns": { "Status": { "weights": { "Open": 7, "Closed": 3 } },
                                                            "CreatedOn": { "between": ["2026-01-01", "2026-03-31"], "skew": "late" } } },
                   "dbo.OrderLine": { "perParent": { "dbo.Order": { "mean": 3.5 } } } },
                 "pins": [ { "table": "dbo.Customer", "rows": [ { "Id": 1, "Name": "Canonical", "Vip": true, "Notes": null } ] } ] } } }"""
    let c = ok (TwinConfig.parse json)
    let scenario = c.Scenarios |> List.find (fun (n, _) -> n = "quarter-end") |> snd
    Assert.Equal(Some "default", scenario.Extends)
    let orderOverride = scenario.Tables |> List.find (fun (t, _) -> TableCoordinate.text t = "dbo.Order") |> snd
    Assert.Equal(Some 500, orderOverride.Rows)
    Assert.Equal(2, List.length orderOverride.Columns)
    let lineOverride = scenario.Tables |> List.find (fun (t, _) -> TableCoordinate.text t = "dbo.OrderLine") |> snd
    Assert.Equal(1, List.length lineOverride.PerParent)
    Assert.Equal(1, List.length scenario.Pins)
    let pin = List.head scenario.Pins
    Assert.Equal(4, pin.Rows |> List.head |> List.length)

[<Fact>]
let ``a column override with both weights and between refuses`` () =
    let r =
        TwinConfig.parse
            """{ "estate": { "tables": "T/*.sql" },
                 "scenarios": { "s": { "tables": { "dbo.T": { "columns": { "C": { "weights": { "A": 1 }, "between": ["2026-01-01", "2026-02-01"] } } } } } } }"""
    Assert.Contains("twin.config.scenario.overrideConflict", codes r)

[<Fact>]
let ``skew without between refuses`` () =
    let r =
        TwinConfig.parse
            """{ "estate": { "tables": "T/*.sql" },
                 "scenarios": { "s": { "tables": { "dbo.T": { "columns": { "C": { "skew": "late" } } } } } } }"""
    // The override carries neither weights nor between AND a skew; the
    // located refusal fires on the missing pair first.
    Assert.NotEmpty (codes r)

[<Fact>]
let ``extends naming an undefined scenario refuses`` () =
    let r =
        TwinConfig.parse
            """{ "estate": { "tables": "T/*.sql" }, "scenarios": { "s": { "extends": "nope" } } }"""
    Assert.Contains("twin.config.scenario.extendsUnknown", codes r)

[<Fact>]
let ``an extends cycle refuses`` () =
    let r =
        TwinConfig.parse
            """{ "estate": { "tables": "T/*.sql" },
                 "scenarios": { "a": { "extends": "b" }, "b": { "extends": "a" } } }"""
    Assert.Contains("twin.config.scenario.extendsCycle", codes r)

[<Fact>]
let ``resolveScenario: the baseline is optional, a named absence refuses`` () =
    let c = ok (TwinConfig.parse minimal)
    Assert.Equal(Ok None, TwinConfig.resolveScenario c TwinConfig.BaselineScenario)
    Assert.Contains("twin.scenario.unknown", codes (TwinConfig.resolveScenario c "quarter-end"))

[<Fact>]
let ``malformed JSON refuses with the syntax code`` () =
    Assert.Contains("twin.config.json", codes (TwinConfig.parse "{ not json"))

// -- Canonical renderings (the fingerprint's config contributions) ----------

let private full =
    """{ "estate": { "tables": "Modules/**/*.sql", "staticData": ["Data/Seeds.sql"] },
         "seed": 7, "scale": 0.5, "defaultRows": 50,
         "scenarios": { "default": {}, "qe": { "extends": "default", "tables": { "dbo.Order": { "rows": 10 } } } } }"""

[<Fact>]
let ``canonical renderings are deterministic`` () =
    let a = ok (TwinConfig.parse full)
    let b = ok (TwinConfig.parse full)
    Assert.Equal(TwinConfig.canonicalEstate a, TwinConfig.canonicalEstate b)
    Assert.Equal(TwinConfig.canonicalMint a "qe", TwinConfig.canonicalMint b "qe")

[<Fact>]
let ``an inherited scenario edit changes the descendant's canonical mint`` () =
    let a = ok (TwinConfig.parse full)
    let edited =
        full.Replace("\"default\": {}", "\"default\": { \"scale\": 2.0 }")
    let b = ok (TwinConfig.parse edited)
    Assert.NotEqual<string>(TwinConfig.canonicalMint a "qe", TwinConfig.canonicalMint b "qe")

[<Fact>]
let ``scenarioChain walks base-first`` () =
    let c = ok (TwinConfig.parse full)
    let chain = TwinConfig.scenarioChain c "qe" |> List.map (fun s -> s.Name)
    Assert.Equal<string list>([ "default"; "qe" ], chain)

[<Fact>]
let ``a merge section parses with inputs, report, and witness`` () =
    let c =
        ok
            (TwinConfig.parse
                """{ "estate": { "tables": "T/*.sql" },
                     "evidence": { "rich": "file:../secure/merged.rich.json",
                       "merge": { "inputs": [ "file:../secure/dev.rich.json", "file:../secure/qa.rich.json" ],
                                  "report": "twin/evidence-merge.report.json",
                                  "witness": "file:../secure/witness" } } }""")
    match c.Evidence.Merge with
    | None -> failwith "the merge section did not parse"
    | Some m ->
        Assert.Equal(2, List.length m.Inputs)
        Assert.Equal(Some "twin/evidence-merge.report.json", m.ReportPath)
        Assert.Equal(Some "file:../secure/witness", m.WitnessPath)

[<Fact>]
let ``a merge section with no inputs refuses by path`` () =
    let r =
        TwinConfig.parse
            """{ "estate": { "tables": "T/*.sql" },
                 "evidence": { "merge": { "inputs": [] } } }"""
    Assert.Contains("twin.config.evidence.merge.inputsEmpty", codes r)
    Assert.Equal(Some "$.evidence.merge.inputs", pathOf "twin.config.evidence.merge.inputsEmpty" r)

[<Fact>]
let ``the merge section is closed-schema`` () =
    let r =
        TwinConfig.parse
            """{ "estate": { "tables": "T/*.sql" },
                 "evidence": { "merge": { "inputs": ["a.json"], "reprot": "x" } } }"""
    Assert.Equal(Some "$.evidence.merge.reprot", pathOf "twin.config.unknownKey" r)

// -- The existing-server substrate (the C8 seam) ----------------------------

[<Fact>]
let ``a server section parses, and the substrate flips the schema-plane identity`` () =
    let c =
        ok
            (TwinConfig.parse
                """{ "estate": { "tables": "T/*.sql" },
                     "server": { "conn": "env:TWIN_SERVER_CONN", "database": "ProvingTwin" } }""")
    match c.Server with
    | None -> failwith "the server section did not parse"
    | Some s ->
        Assert.Equal("env:TWIN_SERVER_CONN", s.ConnRef)
        Assert.Equal("ProvingTwin", s.Database)
    // The container defaults stay populated but inert; the canonical
    // estate rendering carries the external marker, so the same estate on
    // the two substrates converges independently.
    let managed = ok (TwinConfig.parse """{ "estate": { "tables": "T/*.sql" } }""")
    Assert.Contains("external:ProvingTwin", TwinConfig.canonicalEstate c)
    Assert.NotEqual<string>(TwinConfig.canonicalEstate managed, TwinConfig.canonicalEstate c)

[<Fact>]
let ``the server database defaults to the managed twin's name`` () =
    let c =
        ok
            (TwinConfig.parse
                """{ "estate": { "tables": "T/*.sql" }, "server": { "conn": "file:.twin/server.conn" } }""")
    Assert.Equal(Some TwinConfig.DefaultServerDatabase, c.Server |> Option.map (fun s -> s.Database))

[<Fact>]
let ``an inline server connection refuses (D9)`` () =
    let r =
        TwinConfig.parse
            """{ "estate": { "tables": "T/*.sql" },
                 "server": { "conn": "Server=localhost;User Id=sa;Password=x" } }"""
    Assert.Contains("twin.config.secretInline", codes r)
    Assert.Equal(Some "$.server.conn", pathOf "twin.config.secretInline" r)

[<Fact>]
let ``a server section without a conn refuses by path`` () =
    let r =
        TwinConfig.parse
            """{ "estate": { "tables": "T/*.sql" }, "server": { "database": "x" } }"""
    Assert.Contains("twin.config.required", codes r)
    Assert.Equal(Some "$.server.conn", pathOf "twin.config.required" r)

[<Fact>]
let ``naming both substrates refuses`` () =
    let r =
        TwinConfig.parse
            """{ "estate": { "tables": "T/*.sql" },
                 "container": { "name": "twin-x" },
                 "server": { "conn": "env:TWIN_SERVER_CONN" } }"""
    Assert.Contains("twin.config.substrate.both", codes r)

[<Fact>]
let ``the server section is closed-schema`` () =
    let r =
        TwinConfig.parse
            """{ "estate": { "tables": "T/*.sql" },
                 "server": { "conn": "env:V", "databse": "x" } }"""
    Assert.Equal(Some "$.server.databse", pathOf "twin.config.unknownKey" r)
