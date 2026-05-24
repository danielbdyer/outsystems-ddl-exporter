module Projection.Tests.BtReferenceFkFlowTests

open System.Threading.Tasks
open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Pipeline
open Projection.Adapters.Osm
open Projection.Adapters.OssysSql
open Projection.Targets.SSDT

// ---------------------------------------------------------------------
// bt<EspaceSsKey>*<EntitySsKey> reference encoding → foreign keys,
// end-to-end through the live OSSYS rowset extraction path.
//
// OutSystems encodes reference (FK) attributes in `ossys_Entity_Attr.Type`
// as a `bt<espace-guid>*<entity-guid>` binding type. The edge-case seed
// now carries two such references with a NULL `Referenced_Entity_Id`, so
// the rowset extraction CTE (`#RefResolved`) MUST parse the bt-code to
// resolve the target:
//   - Customer.CityId      → City  (same-module:  AppCore → AppCore)
//   - JobRun.TriggeredByUserId → User (cross-module: Ops → SystemUsers)
//
// These tests assert the binding type flows into the V2 relational
// structure (a `Reference` whose `TargetKind` resolves to a real kind)
// and into the emitted SSDT DDL (a `FOREIGN KEY ... REFERENCES` clause).
// Docker-gated like the sibling extraction canary.
// ---------------------------------------------------------------------

let private skipIfNoDocker (label: string) : bool =
    if Deploy.Docker.ensureRunning () then true
    else
        printfn "SKIP %s: Docker daemon not reachable." label
        false

let private extractFromSeed () : Task<Result<Catalog>> =
    task {
        let seed = MetadataExtractionSql.readEdgeCaseSeed()
        let! result =
            Deploy.withBootstrappedDatabase "BtRefFk" seed (fun cnn ->
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

let private allKinds (c: Catalog) : Kind list =
    c.Modules |> List.collect (fun m -> m.Kinds)

let private kindNamed (name: string) (c: Catalog) : Kind =
    allKinds c |> List.find (fun k -> Name.value k.Name = name)

let private withCatalog (label: string) (assertion: Catalog -> unit) : unit =
    if skipIfNoDocker label then
        match TaskSync.run extractFromSeed with
        | Error errors -> Assert.Fail (sprintf "bt-ref extraction failed: %A" errors)
        | Ok catalog   -> assertion catalog

// ---------------------------------------------------------------------
// Relational structure — the bt-code resolves into a Reference whose
// target is a real kind (no dangling), for both same- and cross-module.
// ---------------------------------------------------------------------

[<Fact>]
let ``bt same-module reference resolves Customer.CityId to the City kind`` () =
    withCatalog "bt-ref-same-module" (fun catalog ->
        let customer = kindNamed "Customer" catalog
        let city     = kindNamed "City" catalog
        let cityRef =
            customer.References
            |> List.tryFind (fun r -> Name.value r.Name = "CityId")
        match cityRef with
        | None -> Assert.Fail "expected Customer.CityId reference resolved from the bt-code"
        | Some r -> Assert.Equal(city.SsKey, r.TargetKind))

[<Fact>]
let ``bt cross-module reference resolves JobRun.TriggeredByUserId to the User kind`` () =
    // JobRun lives in Ops (espace 200); the bt-code's espace GUID names
    // SystemUsers (espace 300). Resolving the target proves cross-module
    // bt-reference resolution end-to-end.
    withCatalog "bt-ref-cross-module" (fun catalog ->
        let jobRun = kindNamed "JobRun" catalog
        let user   = kindNamed "User" catalog
        let userRef =
            jobRun.References
            |> List.tryFind (fun r -> Name.value r.Name = "TriggeredByUserId")
        match userRef with
        | None -> Assert.Fail "expected JobRun.TriggeredByUserId reference resolved from the bt-code"
        | Some r ->
            Assert.Equal(user.SsKey, r.TargetKind)
            // The target kind is in a different module than the source.
            let jobRunModule =
                catalog.Modules |> List.find (fun m -> m.Kinds |> List.exists (fun k -> k.SsKey = jobRun.SsKey))
            let userModule =
                catalog.Modules |> List.find (fun m -> m.Kinds |> List.exists (fun k -> k.SsKey = user.SsKey))
            Assert.NotEqual<string>(Name.value jobRunModule.Name, Name.value userModule.Name))

[<Fact>]
let ``bt-resolved reference targets are never dangling`` () =
    // Every Reference's TargetKind must name a kind that exists in the
    // catalog — the structural invariant the topological order + bootstrap
    // cycle-breaking rely on. A bt-code that failed to resolve would
    // synthesize a target key with no matching kind (a dangling edge).
    withCatalog "bt-ref-no-dangling" (fun catalog ->
        let kindKeys = allKinds catalog |> List.map (fun k -> k.SsKey) |> Set.ofList
        for k in allKinds catalog do
            for r in k.References do
                Assert.True(
                    Set.contains r.TargetKind kindKeys,
                    sprintf "reference %s on kind %s dangles (target not in catalog)"
                        (Name.value r.Name) (Name.value k.Name)))

// ---------------------------------------------------------------------
// Emission — the bt-resolved reference becomes a FOREIGN KEY in the DDL.
// ---------------------------------------------------------------------

type private FsResult<'a, 'b> = Microsoft.FSharp.Core.Result<'a, 'b>

[<Fact>]
let ``bt-resolved reference emits a FOREIGN KEY referencing the target table`` () =
    withCatalog "bt-ref-fk-emission" (fun catalog ->
        let enriched =
            (CanonicalizeIdentity.registered.Run catalog |> Lineage.map (fun d -> d.Value)).Value
        match SsdtDdlEmitter.emitSlices enriched with
        | FsResult.Error err -> Assert.Fail (sprintf "emit failed: %A" err)
        | FsResult.Ok artifact ->
            let customer = kindNamed "Customer" enriched
            let body =
                match Map.tryFind customer.SsKey (ArtifactByKind.toMap artifact) with
                | Some f -> f.Body
                | None   -> Assert.Fail "expected a slice for Customer"; ""
            // The bt-resolved CityId reference must materialize as a real
            // FK constraint pointing at City's physical table.
            Assert.Contains("FOREIGN KEY", body)
            Assert.Contains("OSUSR_DEF_CITY", body))
