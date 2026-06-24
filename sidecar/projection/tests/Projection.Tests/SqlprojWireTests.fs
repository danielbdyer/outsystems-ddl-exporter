module Projection.Tests.SqlprojWireTests

// The `.sqlproj` production wire (`emission.sqlproj`). `SqlprojEmitter` /
// `PostDeployEmitter` ship with their own unit coverage and the SSDT deploy/build
// tests prove the artifacts work end-to-end; THESE tests witness the CONFIG WIRE
// through the operator path (`Compose.runWithConfig`):
//   1. `emission.sqlproj: true` → the run grows the bundle with a buildable
//      `ProjectionCatalog.sqlproj` (the SDK-style project), on disk + in the
//      reported paths.
//   2. The default config writes NO project — the pre-wire bundle is
//      byte-identical (the gate defaults off; opting in is the only way to grow
//      the artifact set), mirroring the `emission.dacpac` discipline.

open System
open System.IO
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Targets.SSDT

let private value (r: Result<'a>) : 'a = Result.value r

/// One module / one entity — the smallest catalog the full operator path
/// projects (shared shape with `DacpacWireTests`).
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
    let path = Path.Combine(Path.GetTempPath(), sprintf "projection-sqlprojwire-%s-%s" (Guid.NewGuid().ToString "N") suffix)
    File.WriteAllText(path, content)
    path

let private runWith (emissionJson: string option) (assertOn: string list -> string -> unit) : unit =
    let modelPath = writeTemp "model.json" modelJson
    let outDir = Path.Combine(Path.GetTempPath(), sprintf "projection-sqlprojwire-out-%s" (Guid.NewGuid().ToString "N"))
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
let ``emission.sqlproj true: the run writes a buildable ProjectionCatalog.sqlproj`` () =
    runWith (Some """{ "sqlproj": true }""") (fun paths outDir ->
        let sqlprojPath = Path.Combine(outDir, Compose.ArtifactPath.sqlproj)
        Assert.Contains(sqlprojPath, paths)
        Assert.True(File.Exists sqlprojPath, "ProjectionCatalog.sqlproj is on disk")
        // It is the SDK-style project the emitter produces — same SDK pin the
        // gated `.sqlproj`-build test compiles.
        let xml = File.ReadAllText sqlprojPath
        Assert.Contains("Microsoft.Build.Sql/" + SqlprojEmitter.sdkVersion, xml)
        Assert.Contains(SqlprojEmitter.projectName, xml))

[<Fact>]
let ``emission default: no .sqlproj is written (byte-identical pre-wire bundle)`` () =
    runWith None (fun paths outDir ->
        let sqlprojPath = Path.Combine(outDir, Compose.ArtifactPath.sqlproj)
        Assert.DoesNotContain(sqlprojPath, paths)
        Assert.False(File.Exists sqlprojPath, "the default bundle carries no .sqlproj"))
