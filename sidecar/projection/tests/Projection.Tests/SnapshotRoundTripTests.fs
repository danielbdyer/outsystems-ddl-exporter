module Projection.Tests.SnapshotRoundTripTests

open System.IO
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Targets.Json

// ============================================================================
// The bundle's faithful `catalog.snapshot.json` + `Source.ofFile` codec auto-
// detection — the persist→read→diff drift seam. The operator goal: two full-
// export publishes to different dirs, diffed PRECISELY. These prove: a full
// bundle emits a faithful, reloadable snapshot; `Source.ofFile` reads a codec
// snapshot losslessly (and a DIRECTORY resolves the snapshot inside it); a V1
// `osm_model.json` still routes to the V1 reader; and a real model change
// surfaces as the exact `CatalogDiff` channel count.
// ============================================================================

let private nm (s: string) : Name = Name.create s |> Result.value

let private key (n: int) : SsKey =
    SsKey.ossysOriginal (System.Guid(n, 0s, 0s, 0uy, 0uy, 0uy, 0uy, 0uy, 0uy, 0uy, 0uy))

let private tableId (schema: string) (table: string) : TableId =
    TableId.create schema table |> Result.value

let private pkAttr (n: int) : Attribute =
    { Attribute.create (key n) (nm "Id") PrimitiveType.Integer with
        IsPrimaryKey = true
        IsIdentity   = true }

let private kindOf (kindKey: int) (name: string) (attrKey: int) : Kind =
    Kind.create (key kindKey) (nm name) (tableId "dbo" name) [ pkAttr attrKey ]

let private catalogOf (kinds: Kind list) : Catalog =
    let m = { SsKey = key 1000; Name = nm "Sales"; Kinds = kinds; IsActive = true; ExtendedProperties = [] }
    Catalog.create [ m ] [] |> Result.value

let private tempFile (suffix: string) : string =
    Path.Combine(Path.GetTempPath(), sprintf "proj-snap-%s-%s.json" (System.Guid.NewGuid().ToString("N")) suffix)

let private readBack (path: string) : Catalog =
    match TaskSync.run (fun () -> Source.read (Source.ofFile path)) with
    | Ok c -> c
    | Error errs -> failwithf "Source.ofFile read failed: %A" errs

[<Fact>]
let ``snapshot: Source.ofFile reads a CatalogCodec snapshot FAITHFULLY (drifts to zero)`` () =
    let original = catalogOf [ kindOf 1 "Customer" 11 ]
    let path = tempFile "codec"
    File.WriteAllText(path, CatalogCodec.serialize original)
    try
        Assert.True(
            CatalogDiff.between original (readBack path) |> CatalogDiff.isEmpty,
            "a codec snapshot read back through Source.ofFile must drift to zero")
    finally File.Delete path

[<Fact>]
let ``snapshot: Source.ofFile still routes a V1 osm_model.json to the V1 reader (no regression)`` () =
    // No top-level `codecVersion` marker ⇒ the V1 path; the read must still work.
    let v1 = """{ "exportedAtUtc": "2026-05-20T00:00:00.0000000+00:00",
      "modules": [ { "name": "AppCore", "isSystem": false, "isActive": true,
        "entities": [ { "name": "User", "physicalName": "OSUSR_APPCORE_USER",
          "isStatic": false, "isExternal": false, "isActive": true, "db_catalog": null, "db_schema": "dbo",
          "attributes": [ { "name": "Id", "physicalName": "ID", "originalName": null, "dataType": "rtIdentifier",
            "length": null, "precision": null, "scale": null, "default": null, "isMandatory": true,
            "isIdentifier": true, "isAutoNumber": true, "isActive": true, "isReference": 0, "refEntityId": null,
            "refEntity_name": null, "refEntity_physicalName": null, "reference_deleteRuleCode": null,
            "reference_hasDbConstraint": 0, "external_dbType": null, "physical_isPresentButInactive": 0 } ],
          "relationships": [], "indexes": [], "triggers": [] } ] } ] }"""
    let path = tempFile "v1"
    File.WriteAllText(path, v1)
    try Assert.NotEmpty(Catalog.allKinds (readBack path))
    finally File.Delete path

[<Fact>]
let ``snapshot: two faithful snapshots diff PRECISELY — a real model change is the exact channel count`` () =
    let before = catalogOf [ kindOf 1 "Customer" 11 ]
    let after  = catalogOf [ kindOf 1 "Customer" 11; kindOf 2 "Order" 12 ]
    let beforePath, afterPath = tempFile "before", tempFile "after"
    File.WriteAllText(beforePath, CatalogCodec.serialize before)
    File.WriteAllText(afterPath, CatalogCodec.serialize after)
    try
        let d = CatalogDiff.between (readBack beforePath) (readBack afterPath)
        Assert.False(CatalogDiff.isEmpty d)
        Assert.Equal(1, (CatalogDiff.channelCounts d).AddedKinds)
    finally
        for p in [ beforePath; afterPath ] do (try File.Delete p with _ -> ())

[<Fact>]
let ``snapshot: Source.ofFile resolves a publish DIRECTORY to its catalog.snapshot.json`` () =
    // The `diff outA outB` ergonomic: a directory operand resolves the bundle's
    // conventional snapshot file inside it.
    let cat = catalogOf [ kindOf 1 "Customer" 11 ]
    let dir = Path.Combine(Path.GetTempPath(), "proj-snap-dir-" + System.Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory dir |> ignore
    File.WriteAllText(Path.Combine(dir, "catalog.snapshot.json"), CatalogCodec.serialize cat)
    try
        Assert.True(CatalogDiff.between cat (readBack dir) |> CatalogDiff.isEmpty)
    finally Directory.Delete(dir, true)

[<Fact>]
let ``snapshot: a full bundle emission writes a faithful, reloadable catalog.snapshot.json`` () =
    let cat = catalogOf [ kindOf 1 "Customer" 11; kindOf 2 "Order" 12 ]
    match Compose.projectWithConfig Config.defaultConfig cat with
    | Error errs -> failwithf "projectWithConfig failed: %A" errs
    | Ok outputs ->
        Assert.Contains("\"codecVersion\"", outputs.CatalogSnapshot)
        match CatalogCodec.deserialize outputs.CatalogSnapshot with
        | Ok back -> Assert.NotEmpty(Catalog.allKinds back)
        | Error e -> failwithf "the emitted snapshot did not round-trip: %A" e
