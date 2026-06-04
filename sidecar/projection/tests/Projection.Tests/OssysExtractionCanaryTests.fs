module Projection.Tests.OssysExtractionCanaryTests

open System.Threading.Tasks
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Adapters.Osm
open Projection.Adapters.OssysSql

// ---------------------------------------------------------------------
// Chapter 5.0 slice γ + δ + ε — OSSYS extraction canary. End-to-end:
//   1. Apply the carbon-copied OSSYS bootstrap fixture against a fresh
//      per-run database in the warm Docker container (synthetic
//      `dbo.ossys_Espace` / `dbo.ossys_Entity` / `dbo.ossys_Entity_Attr`
//      tables with known edge-case data, plus their referenced physical
//      tables).
//   2. Execute the carbon-copied rowsets-SQL script against the seeded
//      source via `MetadataSnapshotRunner.runAsync`.
//   3. Compose the resulting typed snapshot into a
//      `OssysRowsetTypes.RowsetBundle` via `MetadataSnapshotRunner.toBundle`.
//   4. Parse the bundle into a V2 `Catalog` via `CatalogReader.parse`.
//   5. Assert structural invariants on the produced Catalog: the
//      expected modules / entities / attributes from the fixture
//      survive end-to-end into V2's IR.
//
// This is the canary mockup that lets V2 stand on its own — exercising
// the full live-SQL extraction path against a deterministic synthetic
// source, gated on Docker availability.
// ---------------------------------------------------------------------

let private skipIfNoDocker (label: string) : bool =
    if Deploy.Docker.ensureRunning () then true
    else
        printfn
            "SKIP %s: Docker daemon not reachable. Set DOCKER_HOST or start the daemon to run canary tests."
            label
        false

let private extractFromSeed () : Task<Result<Catalog>> =
    task {
        let seed = MetadataExtractionSql.readEdgeCaseSeed()
        let! result =
            Deploy.withBootstrappedDatabase "OssysCanary" seed (fun cnn ->
                task {
                    let! snapshotResult = MetadataSnapshotRunner.runAsync cnn MetadataSnapshotRunner.defaultParameters
                    match snapshotResult with
                    | Error errors -> return Result.failure errors
                    | Ok snapshot ->
                        let bundle = MetadataSnapshotRunner.toBundle snapshot
                        let! catalog = CatalogReader.parse (CatalogReader.SnapshotRowsets bundle)
                        return catalog
                })
        return result
    }

[<Fact>]
let ``Slice ε canary: OSSYS seed fixture extracts to a V2 Catalog with 3 modules`` () =
    if skipIfNoDocker "ossys-canary-modules" then
        let result = TaskSync.run extractFromSeed
        match result with
        | Error errors ->
            Assert.Fail (sprintf "OSSYS canary extraction failed: %A" errors)
        | Ok catalog ->
            // The seed defines 7 modules: AppCore (100), Ops (200),
            // SystemUsers (300, system), Sales (400), Inventory (500),
            // Integration (600, Extension), RefData (700). The original
            // three are preserved; the comprehensive expansion adds four.
            Assert.Equal(7, List.length catalog.Modules)

[<Fact>]
let ``Slice ε canary: OSSYS seed fixture extracts the AppCore module with 3 entities`` () =
    if skipIfNoDocker "ossys-canary-entities" then
        let result = TaskSync.run extractFromSeed
        match result with
        | Error errors ->
            Assert.Fail (sprintf "OSSYS canary extraction failed: %A" errors)
        | Ok catalog ->
            // AppCore (EspaceId 100) holds: Customer (1000), City (2001),
            // BillingAccount (2002).
            let appCore =
                catalog.Modules
                |> List.find (fun m -> Name.value m.Name = "AppCore")
            Assert.Equal(3, List.length appCore.Kinds)

[<Fact>]
let ``Slice ε canary: OSSYS seed fixture preserves cross-schema BillingAccount external entity`` () =
    if skipIfNoDocker "ossys-canary-billing" then
        let result = TaskSync.run extractFromSeed
        match result with
        | Error errors ->
            Assert.Fail (sprintf "OSSYS canary extraction failed: %A" errors)
        | Ok catalog ->
            // BillingAccount has IsExternal=1 + Physical_Table_Name=BILLING_ACCOUNT;
            // its physical table lives in the `billing` schema.
            let appCore =
                catalog.Modules
                |> List.find (fun m -> Name.value m.Name = "AppCore")
            let billing =
                appCore.Kinds
                |> List.find (fun k -> Name.value k.Name = "BillingAccount")
            Assert.Equal("billing", TableId.schemaText billing.Physical)
            Assert.Equal("BILLING_ACCOUNT", TableId.tableText billing.Physical)

[<Fact>]
let ``Slice ε canary: Customer entity carries six attributes including FK to City`` () =
    if skipIfNoDocker "ossys-canary-customer-attrs" then
        let result = TaskSync.run extractFromSeed
        match result with
        | Error errors ->
            Assert.Fail (sprintf "OSSYS canary extraction failed: %A" errors)
        | Ok catalog ->
            // Customer (EntityId 1000) has 6 attributes per V1 fixture:
            // Id, Email, FirstName, LastName, CityId (FK), LegacyCode.
            // LegacyCode has Is_Active=0; default parameters include it
            // because OnlyActiveAttributes defaults to false.
            let appCore =
                catalog.Modules
                |> List.find (fun m -> Name.value m.Name = "AppCore")
            let customer =
                appCore.Kinds
                |> List.find (fun k -> Name.value k.Name = "Customer")
            Assert.Equal(6, List.length customer.Attributes)
            // CityId carries a `bt<espace>*<entity>` binding type in the
            // `Type` column with a NULL Referenced_Entity_Id; the rowset
            // CTE resolves the bt-code to City and V2 composes a Reference.
            let hasCityRef =
                customer.References
                |> List.exists (fun r ->
                    Name.value r.Name = "CityId" || Name.value r.Name = "City")
            Assert.True(hasCityRef, "expected Customer.CityId reference to City")

[<Fact>]
let ``Slice ε canary: extraction is deterministic across repeated runs`` () =
    if skipIfNoDocker "ossys-canary-determinism" then
        let r1 = TaskSync.run extractFromSeed
        let r2 = TaskSync.run extractFromSeed
        match r1, r2 with
        | Ok c1, Ok c2 ->
            // Each run uses a fresh database (uniqueDatabaseName ensures
            // no cross-run pollution). The extracted Catalog should be
            // byte-identical modulo SsKey synthesis (which is also
            // deterministic).
            Assert.Equal<int>(List.length c1.Modules, List.length c2.Modules)
            // Names + module structure should be identical.
            let names1 = c1.Modules |> List.map (fun m -> Name.value m.Name) |> List.sort
            let names2 = c2.Modules |> List.map (fun m -> Name.value m.Name) |> List.sort
            Assert.Equal<string list>(names1, names2)
        | _ ->
            Assert.Fail (sprintf "OSSYS canary determinism check: r1=%A r2=%A" r1 r2)

[<Fact>]
let ``Slice 5.13.progress-callback canary: progress fires for every observed rowset`` () =
    // Live-extraction test for matrix row 36 — the callback must fire
    // once per result set in the script's observed shape
    // (`ExpectedResultSets = 23` per matrix row 35's empirical
    // observation). Confirms the runner walks the documented contract
    // shape end-to-end with the callback wired.
    if skipIfNoDocker "ossys-canary-progress" then
        let observations = ResizeArray<MetadataSnapshotRunner.ProgressObservation>()
        let onComplete : MetadataSnapshotRunner.OnRowsetComplete =
            fun obs -> lock observations (fun () -> observations.Add obs)
        let seed = MetadataExtractionSql.readEdgeCaseSeed()
        let result =
            TaskSync.run (fun () ->
                Deploy.withBootstrappedDatabase "OssysProgressCanary" seed (fun cnn ->
                    task {
                        return!
                            MetadataSnapshotRunner.runAsyncWithOptions
                                cnn
                                MetadataSnapshotRunner.defaultParameters
                                { MetadataSnapshotRunner.defaultOptions with
                                    OnRowsetComplete = onComplete }
                    }))
        match result with
        | Error errors ->
            Assert.Fail (sprintf "OSSYS progress-callback extraction failed: %A" errors)
        | Ok _ ->
            Assert.Equal(
                MetadataSnapshotRunner.ExpectedResultSets,
                observations.Count)
            // The first 5 observations should be the V2-consumed rowsets
            // by name; subsequent ones are the skipped-N tail.
            let firstFive =
                observations
                |> Seq.take 5
                |> Seq.map (fun o -> o.ResultSetName)
                |> List.ofSeq
            Assert.Equal<string list>(
                [ "modules"; "entities"; "attributes"; "references"; "physicalTables" ],
                firstFive)
            // The next 8 observations are the slice 5.13.ossys-rowsets-cluster
            // lifts (with one SUNSET skip between columnChecks and
            // physColsPresent). They cover matrix rows 11 + 12 + 14 +
            // 15 + 16 + 17 + 18 + 23.
            let liftedFamily =
                observations
                |> Seq.skip 5
                |> Seq.take 9
                |> Seq.map (fun o -> o.ResultSetName)
                |> List.ofSeq
            Assert.Equal<string list>(
                [ "columnReality"; "columnChecks"; "attrCheckJson";
                  "physColsPresent"; "allIdx"; "idxColsMapped";
                  "fkReality"; "fkColumns"; "fkAttrMap" ],
                liftedFamily)

// ----------------------------------------------------------------
// Slice 5.13.ossys-rowsets-cluster — canary tests asserting the
// lifted physical-reflection axes (indexes / triggers / column
// checks) flow into V2's IR via the rowset path. Closes matrix
// rows 12 + 15 + 16 + 23 (the rows with V2 IR consumers ready).
// ----------------------------------------------------------------

[<Fact>]
let ``Slice 5.13.ossys-rowsets-cluster: indexes lift via rowset path (matrix rows 15 + 16)`` () =
    if skipIfNoDocker "ossys-canary-indexes" then
        let result = TaskSync.run extractFromSeed
        match result with
        | Error errors ->
            Assert.Fail (sprintf "OSSYS canary extraction failed: %A" errors)
        | Ok catalog ->
            // The seed (ossys-edge-case.seed.sql) declares:
            //   - IDX_CUSTOMER_EMAIL (filtered unique on Customer.Email)
            //   - IDX_CUSTOMER_NAME (LastName, FirstName; DISABLED)
            //   - IDX_BILLINGACCOUNT_ACCTNUM (unique)
            //
            // The rowset adapter now lifts #AllIdx + #IdxColsMapped
            // into Kind.Indexes via the JOIN logic in
            // CatalogReader.parseIndexRowFor. At least the unique
            // indexes should appear; verify the structure.
            let appCore =
                catalog.Modules
                |> List.find (fun m -> Name.value m.Name = "AppCore")
            let customer =
                appCore.Kinds
                |> List.find (fun k -> Name.value k.Name = "Customer")
            // Customer has at least the two declared user indexes
            // (IDX_CUSTOMER_EMAIL + IDX_CUSTOMER_NAME) — the
            // primary key is auto-named and may or may not appear
            // depending on V1's #AllIdx Kind classification.
            let indexNames =
                customer.Indexes
                |> List.map (fun i -> Name.value i.Name)
                |> Set.ofList
            Assert.Contains("IDX_CUSTOMER_EMAIL", indexNames)
            Assert.Contains("IDX_CUSTOMER_NAME", indexNames)
            // IDX_CUSTOMER_EMAIL is unique + filtered.
            let emailIdx =
                customer.Indexes
                |> List.find (fun i -> Name.value i.Name = "IDX_CUSTOMER_EMAIL")
            Assert.True(IndexUniqueness.isUnique emailIdx.Uniqueness)
            Assert.False(IndexUniqueness.isPrimaryKey emailIdx.Uniqueness)
            match emailIdx.Filter with
            | Some _ -> ()
            | None -> Assert.Fail("IDX_CUSTOMER_EMAIL expected to carry a filter (rowset-path #AllIdx.FilterDefinition)")

[<Fact>]
let ``Slice 5.13.ossys-rowsets-cluster: triggers lift via rowset path (matrix row 23)`` () =
    if skipIfNoDocker "ossys-canary-triggers" then
        let result = TaskSync.run extractFromSeed
        match result with
        | Error errors ->
            Assert.Fail (sprintf "OSSYS canary extraction failed: %A" errors)
        | Ok catalog ->
            // The seed declares TR_OSUSR_XYZ_JOBRUN_AUDIT on the
            // JOBRUN entity (Ops module). The rowset adapter now
            // lifts #Triggers into Kind.Triggers.
            let allTriggers =
                catalog.Modules
                |> List.collect (fun m -> m.Kinds)
                |> List.collect (fun k -> k.Triggers)
                |> List.map (fun t -> Name.value t.Name)
                |> Set.ofList
            Assert.Contains("TR_OSUSR_XYZ_JOBRUN_AUDIT", allTriggers)
