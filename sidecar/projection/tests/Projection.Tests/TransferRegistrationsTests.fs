module Projection.Tests.TransferRegistrationsTests

open Xunit
open Projection.Core
open Projection.Adapters.Sql
open Projection.Pipeline

// Pillar-9 TransformRegistry coverage for the Transfer epic. The DataIntent
// core — Ingestion adapter leg, the pure two-phase plan, the Projection-
// onto-Sink realization — plus the operator-intent reconciliation ruleset
// (Slice C′), all routed through the registry: classified, validated, and
// enumerated in the grand-union RegisteredAllTransforms.all (so the existing
// bidirectional totality property tests cover them too).

let private dataIntentEntries : RegisteredTransformMetadata list =
    [ Ingestion.registeredMetadata
      TransferPlan.registeredMetadata
      Transfer.registeredMetadata ]

let private allTransferEntries : RegisteredTransformMetadata list =
    dataIntentEntries @ [ Reconciliation.registeredMetadata ]

[<Fact>]
let ``Transfer transforms bind to the expected stages`` () =
    Assert.Equal(Adapter,  Ingestion.registeredMetadata.StageBinding)
    Assert.Equal(Pipeline, TransferPlan.registeredMetadata.StageBinding)
    Assert.Equal(Emitter,  Transfer.registeredMetadata.StageBinding)
    Assert.Equal(Pipeline, Reconciliation.registeredMetadata.StageBinding)

[<Fact>]
let ``the DataIntent core (ingest / plan / project) carries only DataIntent sites in the Data domain`` () =
    for rt in dataIntentEntries do
        Assert.Equal(Data, rt.Domain)
        Assert.NotEmpty rt.Sites
        for site in rt.Sites do
            Assert.Equal(DataIntent, site.Classification)

[<Fact>]
let ``the reconciliation ruleset is an OperatorIntent Selection site (Identity domain)`` () =
    let rt = Reconciliation.registeredMetadata
    Assert.Equal(Identity, rt.Domain)
    Assert.NotEmpty rt.Sites
    for site in rt.Sites do
        Assert.Equal(OperatorIntent Selection, site.Classification)
        Assert.False(System.String.IsNullOrWhiteSpace site.Rationale)

[<Fact>]
let ``Transfer transforms validate through TransformRegistry.create`` () =
    match TransformRegistry.create allTransferEntries with
    | Ok entries -> Assert.Equal(4, List.length entries)
    | Error es   -> Assert.Fail(sprintf "expected validation; got: %A" es)

[<Fact>]
let ``Transfer transforms are enumerated in the grand-union RegisteredAllTransforms.all`` () =
    let names = RegisteredAllTransforms.all |> List.map (fun rt -> rt.Name) |> Set.ofList
    for n in [ "transferIngestion"; "transferPlan"; "transferProjection"; "transferReconciliation" ] do
        Assert.True(Set.contains n names, sprintf "%s missing from the registry" n)

[<Fact>]
let ``the whole grand-union registry still validates with the Transfer transforms in it`` () =
    match TransformRegistry.create RegisteredAllTransforms.all with
    | Ok _     -> ()
    | Error es -> Assert.Fail(sprintf "RegisteredAllTransforms.all failed validation: %A" es)
