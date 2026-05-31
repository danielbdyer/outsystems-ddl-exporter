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

let private allTransferEntries : RegisteredTransformMetadata list =
    [ Ingestion.registeredMetadata
      Transfer.registeredMetadata
      DataLoadPlan.registeredMetadata
      Reconciliation.registeredMetadata ]

[<Fact>]
let ``Transfer transforms bind to the expected stages`` () =
    Assert.Equal(Adapter,  Ingestion.registeredMetadata.StageBinding)
    Assert.Equal(Pipeline, DataLoadPlan.registeredMetadata.StageBinding)
    Assert.Equal(Emitter,  Transfer.registeredMetadata.StageBinding)
    Assert.Equal(Pipeline, Reconciliation.registeredMetadata.StageBinding)

[<Fact>]
let ``the Ingestion adapter leg carries only DataIntent sites in the Data domain`` () =
    let rt = Ingestion.registeredMetadata
    Assert.Equal(Data, rt.Domain)
    Assert.NotEmpty rt.Sites
    for site in rt.Sites do
        Assert.Equal(DataIntent, site.Classification)

[<Fact>]
let ``the Transfer realization is DataIntent except the §5.2 AssignedBySink capture (OperatorIntent Insertion)`` () =
    // Bulk/UPDATE realization of a pre-substituted plan is DataIntent; the
    // §5.2 sink-minted-key capture is OperatorIntent Insertion because the
    // remap is discovered DURING the write (not supplied to the plan).
    let rt = Transfer.registeredMetadata
    Assert.Equal(Data, rt.Domain)
    let bySite = rt.Sites |> List.map (fun s -> s.SiteName, s.Classification) |> Map.ofList
    Assert.Equal<Classification>(DataIntent, bySite.["phase1BulkInsert"])
    Assert.Equal<Classification>(DataIntent, bySite.["phase2FkRepoint"])
    Assert.Equal<Classification>(OperatorIntent Insertion, bySite.["assignedKeyCapture"])

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
