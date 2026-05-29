module Projection.Tests.TransferRegistrationsTests

open Xunit
open Projection.Core
open Projection.Adapters.Sql
open Projection.Pipeline

// Pillar-9 TransformRegistry coverage for the Transfer epic + the
// converged data-load plan. Post-convergence, `DataLoadPlan.build`
// carries the one `OperatorIntent Insertion` site (`identitySubstitution`)
// for the entire data-load family; the Transfer realization, the
// Ingestion adapter leg, and the Reconciliation acquisition each
// register their own surfaces, classified, validated, and enumerated
// in the grand-union `RegisteredAllTransforms.all`.

let private pureDataIntentEntries : RegisteredTransformMetadata list =
    [ Ingestion.registeredMetadata
      Transfer.registeredMetadata ]

let private allTransferEntries : RegisteredTransformMetadata list =
    pureDataIntentEntries @ [ DataLoadPlan.registeredMetadata; Reconciliation.registeredMetadata ]

[<Fact>]
let ``Transfer transforms bind to the expected stages`` () =
    Assert.Equal(Adapter,  Ingestion.registeredMetadata.StageBinding)
    Assert.Equal(Pipeline, DataLoadPlan.registeredMetadata.StageBinding)
    Assert.Equal(Emitter,  Transfer.registeredMetadata.StageBinding)
    Assert.Equal(Pipeline, Reconciliation.registeredMetadata.StageBinding)

[<Fact>]
let ``the pure-DataIntent core (ingest + project) carries only DataIntent sites in the Data domain`` () =
    for rt in pureDataIntentEntries do
        Assert.Equal(Data, rt.Domain)
        Assert.NotEmpty rt.Sites
        for site in rt.Sites do
            Assert.Equal(DataIntent, site.Classification)

[<Fact>]
let ``DataLoadPlan splits four DataIntent sites from one OperatorIntent Insertion (identitySubstitution)`` () =
    let rt = DataLoadPlan.registeredMetadata
    Assert.Equal(Data, rt.Domain)
    let bySite = rt.Sites |> List.map (fun s -> s.SiteName, s.Classification) |> Map.ofList
    Assert.Equal<Classification>(DataIntent, bySite.["kindOrdering"])
    Assert.Equal<Classification>(DataIntent, bySite.["dispositionClassification"])
    Assert.Equal<Classification>(DataIntent, bySite.["deferredFkSelection"])
    Assert.Equal<Classification>(DataIntent, bySite.["unbreakableCycleDiagnostics"])
    Assert.Equal<Classification>(OperatorIntent Insertion, bySite.["identitySubstitution"])

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
    for n in [ "transferIngestion"; "dataLoadPlan"; "transferProjection"; "transferReconciliation" ] do
        Assert.True(Set.contains n names, sprintf "%s missing from the registry" n)

[<Fact>]
let ``the whole grand-union registry still validates with the Transfer transforms in it`` () =
    match TransformRegistry.create RegisteredAllTransforms.all with
    | Ok _     -> ()
    | Error es -> Assert.Fail(sprintf "RegisteredAllTransforms.all failed validation: %A" es)
