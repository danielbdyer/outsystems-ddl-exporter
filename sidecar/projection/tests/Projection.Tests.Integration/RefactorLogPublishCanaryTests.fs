module Projection.Tests.RefactorLogPublishCanaryTests

// G3 (DECISIONS 2026-07-16) — THE DacFx-grade witness the refactorlog wiring
// was missing: an INCREMENTAL publish of a renamed table, with the bundle's
// accumulated `ProjectionCatalog.refactorlog` built into the dacpac, applies
// `sp_rename` (the data survives) instead of DROP+CREATE (the data is lost).
// The trap is armed twice: DacFx's default `BlockOnPossibleDataLoss = true`
// FAILS the publish outright on a drop plan, and the row asserts catch a
// silent recreate. The canary drives the operator's REAL path end-to-end:
//
//   full-export --lifecycle-store  (bundle + ProjectionCatalog.refactorlog
//                                   + the `.sqlproj` RefactorLog item)
//   → dotnet build                 (SqlBuildTask embeds refactor.xml)
//   → DacServices.Deploy           (incremental upgrade of the live estate)
//   → sp_rename                    (rows intact under the NEW table name)
//
// Identity note: file-sourced models synthesize SsKeys from names (the
// 6.A.7 limitation), so the rename threads through the STORE — the prior
// episode is seeded with the run-B key under the OLD logical name, exactly
// what an identity-stable (live-OSSYS) source records across a dev rename.
//
// Gated twice: soft-skips without Docker (collection rule) and without a
// restorable Microsoft.Build.Sql SDK (offline / restricted nuget feed).

open System
open System.Diagnostics
open System.IO
open Xunit
open Microsoft.Data.SqlClient
open Microsoft.SqlServer.Dac
open Projection.Core
open Projection.Pipeline
open Projection.Targets.SSDT

[<RequireQualifiedAccess>]
module private RefactorCanary =

    let value (r: Result<'a>) : 'a = Result.value r

    let skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else printfn "SKIP %s: Docker daemon not reachable." label; false

    /// AppCore/User — Id (IDENTITY PK) + Email. The BEFORE model.
    let modelUser : string =
        """{
  "exportedAtUtc": "2026-07-16T00:00:00.0000000+00:00",
  "modules": [
    { "name": "AppCore", "isSystem": false, "isActive": true,
      "entities": [
        { "name": "User", "physicalName": "OSUSR_APPCORE_USER", "isStatic": false, "isExternal": false, "isActive": true, "db_catalog": null, "db_schema": "dbo",
          "attributes": [
            { "name": "Id", "physicalName": "ID", "originalName": null, "dataType": "rtIdentifier", "length": null, "precision": null, "scale": null, "default": null, "isMandatory": true, "isIdentifier": true, "isAutoNumber": true, "isActive": true, "isReference": 0, "refEntityId": null, "refEntity_name": null, "refEntity_physicalName": null, "reference_deleteRuleCode": null, "reference_hasDbConstraint": 0, "external_dbType": null, "physical_isPresentButInactive": 0 },
            { "name": "Email", "physicalName": "EMAIL", "originalName": null, "dataType": "rtText", "length": 200, "precision": null, "scale": null, "default": null, "isMandatory": false, "isIdentifier": false, "isAutoNumber": false, "isActive": true, "isReference": 0, "refEntityId": null, "refEntity_name": null, "refEntity_physicalName": null, "reference_deleteRuleCode": null, "reference_hasDbConstraint": 0, "external_dbType": null, "physical_isPresentButInactive": 0 } ],
          "relationships": [], "indexes": [], "triggers": [] } ] }
  ]
}"""

    /// The AFTER model: the entity renamed `User` → `Client` (the physical
    /// table name is untouched — an OutSystems dev rename never moves the
    /// OSUSR table; the DEPLOYED table moves because V2 emits logical names).
    let modelClient : string =
        modelUser.Replace("\"name\": \"User\"", "\"name\": \"Client\"")

    let writeTemp (suffix: string) (content: string) : string =
        let path = Path.Combine(Path.GetTempPath(), sprintf "refactor-canary-%s-%s" (Guid.NewGuid().ToString "N") suffix)
        File.WriteAllText(path, content)
        path

    let configJson (modelPath: string) (outDir: string) : string =
        sprintf
            """{ "model": { "path": "%s" }, "output": { "dir": "%s" }, "emission": { "sqlproj": true } }"""
            (modelPath.Replace("\\", "\\\\"))
            (outDir.Replace("\\", "\\\\"))

    let nugetConfig : string =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n\
         <configuration>\n\
         \x20\x20<packageSources><clear /><add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" /></packageSources>\n\
         </configuration>\n"

    /// `dotnet build` the published bundle at `dir` into a `.dacpac`.
    /// `Ok path` on success; `Error reason` when the SDK cannot restore
    /// (the offline soft-skip the pure build tests established).
    let buildDacpac (dir: string) : Microsoft.FSharp.Core.Result<string, string> =
        File.WriteAllText(Path.Combine(dir, "nuget.config"), nugetConfig)
        let psi = ProcessStartInfo("dotnet", sprintf "build %s -c Debug --nologo" SqlprojEmitter.fileName)
        psi.WorkingDirectory <- dir
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
            Microsoft.FSharp.Core.Result.Error "Microsoft.Build.Sql SDK not restorable in this environment."
        elif proc.ExitCode <> 0 then
            failwithf "dotnet build of the published .sqlproj failed (exit %d):\n%s" proc.ExitCode combined
        else
            let dacpac = Path.Combine(dir, "bin", "Debug", "ProjectionCatalog.dacpac")
            Assert.True(File.Exists dacpac, sprintf "expected a built .dacpac at %s\n%s" dacpac combined)
            Microsoft.FSharp.Core.Result.Ok dacpac

    let publishDacpac (connStr: string) (dacpacPath: string) : unit =
        let dbName = SqlConnectionStringBuilder(connStr).InitialCatalog
        use stream = File.OpenRead dacpacPath
        use package = DacPackage.Load stream
        let services = DacServices(connStr)
        // Default DacDeployOptions: BlockOnPossibleDataLoss = true — the
        // publish itself REFUSES a table-drop plan, so reaching the row
        // asserts already means no DROP+CREATE was planned.
        services.Deploy(package, dbName, true, DacDeployOptions())

    let scalarInt (cnn: SqlConnection) (sql: string) : Threading.Tasks.Task<int> =
        task {
            use cmd = new SqlCommand(sql, cnn)
            let! v = cmd.ExecuteScalarAsync()
            return
                match v with
                | :? int as i -> i
                | :? System.DBNull | null -> 0
                | other -> System.Convert.ToInt32 other
        }

    /// The prior episode's schema, hand-authored with run-B's synthesized
    /// SsKeys (module `AppCore`, entity `Client`) under the OLD logical
    /// name `User` — the store is the identity carrier across the rename.
    let priorSchemaUserUnderClientKeys () : Catalog =
        let kindKey = SsKey.synthesizedComposite "OS_KIND" [ "AppCore"; "Client" ] |> value
        let idKey = SsKey.synthesizedComposite "OS_ATTR" [ "AppCore"; "Client"; "Id" ] |> value
        let emailKey = SsKey.synthesizedComposite "OS_ATTR" [ "AppCore"; "Client"; "Email" ] |> value
        let moduleKey = SsKey.synthesized "OS_MOD" "AppCore" |> value
        let nameOf (s: string) : Name = Name.create s |> value
        let idAttr =
            { Attribute.create idKey (nameOf "Id") Integer with
                Column = ColumnRealization.create "ID" false |> Result.value
                IsPrimaryKey = true
                IsMandatory = true
                IsIdentity = true }
        let emailAttr =
            { Attribute.create emailKey (nameOf "Email") Text with
                Column = ColumnRealization.create "EMAIL" true |> Result.value
                Length = Some 200 }
        let kind =
            Kind.create kindKey (nameOf "User")
                (TableId.create "dbo" "OSUSR_APPCORE_USER" |> value)
                [ idAttr; emailAttr ]
        { Modules =
            [ { SsKey = moduleKey; Name = nameOf "AppCore"; Kinds = [ kind ]; IsActive = true; ExtendedProperties = [] } ]
          Sequences = [] }

[<Xunit.Collection("Docker-SqlServer")>]
type RefactorLogPublishCanaryTests(fixture: EphemeralContainerFixture) =
    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``G3 canary: an incremental publish with the bundle refactorlog RENAMES (sp_rename) — the rows survive under the new table name`` () =
        if not (RefactorCanary.skipIfNoDocker "RefactorLogPublish") then () else
        let outA = Path.Combine(Path.GetTempPath(), "refactor-canary-outA-" + Guid.NewGuid().ToString("N"))
        let outB = Path.Combine(Path.GetTempPath(), "refactor-canary-outB-" + Guid.NewGuid().ToString("N"))
        let modelA = RefactorCanary.writeTemp "user.json" RefactorCanary.modelUser
        let modelB = RefactorCanary.writeTemp "client.json" RefactorCanary.modelClient
        let cfgA = RefactorCanary.writeTemp "cfgA.json" (RefactorCanary.configJson modelA outA)
        let cfgB = RefactorCanary.writeTemp "cfgB.json" (RefactorCanary.configJson modelB outB)
        let store = Path.Combine(Path.GetTempPath(), "refactor-canary-store-" + Guid.NewGuid().ToString("N") + ".json")
        try
            // ---- Emission A (genesis; store-less): the BEFORE estate. ----
            match FullExportRun.execute cfgA None LogSink.Verbosity.Quiet Set.empty with
            | FullExportRun.RunOutcome.Succeeded _ -> ()
            | other -> failwithf "publish A failed: %A" other
            // ---- Seed the prior: run-B identity under the OLD name. ----
            let tl = Timeline.create "appcore" |> RefactorCanary.value
            let coordinate =
                EpisodeCoordinate.create
                    (Version.create 0 "v0" |> RefactorCanary.value)
                    Environment.Dev
                    (DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.Zero))
            let genesis =
                Episode.create coordinate (RefactorCanary.priorSchemaUserUnderClientKeys ()) Profile.empty None DataObservation.empty
            (match LifecycleStore.save store (EpisodicLifecycle.genesis tl genesis) with
             | Microsoft.FSharp.Core.Result.Ok () -> ()
             | Microsoft.FSharp.Core.Result.Error e -> failwithf "store seed failed: %A" e)
            // ---- Emission B (store-threaded): the rename + its refactorlog. ----
            let outcomeB, legB =
                FullExportRun.executeWithStore
                    cfgB None LogSink.Verbosity.Quiet Set.empty
                    (Some store) tl Environment.Dev
                    (DateTimeOffset(2026, 7, 16, 9, 0, 0, TimeSpan.Zero))
            (match outcomeB with
             | FullExportRun.RunOutcome.Succeeded _ -> ()
             | other -> failwithf "publish B failed: %A" other)
            let leg = match legB with Some l -> l | None -> failwith "store threaded but no leg"
            Assert.Equal(1, List.length leg.AccumulatedRefactorLog)
            let refactorPath = Path.Combine(outB, Compose.ArtifactPath.refactorLog)
            Assert.True(File.Exists refactorPath, "bundle B carries ProjectionCatalog.refactorlog")
            Assert.Contains("Value=\"[dbo].[User]\"", File.ReadAllText refactorPath)
            // ---- Build both bundles (SqlBuildTask embeds refactor.xml). ----
            match RefactorCanary.buildDacpac outA, RefactorCanary.buildDacpac outB with
            | Microsoft.FSharp.Core.Result.Error reason, _
            | _, Microsoft.FSharp.Core.Result.Error reason ->
                printfn "SKIP RefactorLogPublish canary: %s" reason
            | Microsoft.FSharp.Core.Result.Ok dacpacA, Microsoft.FSharp.Core.Result.Ok dacpacB ->
                TaskSync.run (fun () ->
                    fixture.WithEphemeralDatabase "RefactorPub" (fun cnn connStr ->
                        task {
                            // Deploy A, then live data lands under [dbo].[User].
                            RefactorCanary.publishDacpac connStr dacpacA
                            do! Deploy.executeBatch cnn
                                    "INSERT INTO [dbo].[User] ([Email]) VALUES (N'ada@example.test'); \
                                     INSERT INTO [dbo].[User] ([Email]) VALUES (N'grace@example.test');"
                            let! before = RefactorCanary.scalarInt cnn "SELECT COUNT(*) FROM [dbo].[User]"
                            Assert.Equal(2, before)
                            // Incremental publish B: BlockOnPossibleDataLoss (the
                            // DacFx default) refuses a DROP plan — success here
                            // already means the refactorlog was honored.
                            RefactorCanary.publishDacpac connStr dacpacB
                            // The rows live under the NEW name — sp_rename, not
                            // DROP+CREATE. IDENTITY values preserved.
                            let! count = RefactorCanary.scalarInt cnn "SELECT COUNT(*) FROM [dbo].[Client]"
                            Assert.Equal(2, count)
                            let! ada = RefactorCanary.scalarInt cnn "SELECT COUNT(*) FROM [dbo].[Client] WHERE [Email] = N'ada@example.test' AND [Id] = 1"
                            Assert.Equal(1, ada)
                            let! oldGone = RefactorCanary.scalarInt cnn "SELECT COUNT(*) FROM sys.tables WHERE name = 'User' AND schema_id = SCHEMA_ID('dbo')"
                            Assert.Equal(0, oldGone)
                        }))
        finally
            for p in [ modelA; modelB; cfgA; cfgB; store ] do
                try if File.Exists p then File.Delete p with _ -> ()
            for d in [ outA; outB ] do
                try if Directory.Exists d then Directory.Delete(d, true) with _ -> ()
