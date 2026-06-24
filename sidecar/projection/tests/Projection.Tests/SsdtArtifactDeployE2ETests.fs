module Projection.Tests.SsdtArtifactDeployE2ETests

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
// E2E (Docker): consume V2's emitted SSDT artifacts the way the operator
// deploys — publish the schema `.dacpac` to a real (Testcontainers) SQL Server,
// run the post-deployment DATA (static seeds + migration), then run the
// bootstrap MERGE as a SEPARATE post-publish step — and assert the rows land
// idempotently. Rides the `Docker-SqlServer` pool; soft-skips without Docker.
//
// Schema correctness is implied by the per-table `SELECT COUNT(*)` (they throw
// if the dacpac didn't create the table); the dacpac↔catalog schema fidelity is
// covered separately by `DacpacPublishEquivalenceTests`.
// ============================================================================

let private mkKey (parts: string list) : SsKey =
    SsKey.synthesizedComposite "OS_E2E" parts |> Result.value

let private col (physical: string) : ColumnRealization =
    ColumnRealization.create physical false |> Result.value

// A STATIC-seed kind (Country) — populated from `Modality.Static`.
let private countryKey = mkKey [ "Sales"; "Country" ]
let private mkCountry () : Kind =
    let row idVal code label =
        { Identifier = mkKey [ "Sales"; "Country"; "Row"; code ]
          Values = Map.ofList [ mkName "Id", idVal; mkName "Code", code; mkName "Label", label ] }
    { SsKey = countryKey; Name = mkName "Country"; Origin = Native
      Modality = [ Static [ row "1" "US" "United States"; row "2" "CA" "Canada" ] ]
      Physical = mkTableId "dbo" "OSUSR_E2E_COUNTRY"
      Attributes =
        [ { Attribute.create (mkKey [ "Sales"; "Country"; "Id" ]) (mkName "Id") Integer with Column = col "ID"; IsPrimaryKey = true; IsMandatory = true }
          { Attribute.create (mkKey [ "Sales"; "Country"; "Code" ]) (mkName "Code") Text with Column = col "CODE"; IsMandatory = true }
          { Attribute.create (mkKey [ "Sales"; "Country"; "Label" ]) (mkName "Label") Text with Column = col "LABEL"; IsMandatory = true } ]
      References = []; Indexes = []; Description = None; IsActive = true
      Triggers = []; ColumnChecks = []; ExtendedProperties = [] }

// A non-static kind whose rows ride the MIGRATION lane (MigrationDependencyContext).
let private roleKey = mkKey [ "Sales"; "Role" ]
let private mkRole () : Kind =
    { SsKey = roleKey; Name = mkName "Role"; Origin = Native; Modality = []
      Physical = mkTableId "dbo" "OSUSR_E2E_ROLE"
      Attributes =
        [ { Attribute.create (mkKey [ "Sales"; "Role"; "Id" ]) (mkName "Id") Integer with Column = col "ID"; IsPrimaryKey = true; IsMandatory = true }
          { Attribute.create (mkKey [ "Sales"; "Role"; "Label" ]) (mkName "Label") Text with Column = col "LABEL"; IsMandatory = true } ]
      References = []; Indexes = []; Description = None; IsActive = true
      Triggers = []; ColumnChecks = []; ExtendedProperties = [] }

// A non-static kind whose rows ride the BOOTSTRAP lane (hydrated `bootstrapRows`).
let private productKey = mkKey [ "Sales"; "Product" ]
let private mkProduct () : Kind =
    { SsKey = productKey; Name = mkName "Product"; Origin = Native; Modality = []
      Physical = mkTableId "dbo" "OSUSR_E2E_PRODUCT"
      Attributes =
        [ { Attribute.create (mkKey [ "Sales"; "Product"; "Id" ]) (mkName "Id") Integer with Column = col "ID"; IsPrimaryKey = true; IsMandatory = true }
          { Attribute.create (mkKey [ "Sales"; "Product"; "Name" ]) (mkName "Name") Text with Column = col "NAME"; IsMandatory = true } ]
      References = []; Indexes = []; Description = None; IsActive = true
      Triggers = []; ColumnChecks = []; ExtendedProperties = [] }

let private e2eCatalog () : Catalog =
    let m : Module =
        { SsKey = mkKey [ "Sales" ]; Name = mkName "Sales"
          Kinds = [ mkCountry (); mkRole (); mkProduct () ]; IsActive = true; ExtendedProperties = [] }
    { Modules = [ m ]; Sequences = [] }

let private migrationCtx () : MigrationDependencyContext =
    { Rows =
        [ { KindKey = roleKey; Identifier = mkKey [ "Sales"; "Role"; "Row"; "Admin" ];   Values = Map.ofList [ mkName "Id", "1"; mkName "Label", "Administrator" ] }
          { KindKey = roleKey; Identifier = mkKey [ "Sales"; "Role"; "Row"; "Auditor" ]; Values = Map.ofList [ mkName "Id", "2"; mkName "Label", "Auditor" ] } ] }

let private bootstrapRows () : Map<SsKey, StaticRow list> =
    Map.ofList
        [ productKey,
          [ { Identifier = mkKey [ "Sales"; "Product"; "Row"; "Widget" ]; Values = Map.ofList [ mkName "Id", "1"; mkName "Name", "Widget" ] }
            { Identifier = mkKey [ "Sales"; "Product"; "Row"; "Gadget" ]; Values = Map.ofList [ mkName "Id", "2"; mkName "Name", "Gadget" ] }
            { Identifier = mkKey [ "Sales"; "Product"; "Row"; "Gizmo" ];  Values = Map.ofList [ mkName "Id", "3"; mkName "Name", "Gizmo" ] } ] ]

[<Xunit.Collection("Docker-SqlServer")>]
type SsdtArtifactDeployE2ETests(fixture: EphemeralContainerFixture) =
    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``E2E: dacpac publish + post-deploy (static+migration) + separate bootstrap MERGE loads every lane idempotently`` () =
        if not (Deploy.Docker.ensureRunning ()) then
            printfn "SKIP E2E SSDT deploy: Docker daemon not reachable."
        else
            let catalog = e2eCatalog ()
            let policy = { Policy.empty with Emission = EmissionPolicy.combined }
            let bundle =
                match
                    DataEmissionComposer.composeRenderedBundleWithBootstrap
                        policy catalog Profile.empty (migrationCtx ()) (bootstrapRows ()) UserRemapContext.empty
                with
                | Ok b -> b
                | Error e -> failwithf "data compose failed: %A" e
            // The composer must route each lane — a loud failure here beats a
            // confusing deploy error later.
            Assert.False(System.String.IsNullOrWhiteSpace bundle.StaticSeeds, "StaticSeeds lane is empty")
            Assert.False(System.String.IsNullOrWhiteSpace bundle.MigrationData, "MigrationData lane is empty")
            Assert.False(System.String.IsNullOrWhiteSpace bundle.Bootstrap, "Bootstrap lane is empty")
            let dacpac =
                match DacpacEmitter.emit catalog with
                | Ok bytes -> bytes
                | Error es -> failwithf "dacpac emit failed: %A" es
            // Post-deploy carries static seeds + migration (NOT bootstrap).
            let postDeploy =
                PostDeployEmitter.renderInlined
                    [ "StaticSeeds", bundle.StaticSeeds; "MigrationData", bundle.MigrationData ]
            let scalar (cnn: SqlConnection) (sql: string) : Task<string> =
                task {
                    use cmd = cnn.CreateCommand()
                    cmd.CommandText <- sql
                    let! v = cmd.ExecuteScalarAsync()
                    return string v
                }
            TaskSync.run (fun () ->
                fixture.WithEphemeralDatabase "SsdtE2E" (fun cnn connStr ->
                    task {
                        // 1) consume the SSDT dacpac → publish the schema (DacFx Deploy)
                        let dbName = SqlConnectionStringBuilder(connStr).InitialCatalog
                        use stream = new MemoryStream(dacpac)
                        use package = DacPackage.Load stream
                        (DacServices connStr).Deploy(package, dbName, true, DacDeployOptions())
                        // 2) post-deployment data: static seeds + migration
                        do! Deploy.executeBatch cnn postDeploy
                        // 3) bootstrap MERGE — the separate post-publish step
                        do! Deploy.executeBatch cnn bundle.Bootstrap
                        // every lane landed (these COUNTs throw if the dacpac didn't create the table)
                        let! cCountry = scalar cnn "SELECT COUNT(*) FROM [dbo].[OSUSR_E2E_COUNTRY];"
                        let! cRole    = scalar cnn "SELECT COUNT(*) FROM [dbo].[OSUSR_E2E_ROLE];"
                        let! cProduct = scalar cnn "SELECT COUNT(*) FROM [dbo].[OSUSR_E2E_PRODUCT];"
                        Assert.Equal("2", cCountry)   // static seeds
                        Assert.Equal("2", cRole)      // migration
                        Assert.Equal("3", cProduct)   // bootstrap
                        let! adminLabel = scalar cnn "SELECT [LABEL] FROM [dbo].[OSUSR_E2E_ROLE] WHERE [ID] = 1;"
                        Assert.Equal("Administrator", adminLabel)
                        // 4) idempotency: re-run post-deploy + bootstrap → counts unchanged (MERGE is upsert)
                        do! Deploy.executeBatch cnn postDeploy
                        do! Deploy.executeBatch cnn bundle.Bootstrap
                        let! cCountry2 = scalar cnn "SELECT COUNT(*) FROM [dbo].[OSUSR_E2E_COUNTRY];"
                        let! cProduct2 = scalar cnn "SELECT COUNT(*) FROM [dbo].[OSUSR_E2E_PRODUCT];"
                        Assert.Equal("2", cCountry2)
                        Assert.Equal("3", cProduct2)
                    }))
