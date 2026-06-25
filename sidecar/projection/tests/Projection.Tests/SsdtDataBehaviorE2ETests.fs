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
// And two STRUCTURAL constraints proven ENFORCED post-deploy:
//   • a composite PK (Id keyed on two columns) — the MERGE keys on both, and a
//     duplicate pair is rejected.
//   • a UNIQUE index — it deploys and a duplicate value is rejected.
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

// ---- composite-PK fixture: OrderLine keyed on (OrderId, ProductId)
let private orderLineKey = mkKey [ "Sales"; "OrderLine" ]
let private orderLineCatalog () : Catalog =
    let row o p q =
        { Identifier = mkKey [ "Sales"; "OrderLine"; "Row"; o + "-" + p ]
          Values = Map.ofList [ mkName "OrderId", o; mkName "ProductId", p; mkName "Qty", q ] }
    let kind : Kind =
        { SsKey = orderLineKey; Name = mkName "OrderLine"; Origin = Native
          Modality = [ Static [ row "1" "10" "5"; row "1" "20" "3" ] ]
          Physical = mkTableId "dbo" "OSUSR_E2E_ORDERLINE"
          Attributes =
            [ { Attribute.create (mkKey [ "Sales"; "OrderLine"; "OrderId" ]) (mkName "OrderId") Integer with Column = col "ORDERID"; IsPrimaryKey = true; IsMandatory = true }
              { Attribute.create (mkKey [ "Sales"; "OrderLine"; "ProductId" ]) (mkName "ProductId") Integer with Column = col "PRODUCTID"; IsPrimaryKey = true; IsMandatory = true }
              { Attribute.create (mkKey [ "Sales"; "OrderLine"; "Qty" ]) (mkName "Qty") Integer with Column = col "QTY"; IsMandatory = true } ]
          References = []; Indexes = []; Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
    { Modules = [ { SsKey = mkKey [ "Sales" ]; Name = mkName "Sales"; Kinds = [ kind ]; IsActive = true; ExtendedProperties = [] } ]; Sequences = [] }

// ---- unique-index fixture: Coupon with a UNIQUE index on Code
let private couponKey = mkKey [ "Sales"; "Coupon" ]
let private couponCatalog () : Catalog =
    let codeKey = mkKey [ "Sales"; "Coupon"; "Code" ]
    let row idv code =
        { Identifier = mkKey [ "Sales"; "Coupon"; "Row"; code ]
          Values = Map.ofList [ mkName "Id", idv; mkName "Code", code ] }
    let kind : Kind =
        { SsKey = couponKey; Name = mkName "Coupon"; Origin = Native
          Modality = [ Static [ row "1" "SAVE10"; row "2" "SAVE20" ] ]
          Physical = mkTableId "dbo" "OSUSR_E2E_COUPON"
          Attributes =
            [ { Attribute.create (mkKey [ "Sales"; "Coupon"; "Id" ]) (mkName "Id") Integer with Column = col "ID"; IsPrimaryKey = true; IsMandatory = true }
              // bounded length → NVARCHAR(50): a MAX column can't be an index key
              { Attribute.create codeKey (mkName "Code") Text with Column = col "CODE"; IsMandatory = true; Length = Some 50 } ]
          References = []
          Indexes =
            [ { Index.create (mkKey [ "Sales"; "Coupon"; "UQ_Code" ]) (mkName "UQ_COUPON_CODE") (IndexColumn.ascendingList [ codeKey ]) with
                  Uniqueness = IndexUniqueness.Unique } ]
          Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
    { Modules = [ { SsKey = mkKey [ "Sales" ]; Name = mkName "Sales"; Kinds = [ kind ]; IsActive = true; ExtendedProperties = [] } ]; Sequences = [] }

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

    [<Fact>]
    member _.``E2E: composite-PK kind deploys, the MERGE keys on both columns, and a duplicate pair is rejected`` () =
        if not (Deploy.Docker.ensureRunning ()) then
            printfn "SKIP composite-PK E2E: Docker daemon not reachable."
        else
            let catalog = orderLineCatalog ()
            let policy = { Policy.empty with Emission = EmissionPolicy.combined }
            let seeds = staticSeeds policy catalog
            TaskSync.run (fun () ->
                fixture.WithEphemeralDatabase "CompositePk" (fun cnn connStr ->
                    task {
                        deploySchema connStr catalog
                        do! Deploy.executeBatch cnn seeds
                        let! cnt = scalar cnn "SELECT COUNT(*) FROM [dbo].[OSUSR_E2E_ORDERLINE];"
                        Assert.Equal("2", cnt)                  // both composite-keyed rows seeded
                        // the composite PK (OrderId, ProductId) is ENFORCED — (1,10) already exists
                        let! threw =
                            task {
                                try
                                    do! Deploy.executeBatch cnn "INSERT INTO [dbo].[OSUSR_E2E_ORDERLINE] ([ORDERID],[PRODUCTID],[QTY]) VALUES (1,10,9);"
                                    return false
                                with :? SqlException as ex -> return ex.Number = 2627 || ex.Number = 2601
                            }
                        Assert.True(threw, "the composite PK must reject a duplicate (OrderId, ProductId)")
                        // the MERGE keys on BOTH columns → idempotent, still 2 rows
                        do! Deploy.executeBatch cnn seeds
                        let! cnt2 = scalar cnn "SELECT COUNT(*) FROM [dbo].[OSUSR_E2E_ORDERLINE];"
                        Assert.Equal("2", cnt2)
                    }))

    [<Fact>]
    member _.``E2E: a UNIQUE index deploys and is enforced — a duplicate value is rejected`` () =
        if not (Deploy.Docker.ensureRunning ()) then
            printfn "SKIP unique-index E2E: Docker daemon not reachable."
        else
            let catalog = couponCatalog ()
            let policy = { Policy.empty with Emission = EmissionPolicy.combined }
            let seeds = staticSeeds policy catalog
            TaskSync.run (fun () ->
                fixture.WithEphemeralDatabase "UniqueIndex" (fun cnn connStr ->
                    task {
                        deploySchema connStr catalog
                        do! Deploy.executeBatch cnn seeds
                        // the unique index deployed on CODE
                        let! isUnique = scalar cnn "SELECT CAST(i.is_unique AS INT) FROM sys.indexes i WHERE i.object_id = OBJECT_ID('dbo.OSUSR_E2E_COUPON') AND i.name = 'UQ_COUPON_CODE';"
                        Assert.Equal("1", isUnique)
                        // and it is ENFORCED — a duplicate CODE is rejected
                        let! threw =
                            task {
                                try
                                    do! Deploy.executeBatch cnn "INSERT INTO [dbo].[OSUSR_E2E_COUPON] ([ID],[CODE]) VALUES (3,'SAVE10');"
                                    return false
                                with :? SqlException as ex -> return ex.Number = 2601 || ex.Number = 2627
                            }
                        Assert.True(threw, "the unique index must reject a duplicate CODE")
                    }))
