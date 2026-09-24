module Projection.Tests.ModelSourceTests

// The file-sourced model for the cutover (operator directive 2026-07-18): the
// single model-file reader (`Compose.read`, the publish path's `readConfigModel`
// file branch dispatches through it) is faithful- and directory-aware, so
// `model.path` accepts a FROZEN `catalog.snapshot.json` (the exact emitted shape
// a full-export wrote) as a first-class model source — the shape the cutover
// pins its `check … --against model` gates against, independent of any live
// connection. An `osm_model.json` still reads through the registered
// `CatalogReader.parse` (byte-identical); a directory resolves to its bundle
// snapshot inside.

open System.IO
open Xunit
open Projection.Core
open Projection.Targets.Json
open Projection.Pipeline

let private nm (s: string) : Name = Name.create s |> Result.value
let private key (n: int) : SsKey =
    SsKey.ossysOriginal (System.Guid(n, 0s, 0s, 0uy, 0uy, 0uy, 0uy, 0uy, 0uy, 0uy, 0uy))
let private tableId (schema: string) (table: string) : TableId = TableId.create schema table |> Result.value

/// A small two-kind catalog to round-trip through the snapshot codec.
let private sampleCatalog : Catalog =
    let kind =
        Kind.create (key 1) (nm "Customer") (tableId "dbo" "OSUSR_CUSTOMER")
            [ Attribute.create (key 2) (nm "Id") PrimitiveType.Integer
              Attribute.create (key 3) (nm "Email") PrimitiveType.Text ]
    let m = { SsKey = key 1000; Name = nm "Sales"; Kinds = [ kind ]; IsActive = true; ExtendedProperties = [] }
    Catalog.create [ m ] [] |> Result.value

let private read (path: string) : Catalog =
    match (Compose.read path).GetAwaiter().GetResult() with
    | Ok c -> c
    | Error es -> failwithf "Compose.read failed: %A" es

let private kindNames (c: Catalog) : string list =
    c.Modules |> List.collect (fun m -> m.Kinds |> List.map (fun k -> Name.value k.Name)) |> List.sort

[<Fact>]
let ``Compose.read: a catalog.snapshot.json (CatalogCodec) reads back as the frozen model`` () =
    let dir = Path.Combine(Path.GetTempPath(), "proj-modelsrc-" + System.Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory dir |> ignore
    try
        let snapshotPath = Path.Combine(dir, Compose.ArtifactPath.catalogSnapshot)
        File.WriteAllText(snapshotPath, CatalogCodec.serialize sampleCatalog)
        // The publish path's file reader now accepts the faithful snapshot.
        let restored = read snapshotPath
        Assert.Equal<string list>([ "Customer" ], kindNames restored)
        // Faithful: the codec round-trip is byte-stable through the read.
        Assert.Equal(CatalogCodec.serialize sampleCatalog, CatalogCodec.serialize restored)
    finally
        if Directory.Exists dir then Directory.Delete(dir, true)

[<Fact>]
let ``Compose.read: a PUBLISH DIRECTORY resolves to its bundle catalog.snapshot.json`` () =
    // `model.path` pointed at a full-export output dir reads the frozen model
    // inside it — the ergonomic cutover form (pin the gate at the publish dir).
    let dir = Path.Combine(Path.GetTempPath(), "proj-modelsrc-dir-" + System.Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory dir |> ignore
    try
        File.WriteAllText(Path.Combine(dir, Compose.ArtifactPath.catalogSnapshot), CatalogCodec.serialize sampleCatalog)
        let restored = read dir
        Assert.Equal<string list>([ "Customer" ], kindNames restored)
    finally
        if Directory.Exists dir then Directory.Delete(dir, true)

[<Fact>]
let ``Config: applyUniquePromotions parses (absent = advise-only default; true = apply)`` () =
    let parse (frag: string) =
        let json =
            sprintf
                """{ "model": { "path": "m.json" }, "output": { "dir": "o" },
                     "policy": { "tightening": { "interventions": [ %s ] } } }"""
                frag
        match Config.parse json with
        | Ok c -> c.Policy.Tightening
        | Error es -> failwithf "config parse failed: %A" es
    let entryOf (t: Config.TighteningSection option) : Config.TighteningInterventionEntry =
        match t with
        | Some s -> List.head s.Interventions
        | None -> failwith "no tightening section parsed"
    // Absent → None (the binder reads this as advise-only).
    let absent = entryOf (parse """{ "kind": "uniqueIndex", "id": "u" }""")
    Assert.Equal<bool option>(None, absent.ApplyUniquePromotions)
    // Explicit opt-in → Some true.
    let applied = entryOf (parse """{ "kind": "uniqueIndex", "id": "u", "applyUniquePromotions": true }""")
    Assert.Equal<bool option>(Some true, applied.ApplyUniquePromotions)
