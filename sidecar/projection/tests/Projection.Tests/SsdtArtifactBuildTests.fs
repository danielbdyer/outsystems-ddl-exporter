module Projection.Tests.SsdtArtifactBuildTests

open System
open System.IO
open System.Diagnostics
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Targets.SSDT
open Projection.Targets.Data
open Projection.Tests.Fixtures   // mkName, mkTableId

// ============================================================================
// Capstone: the emitted `.sqlproj` actually BUILDS. `dotnet build` the SDK-style
// Microsoft.Build.Sql project (per-table schema `.sql` + `Script.PostDeployment.sql`
// `:r`-including the data lanes) into a `.dacpac`. This proves the operator's SSDT
// artifact is valid + deployable end-to-end: SSDT parses the schema, resolves the
// post-deploy `:r` includes, and validates the data MERGE T-SQL. Gated: soft-skips
// when the Microsoft.Build.Sql SDK can't be restored (offline / restricted feed).
// ============================================================================

let private mkKey (parts: string list) : SsKey =
    SsKey.synthesizedComposite "OS_BUILD" parts |> Result.value
let private col (p: string) : ColumnRealization =
    ColumnRealization.create p false |> Result.value

let private countryKey = mkKey [ "Sales"; "Country" ]
let private roleKey = mkKey [ "Sales"; "Role" ]

let private buildCatalog () : Catalog =
    let country =
        let row idVal code label =
            { Identifier = mkKey [ "Sales"; "Country"; "Row"; code ]
              Values = StaticRow.presentValues [ mkName "Id", idVal; mkName "Code", code; mkName "Label", label ] }
        { SsKey = countryKey; Name = mkName "Country"; Origin = Native
          Modality = [ Static [ row "1" "US" "United States"; row "2" "CA" "Canada" ] ]
          Physical = mkTableId "dbo" "OSUSR_BLD_COUNTRY"
          Attributes =
            [ { Attribute.create (mkKey [ "Sales"; "Country"; "Id" ]) (mkName "Id") Integer with Column = col "ID"; IsPrimaryKey = true; IsMandatory = true }
              { Attribute.create (mkKey [ "Sales"; "Country"; "Code" ]) (mkName "Code") Text with Column = col "CODE"; IsMandatory = true }
              { Attribute.create (mkKey [ "Sales"; "Country"; "Label" ]) (mkName "Label") Text with Column = col "LABEL"; IsMandatory = true } ]
          References = []; Indexes = []; Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
    let role =
        { SsKey = roleKey; Name = mkName "Role"; Origin = Native; Modality = []
          Physical = mkTableId "dbo" "OSUSR_BLD_ROLE"
          Attributes =
            [ { Attribute.create (mkKey [ "Sales"; "Role"; "Id" ]) (mkName "Id") Integer with Column = col "ID"; IsPrimaryKey = true; IsMandatory = true }
              { Attribute.create (mkKey [ "Sales"; "Role"; "Label" ]) (mkName "Label") Text with Column = col "LABEL"; IsMandatory = true } ]
          References = []; Indexes = []; Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
    { Modules = [ { SsKey = mkKey [ "Sales" ]; Name = mkName "Sales"; Kinds = [ country; role ]; IsActive = true; ExtendedProperties = [] } ]
      Sequences = [] }

let private migrationCtx () : MigrationDependencyContext =
    { Rows = [ { KindKey = roleKey; Identifier = mkKey [ "Sales"; "Role"; "Row"; "Admin" ]; Values = StaticRow.presentValues [ mkName "Id", "1"; mkName "Label", "Administrator" ] } ] }

let private writeFile (dir: string) (rel: string) (body: string) : unit =
    let path = Path.Combine(dir, rel)
    match Path.GetDirectoryName path with
    | null | "" -> ()
    | d -> Directory.CreateDirectory d |> ignore
    File.WriteAllText(path, body)

let private nugetConfig : string =
    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n\
     <configuration>\n\
     \x20\x20<packageSources><clear /><add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" /></packageSources>\n\
     </configuration>\n"

[<Fact>]
let ``Sqlproj build: the emitted Microsoft.Build.Sql project builds to a .dacpac (schema + post-deploy data)`` () =
    let catalog = buildCatalog ()
    let policy = { Policy.empty with Emission = EmissionPolicy.combined }
    let outputs = Compose.projectWith policy Profile.empty catalog
    let bundle =
        match DataEmissionComposer.composeRenderedBundleWithBootstrap policy catalog Profile.empty (migrationCtx ()) Map.empty UserRemapContext.empty with
        | Ok b -> b
        | Error e -> failwithf "data compose failed: %A" e
    let dir = Path.Combine(Path.GetTempPath(), "proj-sqlproj-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory dir |> ignore
    try
        // schema: the per-table .sql — the SDK globs Modules/**/*.sql as Build
        for KeyValue (rel, body) in outputs.SsdtBundle do
            if rel.EndsWith ".sql" then writeFile dir rel body
        // the data lanes the post-deploy :r-includes (static seeds + migration)
        let dataLanes = [ "Data/StaticSeeds.sql", bundle.StaticSeeds; "Data/MigrationData.sql", bundle.MigrationData ]
        for (rel, body) in dataLanes do writeFile dir rel body
        let laneRelPaths = dataLanes |> List.map fst
        writeFile dir PostDeployEmitter.fileName (PostDeployEmitter.renderIncludes laneRelPaths)
        writeFile dir SqlprojEmitter.fileName (SqlprojEmitter.emit laneRelPaths true)
        writeFile dir "nuget.config" nugetConfig
        // dotnet build the emitted .sqlproj → a .dacpac
        let psi = ProcessStartInfo("dotnet", sprintf "build %s -c Debug --nologo" SqlprojEmitter.fileName)
        psi.WorkingDirectory <- dir
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.UseShellExecute <- false
        let proc =
            match Process.Start psi with
            | null -> failwith "could not start `dotnet`"
            | p -> p
        let stdout = proc.StandardOutput.ReadToEnd()
        let stderr = proc.StandardError.ReadToEnd()
        proc.WaitForExit()
        let combined = stdout + "\n" + stderr
        // Gate: a restricted/offline feed can't restore the SDK → skip, never fail.
        if combined.Contains "Could not resolve SDK" || combined.Contains "Unable to resolve 'Microsoft.Build.Sql" then
            printfn "SKIP .sqlproj build: Microsoft.Build.Sql SDK not restorable in this environment."
        else
            Assert.True(proc.ExitCode = 0, sprintf "dotnet build of the emitted .sqlproj failed (exit %d):\n%s" proc.ExitCode combined)
            let dacpac = Path.Combine(dir, "bin", "Debug", "ProjectionCatalog.dacpac")
            Assert.True(File.Exists dacpac, sprintf "expected a built .dacpac at %s\nbuild output:\n%s" dacpac combined)
    finally
        try Directory.Delete(dir, true) with _ -> ()

// ----------------------------------------------------------------------------
// #4 — consume a REAL publish. The test above hand-assembles the dir; this one
// runs the actual operator path (`Compose.runWithConfig` with `emission.sqlproj`
// + an `overrides.migrationDependencies` row file) and `dotnet build`s the bundle
// it drops to disk — proving the WIRED publish output is itself a buildable SSDT
// project (no hand-assembly drift), INCLUDING the post-deploy `:r`-ing a REAL data
// lane (the migration rows the publish rendered to `Data/MigrationData.sql`).
// ----------------------------------------------------------------------------

/// One module / one entity (Role: IDENTITY Id + Label) — a NON-static kind so its
/// rows ride the MIGRATION lane (shared shape with the migration-binding tests).
let private realModelJson : string =
    """{
  "exportedAtUtc": "2026-06-10T00:00:00.0000000+00:00",
  "modules": [
    { "name": "AppCore", "isSystem": false, "isActive": true,
      "entities": [
        { "name": "Role", "physicalName": "OSUSR_APPCORE_ROLE", "isStatic": false, "isExternal": false, "isActive": true, "db_catalog": null, "db_schema": "dbo",
          "attributes": [
            { "name": "Id", "physicalName": "ID", "originalName": null, "dataType": "Identifier", "length": null, "precision": null, "scale": null, "default": null, "isMandatory": true, "isIdentifier": true, "isAutoNumber": true, "isActive": true, "isReference": 0, "refEntityId": null, "refEntity_name": null, "refEntity_physicalName": null, "reference_deleteRuleCode": null, "reference_hasDbConstraint": 0, "external_dbType": null, "physical_isPresentButInactive": 0 },
            { "name": "Label", "physicalName": "LABEL", "originalName": null, "dataType": "Text", "length": 100, "precision": null, "scale": null, "default": null, "isMandatory": true, "isIdentifier": false, "isAutoNumber": false, "isActive": true, "isReference": 0, "refEntityId": null, "refEntity_name": null, "refEntity_physicalName": null, "reference_deleteRuleCode": null, "reference_hasDbConstraint": 0, "external_dbType": null, "physical_isPresentButInactive": 0 } ],
          "relationships": [], "indexes": [], "triggers": [] } ] }
  ]
}"""

/// The `overrides.migrationDependencies` row file — logical `Module.Entity` → rows,
/// the operator-curated MIGRATION lane the publish renders to `Data/MigrationData.sql`.
let private migrationOverrideJson : string =
    """{ "kinds": [ { "module": "AppCore", "entity": "Role",
          "rows": [ { "id": "Admin",   "values": { "Id": "1", "Label": "Administrator" } },
                    { "id": "Auditor", "values": { "Id": "2", "Label": "Auditor" } } ] } ] }"""

[<Fact>]
let ``Sqlproj build: a real emission.sqlproj publish (with migration data) drops a buildable bundle`` () =
    let outDir = Path.Combine(Path.GetTempPath(), "proj-realpub-" + Guid.NewGuid().ToString("N"))
    let modelPath = Path.Combine(Path.GetTempPath(), "proj-realpub-model-" + Guid.NewGuid().ToString("N") + ".json")
    let migPath = Path.Combine(Path.GetTempPath(), "proj-realpub-mig-" + Guid.NewGuid().ToString("N") + ".json")
    File.WriteAllText(modelPath, realModelJson)
    File.WriteAllText(migPath, migrationOverrideJson)
    // Real operator config: schema + the wired .sqlproj + the MIGRATION data lane
    // (fed by the override row file). staticSeeds/bootstrap off so the only data
    // lane is MigrationData; the .json/.txt artifacts the schema glob ignores.
    let cfgJson =
        sprintf
            """{ "model": { "path": "%s" }, "output": { "dir": "%s" }, "overrides": { "migrationDependencies": { "path": "%s" } }, "emission": { "ssdt": true, "sqlproj": true, "migrationDependencies": true, "staticSeeds": false, "bootstrap": false, "json": false, "distributions": false, "opportunities": false, "decisionLog": false, "validations": false } }"""
            (modelPath.Replace("\\", "\\\\"))
            (outDir.Replace("\\", "\\\\"))
            (migPath.Replace("\\", "\\\\"))
    try
        let cfg = Config.parse cfgJson |> Result.value
        let _report = (Compose.runWithConfig cfg).GetAwaiter().GetResult() |> Result.value
        // the WIRE dropped the project + post-deploy + a REAL migration data lane
        Assert.True(File.Exists(Path.Combine(outDir, SqlprojEmitter.fileName)), "the publish wrote ProjectionCatalog.sqlproj")
        Assert.True(File.Exists(Path.Combine(outDir, PostDeployEmitter.fileName)), "the publish wrote Script.PostDeployment.sql")
        Assert.True(File.Exists(Path.Combine(outDir, "Data", "MigrationData.sql")), "the publish rendered the migration rows to Data/MigrationData.sql")
        // build the REAL published bundle — SSDT compiles the schema AND resolves
        // the post-deploy `:r Data/MigrationData.sql`, validating the MERGE T-SQL.
        writeFile outDir "nuget.config" nugetConfig
        let psi = ProcessStartInfo("dotnet", sprintf "build %s -c Debug --nologo" SqlprojEmitter.fileName)
        psi.WorkingDirectory <- outDir
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.UseShellExecute <- false
        let proc =
            match Process.Start psi with
            | null -> failwith "could not start `dotnet`"
            | p -> p
        let combined = proc.StandardOutput.ReadToEnd() + "\n" + proc.StandardError.ReadToEnd()
        proc.WaitForExit()
        if combined.Contains "Could not resolve SDK" || combined.Contains "Unable to resolve 'Microsoft.Build.Sql" then
            printfn "SKIP real-publish .sqlproj build: Microsoft.Build.Sql SDK not restorable here."
        else
            Assert.True(proc.ExitCode = 0, sprintf "dotnet build of the published .sqlproj failed (exit %d):\n%s" proc.ExitCode combined)
            Assert.True(File.Exists(Path.Combine(outDir, "bin", "Debug", "ProjectionCatalog.dacpac")), "the published .sqlproj built a .dacpac")
    finally
        try File.Delete modelPath with _ -> ()
        try File.Delete migPath with _ -> ()
        try if Directory.Exists outDir then Directory.Delete(outDir, true) with _ -> ()
