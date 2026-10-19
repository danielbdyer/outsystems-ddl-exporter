module Projection.Tests.StagedMergeDeployE2ETests

open System.IO
open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Microsoft.SqlServer.Dac
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Targets.SSDT
open Projection.Targets.Data
open Projection.Tests.Fixtures   // mkName, mkTableId

// ============================================================================
// E2E (Docker): the staged-`#temp` MERGE (the error-8623-safe form for large
// kinds) and its set-based Phase-2, proven on a REAL SQL Server. These are the
// DURABLE witnesses for the 2026-06-25 typed-`buildAtomicBatch` refactor +
// Step-4 set-based Phase-2: the staged path is NOT golden-locked (all goldens
// are ≤3 rows / inline), and a substring unit test is green even when the full
// batch is malformed — only a deploy catches that (the `TableName`-VO temp-name
// bug shipped through green substrings and died only at deploy). So:
//   • Phase-1 staged: a >threshold static kind stages through `#seed_<table>`,
//     deploys, all rows land, re-deploy is idempotent.
//   • Phase-2 staged: a >threshold SELF-REFERENTIAL (nullable self-FK) kind —
//     Phase-1 inserts every row with the FK NULLed, the set-based Phase-2
//     `UPDATE … FROM target JOIN #fk_<table>` re-points every FK in ONE
//     statement; the self-FK constraint deploys and is satisfied.
//   • Atomicity: a mid-batch failure (duplicate PK among the staged rows) rolls
//     the WHOLE batch back (XACT_ABORT + TRY/CATCH) — the target is untouched
//     and no `#temp` survives.
// ============================================================================

let private col (physical: string) : ColumnRealization =
    ColumnRealization.create physical false |> Result.value
let private colNullable (physical: string) : ColumnRealization =
    ColumnRealization.create physical true |> Result.value

let private mkKey (parts: string list) : SsKey =
    SsKey.synthesizedComposite "OS_STG" parts |> Result.value

let private scalar (cnn: SqlConnection) (sql: string) : Task<string> =
    task {
        use cmd = cnn.CreateCommand()
        cmd.CommandText <- sql
        let! v = cmd.ExecuteScalarAsync()
        return string v
    }

/// Publish the catalog's schema-only `.dacpac` to the ephemeral database.
let private deploySchema (connStr: string) (catalog: Catalog) : unit =
    let dbName = SqlConnectionStringBuilder(connStr).InitialCatalog
    let bytes =
        match DacpacEmitter.emit catalog with
        | Ok b -> b
        | Error es -> failwithf "dacpac emit failed: %A" es
    use stream = new MemoryStream(bytes)
    use package = DacPackage.Load stream
    (DacServices connStr).Deploy(package, dbName, true, DacDeployOptions())

/// The rendered StaticSeeds lane (the staged MERGE / UPDATE under test).
let private staticSeeds (policy: Policy) (catalog: Catalog) : string =
    match
        DataEmissionComposer.composeRenderedBundleWithBootstrap
            policy catalog Profile.empty MigrationDependencyContext.empty Map.empty UserRemapContext.empty
    with
    | Ok b -> b.StaticSeeds
    | Error e -> failwithf "data compose failed: %A" e

// ---- a wide static kind above the staging threshold (PK Id, Code).
let private bigStaticCatalog (physical: string) (rows: (string * string list) list) : Catalog =
    StaticCatalogFixtures.staticCatalog "STGE2E" "StgMod" [ physical ] physical physical
        [ StaticCatalogFixtures.pk "Id" "ID" Integer
          StaticCatalogFixtures.attr "Code" "CODE" Text ]
        rows

// ---- a self-referencing kind (Id PK, Label, ParentId nullable self-FK).
let private treeCatalog (n: int) : Catalog =
    let kindKey   = mkKey [ "Tree" ]
    let idKey     = mkKey [ "Tree"; "Id" ]
    let labelKey  = mkKey [ "Tree"; "Label" ]
    let parentKey = mkKey [ "Tree"; "ParentId" ]
    let refKey    = mkKey [ "Tree"; "RefParent" ]
    let row i =
        { Identifier = mkKey [ "Tree"; "Row"; string i ]
          Values =
              Map.ofList
                  [ mkName "Id",       Some (string i)
                    mkName "Label",    Some (sprintf "n%d" i)
                    // root (Id=1) has no parent (explicit NULL); every other row
                    // points at i-1, a chain whose targets all exist once Phase-1
                    // has inserted every row (with ParentId NULLed) — the
                    // deferral's reason.
                    mkName "ParentId", (if i = 1 then None else Some (string (i - 1))) ] }
    let kind : Kind =
        { SsKey = kindKey; Name = mkName "Tree"; Origin = Native
          Modality = [ Static [ for i in 1 .. n -> row i ] ]
          Physical = mkTableId "dbo" "OSUSR_E2E_TREE"
          Attributes =
            [ { Attribute.create idKey (mkName "Id") Integer with Column = col "ID"; IsPrimaryKey = true; IsMandatory = true }
              { Attribute.create labelKey (mkName "Label") Text with Column = col "LABEL"; IsMandatory = true }
              { Attribute.create parentKey (mkName "ParentId") Integer with Column = colNullable "PARENTID" } ]
          References = [ Reference.create refKey (mkName "RefParent") parentKey kindKey ]
          Indexes = []; Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
    { Modules = [ { SsKey = mkKey [ "Mod" ]; Name = mkName "StgMod"; Kinds = [ kind ]; IsActive = true; ExtendedProperties = [] } ]; Sequences = [] }

// ---- a migration-CHANNEL kind (Modality=[], populated via MigrationDependencyContext,
// NOT static) — the lane that had its own un-staged renderMerge and so still hit the
// 8623 wall until the shared StagedMerge module reached it (2026-06-25).
let private migrationCatalog (physical: string) : Catalog =
    let kindKey = mkKey [ physical ]
    let kind : Kind =
        { SsKey = kindKey; Name = mkName physical; Origin = Native
          Modality = []   // populated via the Migration channel, not Static
          Physical = mkTableId "dbo" physical
          Attributes =
            [ { Attribute.create (mkKey [ physical; "Id" ]) (mkName "Id") Integer with Column = col "ID"; IsPrimaryKey = true; IsMandatory = true }
              { Attribute.create (mkKey [ physical; "Code" ]) (mkName "Code") Text with Column = col "CODE"; IsMandatory = true } ]
          References = []; Indexes = []; Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
    { Modules = [ { SsKey = mkKey [ "Mod" ]; Name = mkName "MigMod"; Kinds = [ kind ]; IsActive = true; ExtendedProperties = [] } ]; Sequences = [] }

let private migrationContext (physical: string) (n: int) : MigrationDependencyContext =
    let kindKey = mkKey [ physical ]
    { Rows =
        [ for i in 1 .. n ->
            { KindKey = kindKey
              Identifier = mkKey [ physical; "Row"; string i ]
              Values = StaticRow.presentValues [ mkName "Id", string i; mkName "Code", sprintf "C%05d" i ] } ] }

/// The rendered MigrationDependencies lane (the staged MERGE under test) via the composer.
let private migrationSeeds (policy: Policy) (catalog: Catalog) (context: MigrationDependencyContext) : string =
    match
        DataEmissionComposer.composeRenderedBundleWithBootstrap
            policy catalog Profile.empty context Map.empty UserRemapContext.empty
    with
    | Ok b -> b.MigrationData
    | Error e -> failwithf "migration compose failed: %A" e

[<Xunit.Collection("Docker-SqlServer")>]
type StagedMergeDeployE2ETests(fixture: EphemeralContainerFixture) =
    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``E2E: a >threshold MIGRATION-channel kind stages through #temp, deploys all rows, and re-deploy is idempotent`` () =
        if not (Deploy.Docker.ensureRunning ()) then
            printfn "SKIP staged migration E2E: Docker daemon not reachable."
        else
            let n = 1500
            let catalog = migrationCatalog "MIG_E2E_BIG"
            let context = migrationContext "MIG_E2E_BIG" n
            let policy = { Policy.empty with Emission = EmissionPolicy.combined }
            let seeds = migrationSeeds policy catalog context
            // the staged path was actually taken ON THE MIGRATION LANE (else vacuous) —
            // this is the lane that previously had NO staged path and hit error 8623.
            Assert.Contains("CREATE TABLE [#seed_", seeds)
            Assert.Contains("USING [#seed_", seeds)
            TaskSync.run (fun () ->
                fixture.WithEphemeralDatabase "StagedMigration" (fun cnn connStr ->
                    task {
                        deploySchema connStr catalog
                        do! Deploy.executeBatch cnn seeds
                        let! cnt = scalar cnn "SELECT COUNT(*) FROM [dbo].[MIG_E2E_BIG];"
                        Assert.Equal(string n, cnt)                    // all staged migration rows landed
                        let! probe = scalar cnn "SELECT [CODE] FROM [dbo].[MIG_E2E_BIG] WHERE [ID] = 1500;"
                        Assert.Equal("C01500", probe)
                        do! Deploy.executeBatch cnn seeds              // idempotent re-deploy
                        let! cnt2 = scalar cnn "SELECT COUNT(*) FROM [dbo].[MIG_E2E_BIG];"
                        Assert.Equal(string n, cnt2)
                    }))

    [<Fact>]
    member _.``E2E: a staged kind above indexThreshold deploys the clustered #temp index and all rows land`` () =
        if not (Deploy.Docker.ensureRunning ()) then
            printfn "SKIP staged index E2E: Docker daemon not reachable."
        else
            let n = 1500
            let catalog = bigStaticCatalog "STG_E2E_IDX" [ for i in 1 .. n -> string i, [ string i; sprintf "C%05d" i ] ]
            // lower indexThreshold so a 1500-row staged kind BUILDS the clustered
            // `#temp`-PK index (default 100k floor would not, at this size) — this
            // deploy-verifies the typed `CREATE CLUSTERED INDEX` renders valid SQL.
            let policy =
                { Policy.empty with
                    Emission = { EmissionPolicy.combined with DataStaging = { Mode = DataStagingMode.Auto; Threshold = 1000; IndexThreshold = 1000 } } }
            let seeds = staticSeeds policy catalog
            Assert.Contains("CREATE CLUSTERED INDEX [ix_stg_", seeds)   // the typed index is emitted
            TaskSync.run (fun () ->
                fixture.WithEphemeralDatabase "StagedIndexed" (fun cnn connStr ->
                    task {
                        deploySchema connStr catalog
                        do! Deploy.executeBatch cnn seeds                  // deploys WITH the clustered #temp index
                        let! cnt = scalar cnn "SELECT COUNT(*) FROM [dbo].[STG_E2E_IDX];"
                        Assert.Equal(string n, cnt)                       // all rows landed through the indexed staged MERGE
                        do! Deploy.executeBatch cnn seeds                 // idempotent re-deploy (index rebuilt + dropped each batch)
                        let! cnt2 = scalar cnn "SELECT COUNT(*) FROM [dbo].[STG_E2E_IDX];"
                        Assert.Equal(string n, cnt2)
                    }))

    [<Fact>]
    member _.``E2E: a >threshold static kind stages through #temp, deploys all rows, and re-deploy is idempotent`` () =
        if not (Deploy.Docker.ensureRunning ()) then
            printfn "SKIP staged Phase-1 E2E: Docker daemon not reachable."
        else
            let n = 1500
            let catalog = bigStaticCatalog "STG_E2E_BIG" [ for i in 1 .. n -> string i, [ string i; sprintf "C%05d" i ] ]
            let policy = { Policy.empty with Emission = EmissionPolicy.combined }
            let seeds = staticSeeds policy catalog
            // the staged path was actually taken (else the test is vacuous)
            Assert.Contains("CREATE TABLE [#seed_", seeds)
            Assert.Contains("USING [#seed_", seeds)
            TaskSync.run (fun () ->
                fixture.WithEphemeralDatabase "StagedPhase1" (fun cnn connStr ->
                    task {
                        deploySchema connStr catalog
                        do! Deploy.executeBatch cnn seeds
                        let! cnt = scalar cnn "SELECT COUNT(*) FROM [dbo].[STG_E2E_BIG];"
                        Assert.Equal(string n, cnt)                    // all staged rows landed
                        let! probe = scalar cnn "SELECT [CODE] FROM [dbo].[STG_E2E_BIG] WHERE [ID] = 1500;"
                        Assert.Equal("C01500", probe)                  // a sampled row is correct end-to-end
                        // the MERGE keys on Id → idempotent re-deploy, still n rows
                        do! Deploy.executeBatch cnn seeds
                        let! cnt2 = scalar cnn "SELECT COUNT(*) FROM [dbo].[STG_E2E_BIG];"
                        Assert.Equal(string n, cnt2)
                    }))

    [<Fact>]
    member _.``E2E: a >threshold self-referential kind defers its FK, set-based Phase-2 re-points every row through #fk`` () =
        if not (Deploy.Docker.ensureRunning ()) then
            printfn "SKIP staged Phase-2 E2E: Docker daemon not reachable."
        else
            let n = 1500
            let catalog = treeCatalog n
            let policy = { Policy.empty with Emission = EmissionPolicy.combined }
            let seeds = staticSeeds policy catalog
            // the set-based staged Phase-2 was actually emitted (else vacuous):
            // a narrow `#fk_` temp and a set-based UPDATE FROM it.
            Assert.Contains("#fk_", seeds)
            Assert.Contains("UPDATE", seeds)
            TaskSync.run (fun () ->
                fixture.WithEphemeralDatabase "StagedPhase2" (fun cnn connStr ->
                    task {
                        deploySchema connStr catalog
                        // the self-FK constraint deployed (Phase-1 MUST NULL the FK
                        // or the inserts would violate it before the targets exist)
                        let! fkCount =
                            scalar cnn "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('dbo.OSUSR_E2E_TREE');"
                        Assert.Equal("1", fkCount)
                        do! Deploy.executeBatch cnn seeds
                        let! cnt = scalar cnn "SELECT COUNT(*) FROM [dbo].[OSUSR_E2E_TREE];"
                        Assert.Equal(string n, cnt)
                        // the root keeps a NULL parent; every other row's FK was
                        // re-pointed by the set-based Phase-2 UPDATE.
                        let! rootParent = scalar cnn "SELECT ISNULL(CAST([PARENTID] AS VARCHAR(20)), 'NULL') FROM [dbo].[OSUSR_E2E_TREE] WHERE [ID] = 1;"
                        Assert.Equal("NULL", rootParent)
                        let! leafParent = scalar cnn "SELECT CAST([PARENTID] AS VARCHAR(20)) FROM [dbo].[OSUSR_E2E_TREE] WHERE [ID] = 1500;"
                        Assert.Equal("1499", leafParent)
                        // no row escaped the re-point (every non-root FK populated)
                        let! unpopulated = scalar cnn "SELECT COUNT(*) FROM [dbo].[OSUSR_E2E_TREE] WHERE [ID] > 1 AND [PARENTID] IS NULL;"
                        Assert.Equal("0", unpopulated)
                        // idempotent: a re-deploy churns nothing and the FKs stand
                        do! Deploy.executeBatch cnn seeds
                        let! leafParent2 = scalar cnn "SELECT CAST([PARENTID] AS VARCHAR(20)) FROM [dbo].[OSUSR_E2E_TREE] WHERE [ID] = 1500;"
                        Assert.Equal("1499", leafParent2)
                    }))

    [<Fact>]
    member _.``E2E: a mid-batch failure rolls the whole staged batch back — target untouched, no #temp survives`` () =
        if not (Deploy.Docker.ensureRunning ()) then
            printfn "SKIP staged atomicity E2E: Docker daemon not reachable."
        else
            // 1501 rows, two sharing Id=1 — the staged #temp accepts both (no PK),
            // but the MERGE into the PK'd target fails mid-statement. XACT_ABORT +
            // TRY/CATCH must roll the WHOLE batch back.
            let rows = [ yield ("dup", [ "1"; "DUPLICATE" ])
                         for i in 1 .. 1500 -> string i, [ string i; sprintf "C%05d" i ] ]
            let catalog = bigStaticCatalog "STG_E2E_ATOM" rows
            let policy = { Policy.empty with Emission = EmissionPolicy.combined }
            let seeds = staticSeeds policy catalog
            Assert.Contains("CREATE TABLE [#seed_", seeds)   // staged path
            TaskSync.run (fun () ->
                fixture.WithEphemeralDatabase "StagedAtomicity" (fun cnn connStr ->
                    task {
                        deploySchema connStr catalog
                        let! threw =
                            task {
                                try
                                    do! Deploy.executeBatch cnn seeds
                                    return false
                                with :? SqlException -> return true
                            }
                        Assert.True(threw, "the duplicate-PK staged MERGE must fail and abort the batch")
                        // the whole atomic batch rolled back — NOT ONE row landed
                        let! cnt = scalar cnn "SELECT COUNT(*) FROM [dbo].[STG_E2E_ATOM];"
                        Assert.Equal("0", cnt)
                        // and no staging #temp leaked on the session (the CREATE was
                        // rolled back — DDL is transactional)
                        let! tempAlive = scalar cnn "SELECT ISNULL(CAST(OBJECT_ID('tempdb..#seed_STG_E2E_ATOM') AS VARCHAR(20)), 'GONE');"
                        Assert.Equal("GONE", tempAlive)
                    }))
