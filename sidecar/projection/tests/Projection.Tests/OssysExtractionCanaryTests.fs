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
//      `CatalogReader.RowsetBundle` via `MetadataSnapshotRunner.toBundle`.
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
        let result = (extractFromSeed ()).GetAwaiter().GetResult()
        match result with
        | Error errors ->
            Assert.Fail (sprintf "OSSYS canary extraction failed: %A" errors)
        | Ok catalog ->
            // V1 fixture INSERTs 3 modules: AppCore (100), Ops (200), SystemUsers (300).
            // All three carry IsActive=1; AppCore + Ops are user modules; SystemUsers is system.
            Assert.Equal(3, List.length catalog.Modules)

[<Fact>]
let ``Slice ε canary: OSSYS seed fixture extracts the AppCore module with 3 entities`` () =
    if skipIfNoDocker "ossys-canary-entities" then
        let result = (extractFromSeed ()).GetAwaiter().GetResult()
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
        let result = (extractFromSeed ()).GetAwaiter().GetResult()
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
            Assert.Equal("billing", billing.Physical.Schema)
            Assert.Equal("BILLING_ACCOUNT", billing.Physical.Table)

[<Fact>]
let ``Slice ε canary: Customer entity carries six attributes including FK to City`` () =
    if skipIfNoDocker "ossys-canary-customer-attrs" then
        let result = (extractFromSeed ()).GetAwaiter().GetResult()
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
            // CityId attribute carries Referenced_Entity_Id=2001 (City);
            // V2's RowsetBundle composes this into a Reference.
            let hasCityRef =
                customer.References
                |> List.exists (fun r ->
                    Name.value r.Name = "CityId" || Name.value r.Name = "City")
            Assert.True(hasCityRef, "expected Customer.CityId reference to City")

[<Fact>]
let ``Slice ε canary: extraction is deterministic across repeated runs`` () =
    if skipIfNoDocker "ossys-canary-determinism" then
        let r1 = (extractFromSeed ()).GetAwaiter().GetResult()
        let r2 = (extractFromSeed ()).GetAwaiter().GetResult()
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
