namespace Projection.Tests

// V1_INPUT_DEPRECATION.md §3 — the live-OSSYS model read (primary), V1-free.
// Bootstrap an OSSYS-shaped database from V2's own seed, then resolve the model
// through `ModelResolution.resolveFromConnection` (the same path the synthetic
// flow takes when `modelOssys` is configured): metadata snapshot → rowset
// bundle → Catalog with native SsKey. No osm_model.json, no V1 chain.
// Serial via the Docker-SqlServer collection.

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Adapters.OssysSql

[<Xunit.Collection("Docker-SqlServer")>]
module ModelResolutionDockerTests =

    let private skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else printfn "SKIP %s: Docker daemon not reachable." label; false

    [<Fact>]
    let ``live OSSYS read resolves a non-empty Catalog with native (GUID) SsKey`` () =
        let label = "modelResolutionLiveOssys"
        if not (skipIfNoDocker label) then () else
        TaskSync.run (fun () ->
            task {
                let seed = MetadataExtractionSql.readEdgeCaseSeed()
                let! result =
                    Deploy.withBootstrappedDatabase label seed (fun cnn ->
                        LiveModelRead.fromConnection cnn)
                match result with
                | Error es -> Assert.True(false, sprintf "live OSSYS model read failed: %A" es)
                | Ok catalog ->
                    let kinds = Catalog.allKinds catalog
                    Assert.NotEmpty(kinds)
                    // The rowset path carries identity natively as OssysOriginal
                    // GUIDs (A1-stable), unlike the name-synthesized JSON path —
                    // at least one kind must root in an OssysOriginal key.
                    let nativeIdentity =
                        kinds |> List.exists (fun k ->
                            match SsKey.rootKey k.SsKey with
                            | OssysOriginal _ -> true
                            | _ -> false)
                    Assert.True(nativeIdentity, "expected at least one native (OssysOriginal) SsKey from the live read")
            })
