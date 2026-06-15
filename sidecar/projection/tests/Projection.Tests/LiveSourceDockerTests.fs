namespace Projection.Tests

// Docker-gated end-to-end witness for the LIVE-SOURCE path
// (`PLAN_2026_06_14_LIVE_SOURCE_AND_BOOTSTRAP.md` §2). The live-source
// adapter (`Source.ofLive`; `Ref` `live:` resolution) and `compare`
// live-profiling shipped build-verified; these tests prove they work
// end-to-end against a real SQL Server — the witness the build-only
// verification could not give.
//
// The pure capability surface is already covered (`SourceTests` —
// `canProfile` false for a snapshot, true for a live source;
// `CompareTests` — a no-profile operand is advisory-silent). What needs
// a real database, and is asserted here:
//
//   1. **Live catalog readback** — `live:<connStr>` resolves through
//      `Ref.resolveCatalog` → `Source.ofLive` → `ReadSide.read`, and the
//      reconstructed `Catalog` carries the deployed table.
//   2. **`compare` live-profiling dealbreaker** — A's live data is
//      profiled against B's declared model, and a row in A that violates
//      B's NOT NULL declaration surfaces as a data dealbreaker. This is
//      the test that catches the 4.4 trap: `ReadSide` marks every
//      reconstructed kind `Static`, and `LiveProfiler` SKIPS static kinds
//      — so without `Source.ofLive` stripping the Static mark before it
//      profiles, the dealbreaker section would be silently empty and
//      `compare`'s live-profiling feature would be inert. The assertion
//      `DataDealbreakers` is NON-empty is the executable guard on that.
//
// Soft-skip on `Deploy.Docker.ensureRunning ()` (mirrors
// `CanaryRoundTripTests`). `IClassFixture<EphemeralContainerFixture>`
// shares one container; each test (and each operand) takes its own
// `<prefix>_<guid>` database via `WithEphemeralDatabase`.

open Microsoft.Data.SqlClient
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Targets.Data

[<Xunit.Collection("Docker-SqlServer")>]
type LiveSourceDockerTests(fixture: EphemeralContainerFixture) =

    let skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else
            printfn "SKIP %s: Docker daemon not reachable." label
            false

    let mustOk (r: Result<'a>) : 'a =
        match r with
        | Ok v -> v
        | Error es ->
            let detail =
                es
                |> List.map (fun e -> System.String.Concat(e.Code, ": ", e.Message))
                |> String.concat " | "
            invalidOp (System.String.Concat("expected Ok; got: ", detail))

    let mustOkEmit (r: Result<'a, EmitError>) : 'a =
        match r with
        | Ok v -> v
        | Error e -> invalidOp (sprintf "expected Ok; got %A" e)

    let mkName (s: string) : Name = Name.create s |> mustOk
    let mkKey (parts: string list) : SsKey = SsKey.synthesizedComposite "OS_BOOT" parts |> mustOk

    interface IClassFixture<EphemeralContainerFixture>

    // -- Test 1 — live catalog readback -----------------------------------

    [<Fact>]
    member _.``live: ref resolves a deployed schema's catalog back through ReadSide`` () =
        if not (skipIfNoDocker "live-catalog-readback") then () else
        let found =
            fixture.WithEphemeralDatabase "LiveSrcRead" (fun cnn connStr ->
                task {
                    do!
                        Deploy.executeBatch cnn
                            ("CREATE TABLE [dbo].[OSUSR_LIVE_WIDGET] (" +
                             "[ID] INT NOT NULL PRIMARY KEY, " +
                             "[NAME] NVARCHAR(50) NOT NULL);")
                    // Resolve the live ref exactly as the CLI does.
                    let! catR = Ref.resolveCatalog (Ref.parse ("live:" + connStr))
                    let catalog = mustOk catR
                    return
                        Catalog.allKinds catalog
                        |> List.exists (fun k -> TableId.tableText k.Physical = "OSUSR_LIVE_WIDGET")
                })
            |> fun t -> t.GetAwaiter().GetResult()
        Assert.True(
            found,
            "live: ref did not reconstruct the deployed OSUSR_LIVE_WIDGET table via ReadSide")

    // -- Test 2 — compare live-profiling end-to-end -----------------------

    [<Fact>]
    member _.``compare live-profiling: A's NULL data against B's NOT NULL model surfaces a dealbreaker`` () =
        if not (skipIfNoDocker "compare-live-dealbreaker") then () else
        // Target B (the declared model): STATUS is NOT NULL.
        // Source A (the live env):       STATUS is NULLABLE and holds a NULL —
        // a row B's declared model would reject. Both tables share physical
        // coordinates, so ReadSide synthesizes identical `READSIDE_ATTR` keys
        // and A's profile correlates with B's declared columns.
        let report =
            fixture.WithEphemeralDatabase "CmpTarget" (fun cnnB connB ->
                task {
                    do!
                        Deploy.executeBatch cnnB
                            ("CREATE TABLE [dbo].[OSUSR_CMP_ORDER] (" +
                             "[ID] INT NOT NULL PRIMARY KEY, " +
                             "[STATUS] INT NOT NULL);")
                    return!
                        fixture.WithEphemeralDatabase "CmpSource" (fun cnnA connA ->
                            task {
                                do!
                                    Deploy.executeBatch cnnA
                                        ("CREATE TABLE [dbo].[OSUSR_CMP_ORDER] (" +
                                         "[ID] INT NOT NULL PRIMARY KEY, " +
                                         "[STATUS] INT NULL);")
                                // The dealbreaker: a NULL in a column B declares NOT NULL.
                                do!
                                    Deploy.executeBatch cnnA
                                        ("INSERT INTO [dbo].[OSUSR_CMP_ORDER] ([ID], [STATUS]) " +
                                         "VALUES (1, NULL);")
                                // Resolve A as a profilable Source, B's catalog only —
                                // mirroring `RunFaces.runCompare`.
                                let! srcA = Ref.resolveSource (Ref.parse ("live:" + connA))
                                let srcA = mustOk srcA
                                let! aCatR = Source.read srcA
                                let aCat = mustOk aCatR
                                let! bCatR = Ref.resolveCatalog (Ref.parse ("live:" + connB))
                                let bCat = mustOk bCatR
                                let! profileA =
                                    match Source.profile srcA with
                                    | None -> task { return None }
                                    | Some acquire ->
                                        task {
                                            let! p = acquire aCat
                                            return (match p with Ok v -> Some v | Error _ -> None)
                                        }
                                let source : Compare.Operand =
                                    { Label = "live:A"; Catalog = aCat; Profile = profileA }
                                let target : Compare.Operand =
                                    { Label = "live:B"; Catalog = bCat; Profile = None }
                                return Compare.compute source target
                            })
                })
            |> fun t -> t.GetAwaiter().GetResult()
        // A is a live env → evidence is available (the source was profiled).
        Assert.True(
            report.DataEvidenceAvailable,
            "expected live source A to carry profiled data evidence")
        // The injected NULL violates B's NOT NULL STATUS → at least one dealbreaker.
        // (Empty here would mean the 4.4 trap is back: the Static mark was not
        // stripped before profiling, so LiveProfiler skipped every kind.)
        Assert.True(
            (not (List.isEmpty report.DataDealbreakers)),
            "expected a NOT-NULL data dealbreaker from A's NULL row against B's model")

    // -- Test 3 — Bootstrap-always: live hydration populates Data/Bootstrap.sql --

    [<Fact>]
    member _.``Bootstrap-always: hydrateBootstrapRows streams live rows into a populated Data/Bootstrap.sql`` () =
        if not (skipIfNoDocker "bootstrap-live-hydration") then () else
        // A NON-static, data-bearing kind whose rows live in a real DB. The
        // config's model.ossys points at that DB (via an env: ref); the
        // Bootstrap lane hydrates its rows and renders Data/Bootstrap.sql — the
        // operator's "Bootstrap created, both Docker and live source."
        let envVar = "PROJECTION_TEST_BOOT_CONN"
        let bundle =
            fixture.WithEphemeralDatabase "BootSrc" (fun cnn connStr ->
                task {
                    do!
                        Deploy.executeBatch cnn
                            ("CREATE TABLE [dbo].[OSUSR_BOOT_WIDGET] (" +
                             "[ID] INT NOT NULL PRIMARY KEY, " +
                             "[NAME] NVARCHAR(100) NOT NULL);")
                    do!
                        Deploy.executeBatch cnn
                            ("INSERT INTO [dbo].[OSUSR_BOOT_WIDGET] ([ID], [NAME]) " +
                             "VALUES (1, N'Gadget'), (2, N'Gizmo');")
                    // A programmatic NON-static catalog kind over the deployed
                    // table — non-static ⇒ bootstrap-eligible under AllRemaining.
                    let widget : Kind =
                        { Kind.create (mkKey ["Widget"]) (mkName "Widget")
                            (TableId.create "dbo" "OSUSR_BOOT_WIDGET" |> mustOk)
                            [ { Attribute.create (mkKey ["Widget"; "Id"]) (mkName "Id") Integer with
                                  Column = ColumnRealization.create "ID" false |> Result.value
                                  IsPrimaryKey = true; IsMandatory = true }
                              { Attribute.create (mkKey ["Widget"; "Name"]) (mkName "Name") Text with
                                  Column = ColumnRealization.create "NAME" false |> Result.value
                                  Length = Some 100; IsMandatory = true } ]
                          with Modality = [] }
                    let catalog : Catalog =
                        { Modules =
                            [ { SsKey = mkKey ["M"]; Name = mkName "M"
                                Kinds = [ widget ]; IsActive = true; ExtendedProperties = [] } ]
                          Sequences = [] }
                    // model.ossys = env:<var> → the per-DB connection string.
                    System.Environment.SetEnvironmentVariable(envVar, connStr)
                    try
                        let cfg =
                            { Config.defaultConfig with
                                Model = { Config.defaultConfig.Model with
                                            Ossys = Some ("env:" + envVar); Path = None } }
                        let! rowsR = Hydration.hydrateBootstrapRows cfg catalog
                        let bootstrapRows = mustOk rowsR
                        // The live rows reached the bootstrap row source.
                        Assert.True(
                            Map.containsKey widget.SsKey bootstrapRows,
                            "hydrateBootstrapRows did not stream the widget kind's rows")
                        let bundle =
                            DataEmissionComposer.composeRenderedBundleWithBootstrap
                                Policy.empty catalog Profile.empty
                                MigrationDependencyContext.empty bootstrapRows UserRemapContext.empty
                            |> mustOkEmit
                        return bundle
                    finally
                        System.Environment.SetEnvironmentVariable(envVar, null)
                })
            |> fun t -> t.GetAwaiter().GetResult()
        // Data/Bootstrap.sql is emitted and carries the live rows.
        let files = DataEmissionComposer.RenderedDataBundle.perLaneFiles bundle
        Assert.True(
            Map.containsKey "Data/Bootstrap.sql" files,
            "Data/Bootstrap.sql was not emitted from the live-hydrated bootstrap lane")
        let boot = Map.find "Data/Bootstrap.sql" files
        Assert.Contains("MERGE", boot)
        Assert.Contains("OSUSR_BOOT_WIDGET", boot)
        Assert.Contains("Gadget", boot)

    // -- Test 4 — the data triumvirate: all three lanes live + disjoint ----
    //
    // Migration-context wiring (2026-06-15). The three data lanes populate
    // from THREE sources end-to-end:
    //   * StaticSeeds  ← live OSSYS hydration of a Static-marked kind
    //                     (`Hydration.hydrateCatalog`).
    //   * MigrationData ← the operator-curated file at
    //                     `overrides.migrationDependencies.path`
    //                     (`MigrationDependenciesBinding.fromConfig`).
    //   * Bootstrap    ← live OSSYS hydration of the complement
    //                     (`Hydration.hydrateBootstrapRowsExcluding`, the
    //                     migration kind excluded so the lanes stay disjoint).
    // The composer's `OverlappingEmitterCoverage` partition law is the guard
    // that the three lanes don't double-claim a kind — `mustOkEmit` trips on
    // it. Each lane's `Data/*.sql` is asserted non-empty + carrying its row.
    [<Fact>]
    member _.``the data triumvirate: StaticSeeds + MigrationData + Bootstrap all populate live and stay disjoint`` () =
        if not (skipIfNoDocker "data-triumvirate-live") then () else
        let envVar = "PROJECTION_TEST_TRIUM_CONN"
        let bundle =
            fixture.WithEphemeralDatabase "TriumSrc" (fun cnn connStr ->
                task {
                    // Static lane source: a Static-marked kind, rows live.
                    do!
                        Deploy.executeBatch cnn
                            ("CREATE TABLE [dbo].[OSUSR_TRIUM_COUNTRY] (" +
                             "[ID] INT NOT NULL PRIMARY KEY, " +
                             "[NAME] NVARCHAR(100) NOT NULL);")
                    do!
                        Deploy.executeBatch cnn
                            ("INSERT INTO [dbo].[OSUSR_TRIUM_COUNTRY] ([ID], [NAME]) " +
                             "VALUES (1, N'Atlantis');")
                    // Bootstrap lane source: a non-static kind, rows live.
                    do!
                        Deploy.executeBatch cnn
                            ("CREATE TABLE [dbo].[OSUSR_TRIUM_WIDGET] (" +
                             "[ID] INT NOT NULL PRIMARY KEY, " +
                             "[NAME] NVARCHAR(100) NOT NULL);")
                    do!
                        Deploy.executeBatch cnn
                            ("INSERT INTO [dbo].[OSUSR_TRIUM_WIDGET] ([ID], [NAME]) " +
                             "VALUES (1, N'Sprocket');")
                    // A `Static`-marked Country, a non-static Widget (bootstrap),
                    // and a non-static Role (migration; its rows come from the
                    // file, never the DB — no Role table is deployed).
                    let mkKind (entity: string) (table: string) (extraName: string) (modality: ModalityMark list) : Kind =
                        { Kind.create (mkKey [entity]) (mkName entity)
                            (TableId.create "dbo" table |> mustOk)
                            [ { Attribute.create (mkKey [entity; "Id"]) (mkName "Id") Integer with
                                  Column = ColumnRealization.create "ID" false |> Result.value
                                  IsPrimaryKey = true; IsMandatory = true }
                              { Attribute.create (mkKey [entity; extraName]) (mkName extraName) Text with
                                  Column = ColumnRealization.create (extraName.ToUpperInvariant()) false |> Result.value
                                  Length = Some 100; IsMandatory = true } ]
                          with Modality = modality }
                    let country = mkKind "Country" "OSUSR_TRIUM_COUNTRY" "Name" [ Static [] ]
                    let widget  = mkKind "Widget"  "OSUSR_TRIUM_WIDGET"  "Name" []
                    let role    = mkKind "Role"    "OSUSR_TRIUM_ROLE"    "Label" []
                    let catalog : Catalog =
                        { Modules =
                            [ { SsKey = mkKey ["M"]; Name = mkName "M"
                                Kinds = [ country; widget; role ]; IsActive = true; ExtendedProperties = [] } ]
                          Sequences = [] }
                    // The operator-curated migration file (logical-keyed JSON).
                    let migJson =
                        """{ "kinds": [ { "module": "M", "entity": "Role",
                              "rows": [ { "id": "Admin", "values": { "Id": "1", "Label": "Administrator" } } ] } ] }"""
                    let migPath =
                        System.IO.Path.Combine(
                            System.IO.Path.GetTempPath(),
                            System.String.Concat("trium_mig_", System.Guid.NewGuid().ToString("N"), ".json"))
                    System.IO.File.WriteAllText(migPath, migJson)
                    System.Environment.SetEnvironmentVariable(envVar, connStr)
                    try
                        let cfg =
                            { Config.defaultConfig with
                                Model = { Config.defaultConfig.Model with
                                            Ossys = Some ("env:" + envVar); Path = None }
                                Overrides = { Config.defaultConfig.Overrides with
                                                MigrationDependencies = Some { Path = migPath } } }
                        // The pipeline's extract-stage seam, called directly.
                        let migration = mustOk (MigrationDependenciesBinding.fromConfig catalog cfg)
                        let migrationKinds = MigrationDependenciesBinding.kindKeysOf migration
                        let! hydratedR = Hydration.hydrateCatalog cfg catalog
                        let hydrated = mustOk hydratedR
                        let! bootR = Hydration.hydrateBootstrapRowsExcluding migrationKinds cfg hydrated
                        let bootstrapRows = mustOk bootR
                        // The migration kind is NOT in the bootstrap source (disjoint).
                        Assert.False(
                            Map.containsKey role.SsKey bootstrapRows,
                            "the migration kind leaked into the Bootstrap row source")
                        let bundle =
                            DataEmissionComposer.composeRenderedBundleWithBootstrap
                                Policy.empty hydrated Profile.empty
                                migration bootstrapRows UserRemapContext.empty
                            |> mustOkEmit
                        return bundle
                    finally
                        System.Environment.SetEnvironmentVariable(envVar, null)
                        (try System.IO.File.Delete migPath with _ -> ())
                })
            |> fun t -> t.GetAwaiter().GetResult()
        let files = DataEmissionComposer.RenderedDataBundle.perLaneFiles bundle
        // All three per-lane files are emitted (≥1 row each) — the triumvirate.
        Assert.True(Map.containsKey "Data/StaticSeeds.sql" files,   "Data/StaticSeeds.sql missing")
        Assert.True(Map.containsKey "Data/MigrationData.sql" files, "Data/MigrationData.sql missing")
        Assert.True(Map.containsKey "Data/Bootstrap.sql" files,     "Data/Bootstrap.sql missing")
        // Each lane carries its own source's row, and only its own.
        let staticSeeds = Map.find "Data/StaticSeeds.sql" files
        Assert.Contains("OSUSR_TRIUM_COUNTRY", staticSeeds)
        Assert.Contains("Atlantis", staticSeeds)
        let migData = Map.find "Data/MigrationData.sql" files
        Assert.Contains("OSUSR_TRIUM_ROLE", migData)
        Assert.Contains("Administrator", migData)
        let boot = Map.find "Data/Bootstrap.sql" files
        Assert.Contains("OSUSR_TRIUM_WIDGET", boot)
        Assert.Contains("Sprocket", boot)
