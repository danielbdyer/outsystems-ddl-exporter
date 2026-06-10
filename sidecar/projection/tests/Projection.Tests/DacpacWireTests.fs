module Projection.Tests.DacpacWireTests

// The dacpac production wire (A-cluster dacpac exposure). `DacpacEmitter`
// shipped with round-trip coverage but had no production caller — the
// `emission.dacpac` config gate parsed and was never honored. These tests
// witness the wire end-to-end through the operator path
// (`Compose.runWithConfig`):
//   1. `emission.dacpac: true` → the run writes `projection.dacpac`, a
//      loadable DacFx package carrying the emitted table.
//   2. The default config writes NO package — the pre-wire bundle is
//      byte-identical (the gate defaults off; opting in is the only way
//      to grow the artifact set).

open System
open System.IO
open Xunit
open Microsoft.SqlServer.Dac.Model
open Projection.Core
open Projection.Pipeline

let private value (r: Result<'a>) : 'a = Result.value r

/// One module / one entity — the smallest catalog the full operator path
/// projects (mirrors the RunWithConfigProfileTests fixture shape).
let private modelJson : string =
    """{
  "exportedAtUtc": "2026-06-10T00:00:00.0000000+00:00",
  "modules": [
    {
      "name": "Packaging",
      "isSystem": false,
      "isActive": true,
      "entities": [
        {
          "name": "Parcel",
          "physicalName": "OSUSR_PKG_PARCEL",
          "isStatic": false,
          "isExternal": false,
          "isActive": true,
          "db_catalog": null,
          "db_schema": "dbo",
          "attributes": [
            { "name": "Id", "physicalName": "ID", "originalName": null, "dataType": "rtIdentifier", "length": null, "precision": null, "scale": null, "default": null, "isMandatory": true, "isIdentifier": true, "isAutoNumber": true, "isActive": true, "isReference": 0, "refEntityId": null, "refEntity_name": null, "refEntity_physicalName": null, "reference_deleteRuleCode": null, "reference_hasDbConstraint": 0, "external_dbType": null, "physical_isPresentButInactive": 0 }
          ],
          "relationships": [],
          "indexes": [],
          "triggers": []
        }
      ]
    }
  ]
}"""

let private writeTemp (suffix: string) (content: string) : string =
    let path = Path.Combine(Path.GetTempPath(), sprintf "projection-dacpacwire-%s-%s" (Guid.NewGuid().ToString "N") suffix)
    File.WriteAllText(path, content)
    path

let private runWith (emissionJson: string option) (assertOn: string list -> string -> unit) : unit =
    let modelPath = writeTemp "model.json" modelJson
    let outDir = Path.Combine(Path.GetTempPath(), sprintf "projection-dacpacwire-out-%s" (Guid.NewGuid().ToString "N"))
    let emissionSection =
        match emissionJson with
        | Some e -> sprintf ", \"emission\": %s" e
        | None -> ""
    let cfgJson =
        sprintf
            """{ "model": { "path": "%s" }, "output": { "dir": "%s" }%s }"""
            (modelPath.Replace("\\", "\\\\"))
            (outDir.Replace("\\", "\\\\"))
            emissionSection
    try
        let cfg = Config.parse cfgJson |> value
        let report = (Compose.runWithConfig cfg).GetAwaiter().GetResult() |> value
        assertOn report.Paths outDir
    finally
        try File.Delete modelPath with _ -> ()
        try if Directory.Exists outDir then Directory.Delete(outDir, true) with _ -> ()

[<Fact>]
let ``emission.dacpac true: the run writes projection.dacpac and the package loads`` () =
    runWith (Some """{ "dacpac": true }""") (fun paths outDir ->
        let dacpacPath = Path.Combine(outDir, Compose.ArtifactPath.dacpac)
        Assert.Contains(dacpacPath, paths)
        Assert.True(File.Exists dacpacPath, "projection.dacpac is on disk")
        // The package is a loadable DacFx model carrying the emitted table
        // (same load discipline as SiblingEmitterContractTests).
        use stream = new MemoryStream(File.ReadAllBytes dacpacPath)
        use model = TSqlModel.LoadFromDacpac(stream, ModelLoadOptions())
        // The package carries the EMITTED catalog — post-`LogicalTableEmission`,
        // so the table is at its logical name (the same shape the SSDT bundle
        // projects), not the OSUSR_* physical source name.
        let tables = model.GetObjects(DacQueryScopes.UserDefined, ModelSchema.Table) |> Seq.toList
        Assert.Contains(tables, fun t -> t.Name.ToString().Contains "Parcel")
    )

[<Fact>]
let ``emission default: no projection.dacpac is written (byte-identical pre-wire bundle)`` () =
    runWith None (fun paths outDir ->
        let dacpacPath = Path.Combine(outDir, Compose.ArtifactPath.dacpac)
        Assert.DoesNotContain(dacpacPath, paths)
        Assert.False(File.Exists dacpacPath, "the default bundle carries no dacpac")
    )
