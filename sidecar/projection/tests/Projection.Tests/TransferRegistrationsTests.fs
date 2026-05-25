module Projection.Tests.TransferRegistrationsTests

open Xunit
open Projection.Core
open Projection.Adapters.Sql
open Projection.Pipeline

// Pillar-9 TransformRegistry coverage for the Transfer epic. The three
// Transfer transforms — the Ingestion adapter leg, the pure two-phase
// plan, and the Projection-onto-Sink realization — route through the
// registry like every other V2 transform: classified, validated, and
// enumerated in the grand-union RegisteredAllTransforms.all (so the
// existing bidirectional totality property tests cover them too).

let private transferEntries : RegisteredTransformMetadata list =
    [ Ingestion.registeredMetadata
      TransferPlan.registeredMetadata
      Transfer.registeredMetadata ]

[<Fact>]
let ``Transfer transforms bind to the expected stages`` () =
    Assert.Equal(Adapter,  Ingestion.registeredMetadata.StageBinding)
    Assert.Equal(Pipeline, TransferPlan.registeredMetadata.StageBinding)
    Assert.Equal(Emitter,  Transfer.registeredMetadata.StageBinding)

[<Fact>]
let ``every Transfer transform is in the Data domain`` () =
    for rt in transferEntries do
        Assert.Equal(Data, rt.Domain)

[<Fact>]
let ``every Transfer transform site classifies as DataIntent (no operator overlay yet)`` () =
    for rt in transferEntries do
        Assert.NotEmpty rt.Sites
        for site in rt.Sites do
            Assert.Equal(DataIntent, site.Classification)
            Assert.False(System.String.IsNullOrWhiteSpace site.Rationale)

[<Fact>]
let ``Transfer transforms validate through TransformRegistry.create`` () =
    match TransformRegistry.create transferEntries with
    | Ok entries -> Assert.Equal(3, List.length entries)
    | Error es   -> Assert.Fail(sprintf "expected validation; got: %A" es)

[<Fact>]
let ``Transfer transforms are enumerated in the grand-union RegisteredAllTransforms.all`` () =
    let names = RegisteredAllTransforms.all |> List.map (fun rt -> rt.Name) |> Set.ofList
    Assert.True(Set.contains "transferIngestion" names, "transferIngestion missing from the registry")
    Assert.True(Set.contains "transferPlan" names, "transferPlan missing from the registry")
    Assert.True(Set.contains "transferProjection" names, "transferProjection missing from the registry")

[<Fact>]
let ``the whole grand-union registry still validates with the Transfer transforms in it`` () =
    match TransformRegistry.create RegisteredAllTransforms.all with
    | Ok _     -> ()
    | Error es -> Assert.Fail(sprintf "RegisteredAllTransforms.all failed validation: %A" es)
