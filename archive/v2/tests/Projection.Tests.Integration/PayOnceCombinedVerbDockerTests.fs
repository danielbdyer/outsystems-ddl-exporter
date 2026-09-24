namespace Projection.Tests

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Targets.Data

// ---------------------------------------------------------------------------
// PL-1 (PAY_ONCE_PLAN) — the combined-verb identity gate over a REAL OSSYS
// estate (the content-bearing twin of the pure
// `PayOnceCombinedVerbTests`): hydrated static + bootstrap rows make the
// seed plan non-empty, so the threaded-vs-standalone equality is exercised
// on real content, not empty plans. Also the WIRE RECEIPT: one
// `runWithConfigAndStore` pays exactly ONE `adapter.osm.extract` (the
// incumbent paid a second full metadata extraction for the store leg's
// emitted-schema plane).
// ---------------------------------------------------------------------------

[<Xunit.Collection("Docker-SqlServer")>]
type PayOnceCombinedVerbDockerTests(fixture: EphemeralContainerFixture) =

    let mustOk (r: Result<'a>) : 'a =
        match r with
        | Ok v -> v
        | Error es ->
            let detail =
                es
                |> List.map (fun e -> System.String.Concat(e.Code, ": ", e.Message))
                |> String.concat " | "
            invalidOp (System.String.Concat("expected Ok; got: ", detail))

    let extractCount () : int =
        Bench.snapshot ()
        |> List.tryFind (fun s -> s.Label = "adapter.osm.extract")
        |> Option.map (fun s -> s.Count)
        |> Option.defaultValue 0

    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``PL-1: combined publish+store pays ONE extraction, and both legs' planes equal the standalone forms`` () =
        if not (Deploy.Docker.ensureRunning ()) then
            printfn "SKIP pay-once-combined-verb: Docker daemon not reachable."
        else
        let envVar = "PROJECTION_TEST_PAYONCE_CONN"
        let outDir =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                System.String.Concat("payonce_", System.Guid.NewGuid().ToString("N")))
        let storePath =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                System.String.Concat("payonce_store_", System.Guid.NewGuid().ToString("N"), ".json"))
        (fixture.WithEphemeralDatabase "PayOnce" (fun cnn connStr ->
            task {
                // The self-contained OSSYS edge-case estate + deterministic
                // rows (same seed as the pipelined-equivalence canary): City +
                // Customer feed the Bootstrap drain, Country the static graft.
                do! Deploy.executeBatch cnn (Projection.Adapters.OssysSql.MetadataExtractionSql.readEdgeCaseSeed ())
                do!
                    Deploy.executeBatch cnn
                        ("SET IDENTITY_INSERT [dbo].[OSUSR_DEF_CITY] ON; " +
                         "INSERT INTO [dbo].[OSUSR_DEF_CITY] ([ID],[NAME],[ISACTIVE]) " +
                         "VALUES (1, N'Springfield', 1), (2, N'Shelbyville', 1); " +
                         "SET IDENTITY_INSERT [dbo].[OSUSR_DEF_CITY] OFF;")
                do!
                    Deploy.executeBatch cnn
                        ("SET IDENTITY_INSERT [dbo].[OSUSR_ABC_CUSTOMER] ON; " +
                         "INSERT INTO [dbo].[OSUSR_ABC_CUSTOMER] ([ID],[EMAIL],[FIRSTNAME],[LASTNAME],[CITYID],[LEGACYCODE]) " +
                         "VALUES (1, N'alice@example.com', N'Alice', N'Amber', 1, NULL), " +
                         "(2, N'bob@example.com', N'Bob', N'Blue', 2, N'BC-7'); " +
                         "SET IDENTITY_INSERT [dbo].[OSUSR_ABC_CUSTOMER] OFF;")
                do!
                    Deploy.executeBatch cnn
                        ("SET IDENTITY_INSERT [dbo].[OSUSR_REF_COUNTRY] ON; " +
                         "INSERT INTO [dbo].[OSUSR_REF_COUNTRY] ([ID],[CODE],[NAME]) " +
                         "VALUES (1, N'ATL', N'Atlantis'); " +
                         "SET IDENTITY_INSERT [dbo].[OSUSR_REF_COUNTRY] OFF;")
                System.Environment.SetEnvironmentVariable(envVar, connStr)
                let priorSource =
                    System.Environment.GetEnvironmentVariable Config.SourceConnectionStringEnvVar
                System.Environment.SetEnvironmentVariable(Config.SourceConnectionStringEnvVar, connStr)
                try
                    let cfg =
                        { Config.defaultConfig with
                            Model =
                                { Config.defaultConfig.Model with
                                    Ossys = Some ("env:" + envVar); Path = None }
                            Output = { Dir = outDir }
                            Profiler =
                                { Config.defaultConfig.Profiler with
                                    Provider = Config.ProfilerProvider.Live } }
                    let tl = Timeline.create "payonce" |> mustOk
                    let at = System.DateTimeOffset(2026, 7, 2, 9, 0, 0, System.TimeSpan.Zero)

                    // --- The wire receipt: ONE extraction per combined verb.
                    let extractsBefore = extractCount ()
                    let! storeR = Compose.runWithConfigAndStore cfg (Some storePath) tl Environment.Dev at
                    let _report, legOpt = mustOk storeR
                    let extractsAfter = extractCount ()
                    Assert.Equal(1, extractsAfter - extractsBefore)
                    Assert.True(legOpt.IsSome, "store path supplied but no store leg returned")

                    // --- The store-leg plane: the recorded episode's schema
                    // equals a FRESH emitted-schema read of the same estate.
                    let recorded =
                        match legOpt with
                        | Some leg -> EpisodicLifecycle.latest leg.Chain |> Episode.schema
                        | None -> invalidOp "unreachable"
                    let! freshR = Compose.emittedSchema cfg
                    let fresh = mustOk freshR
                    Assert.Equal<Catalog>(fresh, recorded)

                    // --- The load-leg plane, content-bearing: the seed plan
                    // threaded from the publish's acquisition equals the
                    // standalone (fresh-acquiring) form, and is NON-EMPTY (the
                    // estate really exercised the data lanes — an empty plan
                    // would make the equality vacuous).
                    let! acquiredR = Compose.runWithConfigAcquiring cfg
                    let _rep, acquired = mustOk acquiredR
                    let threadedCatalog, threadedPlan =
                        Compose.projectSeedPlanUsing cfg acquired |> mustOk
                    let! standaloneR = Compose.emittedSeedPlan cfg
                    let standaloneCatalog, standalonePlan = mustOk standaloneR
                    Assert.False(
                        DataEmissionComposer.LeveledDeploymentText.isEmpty threadedPlan,
                        "the estate must project a non-empty seed plan (non-vacuous gate)")
                    Assert.Equal<Catalog>(standaloneCatalog, threadedCatalog)
                    Assert.Equal<DataEmissionComposer.LeveledDeploymentText>(standalonePlan, threadedPlan)
                    return ()
                finally
                    System.Environment.SetEnvironmentVariable(envVar, null)
                    System.Environment.SetEnvironmentVariable(Config.SourceConnectionStringEnvVar, priorSource)
            })).GetAwaiter().GetResult()
        for path in [ outDir ] do
            try
                if System.IO.Directory.Exists path then System.IO.Directory.Delete(path, true)
            with _ -> ()
        try
            if System.IO.File.Exists storePath then System.IO.File.Delete storePath
        with _ -> ()

    // -----------------------------------------------------------------------
    // PL-2 (S04) — under AllData every static table is drained ONCE (the
    // graft rides the Bootstrap drain), on BOTH publish schedules; the two
    // schedules stay byte-identical, the static populations still reach the
    // catalog plane, and the data lanes are unchanged (Bootstrap carries the
    // static rows; the Static lane emits nothing).
    // -----------------------------------------------------------------------

    [<Fact>]
    member _.``PL-2: AllData drains each static table once on both schedules; the bundles stay byte-identical`` () =
        if not (Deploy.Docker.ensureRunning ()) then
            printfn "SKIP pay-once-alldata: Docker daemon not reachable."
        else
        let envVar = "PROJECTION_TEST_PAYONCE_ALLDATA_CONN"
        let tempDir () =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                System.String.Concat("payonce_alldata_", System.Guid.NewGuid().ToString("N")))
        let outPipelined = tempDir ()
        let outTwoPhase = tempDir ()
        let staticDrainCount () =
            Bench.snapshot ()
            |> List.tryFind (fun s -> s.Label = "ingestion.rowDrain.dbo.OSUSR_REF_COUNTRY")
            |> Option.map (fun s -> s.Count)
            |> Option.defaultValue 0
        (fixture.WithEphemeralDatabase "PayOnceAllData" (fun cnn connStr ->
            task {
                do! Deploy.executeBatch cnn (Projection.Adapters.OssysSql.MetadataExtractionSql.readEdgeCaseSeed ())
                do!
                    Deploy.executeBatch cnn
                        ("SET IDENTITY_INSERT [dbo].[OSUSR_DEF_CITY] ON; " +
                         "INSERT INTO [dbo].[OSUSR_DEF_CITY] ([ID],[NAME],[ISACTIVE]) " +
                         "VALUES (1, N'Springfield', 1), (2, N'Shelbyville', 1); " +
                         "SET IDENTITY_INSERT [dbo].[OSUSR_DEF_CITY] OFF;")
                do!
                    Deploy.executeBatch cnn
                        ("SET IDENTITY_INSERT [dbo].[OSUSR_ABC_CUSTOMER] ON; " +
                         "INSERT INTO [dbo].[OSUSR_ABC_CUSTOMER] ([ID],[EMAIL],[FIRSTNAME],[LASTNAME],[CITYID],[LEGACYCODE]) " +
                         "VALUES (1, N'alice@example.com', N'Alice', N'Amber', 1, NULL); " +
                         "SET IDENTITY_INSERT [dbo].[OSUSR_ABC_CUSTOMER] OFF;")
                do!
                    Deploy.executeBatch cnn
                        ("SET IDENTITY_INSERT [dbo].[OSUSR_REF_COUNTRY] ON; " +
                         "INSERT INTO [dbo].[OSUSR_REF_COUNTRY] ([ID],[CODE],[NAME]) " +
                         "VALUES (1, N'ATL', N'Atlantis'); " +
                         "SET IDENTITY_INSERT [dbo].[OSUSR_REF_COUNTRY] OFF;")
                System.Environment.SetEnvironmentVariable(envVar, connStr)
                let priorSource =
                    System.Environment.GetEnvironmentVariable Config.SourceConnectionStringEnvVar
                System.Environment.SetEnvironmentVariable(Config.SourceConnectionStringEnvVar, connStr)
                try
                    let cfgFor (outDir: string) (pipelined: bool) : Config.Config =
                        { Config.defaultConfig with
                            Model =
                                { Config.defaultConfig.Model with
                                    Ossys = Some ("env:" + envVar); Path = None }
                            Output = { Dir = outDir }
                            Profiler =
                                { Config.defaultConfig.Profiler with
                                    Provider = Config.ProfilerProvider.Live }
                            Emission =
                                { Config.defaultConfig.Emission with
                                    PipelinedBootstrap = pipelined
                                    BootstrapAllData = true } }
                    // The wire receipt, per schedule: the static table
                    // (OSUSR_REF_COUNTRY) is drained exactly ONCE per publish
                    // — the graft rides the Bootstrap drain (incumbent: a
                    // static-graft stream PLUS the Bootstrap drain = 2).
                    let before = staticDrainCount ()
                    let! pipelinedR = Compose.runWithConfig (cfgFor outPipelined true)
                    let afterPipelined = staticDrainCount ()
                    Assert.Equal(1, afterPipelined - before)
                    let! twoPhaseR = Compose.runWithConfig (cfgFor outTwoPhase false)
                    let afterTwoPhase = staticDrainCount ()
                    Assert.Equal(1, afterTwoPhase - afterPipelined)
                    let _ = mustOk pipelinedR
                    let _ = mustOk twoPhaseR
                    return ()
                finally
                    System.Environment.SetEnvironmentVariable(envVar, null)
                    System.Environment.SetEnvironmentVariable(Config.SourceConnectionStringEnvVar, priorSource)
            })).GetAwaiter().GetResult()
        try
            let filesOf (dir: string) : string list =
                System.IO.Directory.GetFiles(dir, "*", System.IO.SearchOption.AllDirectories)
                |> Array.map (fun p -> (System.IO.Path.GetRelativePath(dir, p)).Replace('\\', '/'))
                |> Array.sort
                |> Array.toList
            let onFiles = filesOf outPipelined
            Assert.Equal<string list>(filesOf outTwoPhase, onFiles)
            // The Bootstrap lane owns the static rows under AllData; the
            // Static lane emits nothing (no per-lane file).
            Assert.Contains(onFiles, fun f -> f.EndsWith("Bootstrap.sql"))
            Assert.DoesNotContain(onFiles, fun f -> f.EndsWith("StaticSeeds.sql"))
            let bootstrap =
                onFiles |> List.find (fun f -> f.EndsWith("Bootstrap.sql"))
            let bootstrapText =
                System.IO.File.ReadAllText(System.IO.Path.Combine(outPipelined, bootstrap))
            Assert.Contains("Atlantis", bootstrapText)
            // Byte-identity across the two schedules — the two independent
            // single-drain arms (drain-retention vs drain-first-then-graft)
            // cannot diverge silently.
            for rel in onFiles do
                let a = System.IO.File.ReadAllBytes(System.IO.Path.Combine(outPipelined, rel))
                let b = System.IO.File.ReadAllBytes(System.IO.Path.Combine(outTwoPhase, rel))
                Assert.True(
                    (a = b),
                    sprintf "file '%s' differs between the pipelined and two-phase AllData publishes" rel)
        finally
            for dir in [ outPipelined; outTwoPhase ] do
                try
                    if System.IO.Directory.Exists dir then System.IO.Directory.Delete(dir, true)
                with _ -> ()
