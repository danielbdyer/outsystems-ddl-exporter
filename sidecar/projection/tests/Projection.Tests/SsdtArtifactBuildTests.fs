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
              Values = Map.ofList [ mkName "Id", idVal; mkName "Code", code; mkName "Label", label ] }
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
    { Rows = [ { KindKey = roleKey; Identifier = mkKey [ "Sales"; "Role"; "Row"; "Admin" ]; Values = Map.ofList [ mkName "Id", "1"; mkName "Label", "Administrator" ] } ] }

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
