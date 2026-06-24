module Projection.Tests.SsdtDataBehaviorE2ETests

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
// E2E (Docker): the two config-driven DATA-lane behaviors that change the
// emitted MERGE, proven on a REAL SQL Server (DacFx publish + run the emitter's
// own SQL) —
//   • emission.deleteScope → the convergent `WHEN NOT MATCHED BY SOURCE AND
//     <term> THEN DELETE` arm (a tenant/partition gate): a row IN scope but
//     absent from the source is DELETED; a row OUT of scope survives.
//   • dataVerification=validateBeforeApply → the symmetric-EXCEPT drift guard
//     (`THROW 50000` prelude): silent when aligned, ABORTS the batch on drift.
// These compose the actual emitter output with the behaviour on, so the SQL
// under test is what an operator's `projection.json` would emit.
// ============================================================================

let private mkKey (parts: string list) : SsKey =
    SsKey.synthesizedComposite "OS_DBE" parts |> Result.value
let private col (physical: string) : ColumnRealization =
    ColumnRealization.create physical false |> Result.value

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

/// The rendered StaticSeeds lane for the given policy (the MERGE under test).
let private staticSeeds (policy: Policy) (catalog: Catalog) : string =
    match
        DataEmissionComposer.composeRenderedBundleWithBootstrap
            policy catalog Profile.empty MigrationDependencyContext.empty Map.empty UserRemapContext.empty
    with
    | Ok b -> b.StaticSeeds
    | Error e -> failwithf "data compose failed: %A" e

// ---- deleteScope fixture: Tenant (Id PK, Code, TenantId), two static rows in tenant 1
let private tenantKey = mkKey [ "Sales"; "Tenant" ]
let private tenantCatalog () : Catalog =
    let row idVal code tid =
        { Identifier = mkKey [ "Sales"; "Tenant"; "Row"; code ]
          Values = Map.ofList [ mkName "Id", idVal; mkName "Code", code; mkName "TenantId", tid ] }
    let kind : Kind =
        { SsKey = tenantKey; Name = mkName "Tenant"; Origin = Native
          Modality = [ Static [ row "1" "US" "1"; row "2" "CA" "1" ] ]
          Physical = mkTableId "dbo" "OSUSR_E2E_TENANTED"
          Attributes =
            [ { Attribute.create (mkKey [ "Sales"; "Tenant"; "Id" ]) (mkName "Id") Integer with Column = col "ID"; IsPrimaryKey = true; IsMandatory = true }
              { Attribute.create (mkKey [ "Sales"; "Tenant"; "Code" ]) (mkName "Code") Text with Column = col "CODE"; IsMandatory = true }
              { Attribute.create (mkKey [ "Sales"; "Tenant"; "TenantId" ]) (mkName "TenantId") Integer with Column = col "TENANTID"; IsMandatory = true } ]
          References = []; Indexes = []; Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
    { Modules = [ { SsKey = mkKey [ "Sales" ]; Name = mkName "Sales"; Kinds = [ kind ]; IsActive = true; ExtendedProperties = [] } ]; Sequences = [] }

// ---- validateBeforeApply fixture: Setting (Id PK, Val), one static row
let private settingKey = mkKey [ "Ops"; "Setting" ]
let private settingCatalog () : Catalog =
    let kind : Kind =
        { SsKey = settingKey; Name = mkName "Setting"; Origin = Native
          Modality = [ Static [ { Identifier = mkKey [ "Ops"; "Setting"; "Row"; "S1" ]; Values = Map.ofList [ mkName "Id", "1"; mkName "Val", "Alice" ] } ] ]
          Physical = mkTableId "dbo" "OSUSR_E2E_SETTING"
          Attributes =
            [ { Attribute.create (mkKey [ "Ops"; "Setting"; "Id" ]) (mkName "Id") Integer with Column = col "ID"; IsPrimaryKey = true; IsMandatory = true }
              { Attribute.create (mkKey [ "Ops"; "Setting"; "Val" ]) (mkName "Val") Text with Column = col "VAL"; IsMandatory = true } ]
          References = []; Indexes = []; Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
    { Modules = [ { SsKey = mkKey [ "Ops" ]; Name = mkName "Ops"; Kinds = [ kind ]; IsActive = true; ExtendedProperties = [] } ]; Sequences = [] }

[<Xunit.Collection("Docker-SqlServer")>]
type SsdtDataBehaviorE2ETests(fixture: EphemeralContainerFixture) =
    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``E2E: emission.deleteScope deploys the convergent DELETE arm — in-scope orphan removed, out-of-scope survives`` () =
        if not (Deploy.Docker.ensureRunning ()) then
            printfn "SKIP deleteScope E2E: Docker daemon not reachable."
        else
            let catalog = tenantCatalog ()
            let policy =
                { Policy.empty with
                    Emission = { EmissionPolicy.combined with DeleteScope = Some { Terms = [ { Column = "TENANTID"; Value = "1" } ] } } }
            let seeds = staticSeeds policy catalog
            // the emitter actually produced the convergent-delete arm (else the test is vacuous)
            Assert.Contains("NOT MATCHED BY SOURCE", seeds)
            TaskSync.run (fun () ->
                fixture.WithEphemeralDatabase "DeleteScope" (fun cnn connStr ->
                    task {
                        deploySchema connStr catalog
                        // two orphans not in the seed source: one IN scope (TenantId=1), one OUT (TenantId=2)
                        do! Deploy.executeBatch cnn "INSERT INTO [dbo].[OSUSR_E2E_TENANTED] ([ID],[CODE],[TENANTID]) VALUES (99,'ZZ',1),(88,'YY',2);"
                        do! Deploy.executeBatch cnn seeds
                        let! inScope = scalar cnn "SELECT COUNT(*) FROM [dbo].[OSUSR_E2E_TENANTED] WHERE [ID] = 99;"
                        Assert.Equal("0", inScope)    // in-scope orphan DELETED by the convergent arm
                        let! outScope = scalar cnn "SELECT COUNT(*) FROM [dbo].[OSUSR_E2E_TENANTED] WHERE [ID] = 88;"
                        Assert.Equal("1", outScope)   // out-of-scope orphan SURVIVES (term predicate excludes it)
                        let! seeded = scalar cnn "SELECT COUNT(*) FROM [dbo].[OSUSR_E2E_TENANTED] WHERE [ID] IN (1,2);"
                        Assert.Equal("2", seeded)     // the source rows upserted
                    }))

    [<Fact>]
    member _.``E2E: dataVerification validateBeforeApply is silent when aligned and THROWs 50000 on drift`` () =
        if not (Deploy.Docker.ensureRunning ()) then
            printfn "SKIP validateBeforeApply E2E: Docker daemon not reachable."
        else
            let catalog = settingCatalog ()
            let policy =
                { Policy.empty with
                    Emission = { EmissionPolicy.combined with DataVerification = DataVerification.ValidateBeforeApply } }
            let seeds = staticSeeds policy catalog
            Assert.Contains("THROW 50000", seeds)
            TaskSync.run (fun () ->
                fixture.WithEphemeralDatabase "ValidateBeforeApply" (fun cnn connStr ->
                    task {
                        deploySchema connStr catalog
                        // 1) first apply over the empty target — guard silent, inserts Alice
                        do! Deploy.executeBatch cnn seeds
                        let! v1 = scalar cnn "SELECT [VAL] FROM [dbo].[OSUSR_E2E_SETTING] WHERE [ID] = 1;"
                        Assert.Equal("Alice", v1)
                        // 2) aligned re-apply — guard silent (no drift), idempotent
                        do! Deploy.executeBatch cnn seeds
                        // 3) introduce DRIFT, then re-apply — the guard must THROW 50000 and ABORT
                        do! Deploy.executeBatch cnn "UPDATE [dbo].[OSUSR_E2E_SETTING] SET [VAL] = 'Bob' WHERE [ID] = 1;"
                        let! threw =
                            task {
                                try
                                    do! Deploy.executeBatch cnn seeds
                                    return false
                                with :? SqlException as ex -> return ex.Number = 50000
                            }
                        Assert.True(threw, "the validate-before-apply guard must THROW 50000 on drift")
                        // the MERGE never ran — the drifted value still stands
                        let! v2 = scalar cnn "SELECT [VAL] FROM [dbo].[OSUSR_E2E_SETTING] WHERE [ID] = 1;"
                        Assert.Equal("Bob", v2)
                    }))
