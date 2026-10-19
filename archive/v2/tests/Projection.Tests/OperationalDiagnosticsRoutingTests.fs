module Projection.Tests.OperationalDiagnosticsRoutingTests

open Xunit
open FsCheck
open FsCheck.Xunit
open System.Text.Json.Nodes
open Projection.Core
open Projection.Targets.OperationalDiagnostics
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Chapter 4.3 slices β + γ — Routing + OpportunitiesEmitter +
// ValidationsEmitter + partition property.
//
// Per pre-scope §1.3: the routing table is the single point of decision
// for which artifact each entry lands in. The three emitters share the
// per-kind document writer (`DiagnosticDocument` internal module) and
// differ only in their input filter.
//
// **The chapter signature property** (per chapter open axis 8 +
// pre-scope §1.6): the routing partition — every DiagnosticEntry lands
// in exactly one of the three artifacts. No entry orphaned; no entry
// double-counted.
// ---------------------------------------------------------------------------

type private FsResult<'a, 'b> = Microsoft.FSharp.Core.Result<'a, 'b>

let private mustOk (r: FsResult<'a, EmitError>) : 'a =
    match r with
    | FsResult.Ok v -> v
    | FsResult.Error err ->
        Assert.Fail (sprintf "expected Ok; got %A" err)
        Unchecked.defaultof<'a>

let private requireNode (label: string) (n: JsonNode | null) : JsonNode =
    match Option.ofObj n with
    | Some node -> node
    | None      -> Assert.Fail (sprintf "%s: required JsonNode child was null" label); Unchecked.defaultof<JsonNode>

let private requireArr (label: string) (n: JsonNode | null) : JsonArray =
    (requireNode label n).AsArray()

let private mkEntry (code: string) (ssKey: SsKey option) : DiagnosticEntry =
    { Source = "testPass"
      Severity = DiagnosticSeverity.Warning
      Code = code
      Message = sprintf "entry with code %s" code
      SsKey = ssKey
      Metadata = Map.empty
      SuggestedConfig = None }

// ---------------------------------------------------------------------------
// Routing.route — per-entry classification.
// ---------------------------------------------------------------------------

[<Theory>]
[<InlineData("tightening.unique.opportunity.byEvidence")>]
[<InlineData("tightening.nullability.opportunity.signal")>]
[<InlineData("tightening.foreignKey.opportunity.orphans")>]
let ``Routing.route classifies *.opportunity.* codes as Opportunities`` (code: string) =
    let entry = mkEntry code None
    Assert.Equal (Opportunities, Routing.route entry)

[<Theory>]
[<InlineData("tightening.unique.validation.duplicates")>]
[<InlineData("tightening.nullability.validation.nullSurvey")>]
[<InlineData("tightening.foreignKey.validation.referentialIntegrity")>]
let ``Routing.route classifies *.validation.* codes as Validations`` (code: string) =
    let entry = mkEntry code None
    Assert.Equal (Validations, Routing.route entry)

[<Theory>]
[<InlineData("tightening.unique.keepReason")>]
[<InlineData("adapter.osm.unmappedDeleteRule")>]
[<InlineData("emitter.ssdt.renderFailed")>]
[<InlineData("userFkReflow.noEmail")>]
[<InlineData("anyOtherCode.with.no.match")>]
let ``Routing.route classifies non-opportunity non-validation codes as DecisionLog`` (code: string) =
    let entry = mkEntry code None
    Assert.Equal (DecisionLog, Routing.route entry)

// ---------------------------------------------------------------------------
// Routing.partition — preserves chronological order within each bucket.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Routing.partition splits entries by route into three lists`` () =
    let entries =
        [ mkEntry "tightening.unique.opportunity.byEvidence" None
          mkEntry "tightening.nullability.validation.nullSurvey" None
          mkEntry "adapter.osm.unmappedDeleteRule" None
          mkEntry "tightening.unique.opportunity.duplicates" None
          mkEntry "tightening.nullability.validation.signal" None ]
    let (decisionLog, opportunities, validations) = Routing.partition entries
    Assert.Equal (1, List.length decisionLog)
    Assert.Equal (2, List.length opportunities)
    Assert.Equal (2, List.length validations)

[<Fact>]
let ``Routing.partition preserves chronological order within each bucket`` () =
    let e1 = mkEntry "tightening.unique.opportunity.first"  None
    let e2 = mkEntry "tightening.unique.opportunity.second" None
    let e3 = mkEntry "tightening.unique.opportunity.third"  None
    let (_, opportunities, _) = Routing.partition [ e1; e2; e3 ]
    let codes = opportunities |> List.map (fun e -> e.Code)
    Assert.Equal<string list>
        ([ "tightening.unique.opportunity.first"
           "tightening.unique.opportunity.second"
           "tightening.unique.opportunity.third" ],
         codes)

// ---------------------------------------------------------------------------
// THE chapter-signature property: routing partition.
//
// Every DiagnosticEntry lands in exactly one of the three artifacts. No
// entry orphaned; no entry double-counted. Property test over generated
// entries.
// ---------------------------------------------------------------------------

[<Property>]
let ``Routing partition property: every entry lands in exactly one artifact`` (entries: DiagnosticEntry list) =
    let (decisionLog, opportunities, validations) = Routing.partition entries
    // Total count is preserved.
    let total =
        List.length decisionLog + List.length opportunities + List.length validations
    total = List.length entries

[<Property>]
let ``Routing partition property: the three buckets are disjoint as identities`` (entries: DiagnosticEntry list) =
    let (decisionLog, opportunities, validations) = Routing.partition entries
    // No reference-identity overlap between any pair of buckets.
    // Use the per-bucket Code+Message as a structural identity since
    // F# records have structural equality.
    let toIdSet (lst: DiagnosticEntry list) =
        lst |> List.map (fun e -> e.Code, e.Message) |> Set.ofList
    let dlIds = toIdSet decisionLog
    let oppIds = toIdSet opportunities
    let valIds = toIdSet validations
    Set.isEmpty (Set.intersect dlIds oppIds)
    && Set.isEmpty (Set.intersect dlIds valIds)
    && Set.isEmpty (Set.intersect oppIds valIds)

// ---------------------------------------------------------------------------
// Slice β — OpportunitiesEmitter filters to opportunity entries.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Slice β: OpportunitiesEmitter.emit includes only *.opportunity.* entries`` () =
    let customer = Catalog.allKinds sampleCatalog |> List.head
    let opp1 = mkEntry "tightening.unique.opportunity.byEvidence"   (Some customer.SsKey)
    let opp2 = mkEntry "tightening.nullability.opportunity.signal"  (Some customer.SsKey)
    let val1 = mkEntry "tightening.unique.validation.duplicates"    (Some customer.SsKey)
    let dec1 = mkEntry "tightening.unique.keepReason"               (Some customer.SsKey)
    let artifact =
        OpportunitiesEmitter.emit sampleCatalog [ opp1; val1; opp2; dec1 ]
        |> mustOk
    let doc = ArtifactByKind.toMap artifact |> Map.find customer.SsKey
    let entries = requireArr "entries" doc["entries"]
    // Only the two *.opportunity.* entries land in opportunities.json.
    Assert.Equal (2, entries.Count)

[<Fact>]
let ``Slice β: OpportunitiesEmitter T11 keyset coverage`` () =
    let artifact = OpportunitiesEmitter.emit sampleCatalog [] |> mustOk
    let slices = ArtifactByKind.toMap artifact
    Assert.Equal (List.length (Catalog.allKinds sampleCatalog), Map.count slices)
    for k in Catalog.allKinds sampleCatalog do
        Assert.True (Map.containsKey k.SsKey slices)

[<Fact>]
let ``Slice β: T1 byte-determinism for OpportunitiesEmitter`` () =
    let customer = Catalog.allKinds sampleCatalog |> List.head
    let entries =
        [ mkEntry "tightening.unique.opportunity.a" (Some customer.SsKey)
          mkEntry "tightening.unique.opportunity.b" (Some customer.SsKey) ]
    let r1 = OpportunitiesEmitter.emit sampleCatalog entries |> mustOk
    let r2 = OpportunitiesEmitter.emit sampleCatalog entries |> mustOk
    let d1 = (ArtifactByKind.toMap r1 |> Map.find customer.SsKey).ToJsonString()
    let d2 = (ArtifactByKind.toMap r2 |> Map.find customer.SsKey).ToJsonString()
    Assert.Equal<string> (d1, d2)

// ---------------------------------------------------------------------------
// Slice γ — ValidationsEmitter filters to validation entries.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Slice γ: ValidationsEmitter.emit includes only *.validation.* entries`` () =
    let customer = Catalog.allKinds sampleCatalog |> List.head
    let val1 = mkEntry "tightening.unique.validation.duplicates"        (Some customer.SsKey)
    let val2 = mkEntry "tightening.nullability.validation.signal"       (Some customer.SsKey)
    let opp1 = mkEntry "tightening.foreignKey.opportunity.orphans"      (Some customer.SsKey)
    let dec1 = mkEntry "adapter.osm.unmappedDeleteRule"                 (Some customer.SsKey)
    let artifact =
        ValidationsEmitter.emit sampleCatalog [ val1; opp1; dec1; val2 ]
        |> mustOk
    let doc = ArtifactByKind.toMap artifact |> Map.find customer.SsKey
    let entries = requireArr "entries" doc["entries"]
    Assert.Equal (2, entries.Count)

[<Fact>]
let ``Slice γ: ValidationsEmitter T11 keyset coverage`` () =
    let artifact = ValidationsEmitter.emit sampleCatalog [] |> mustOk
    let slices = ArtifactByKind.toMap artifact
    Assert.Equal (List.length (Catalog.allKinds sampleCatalog), Map.count slices)

[<Fact>]
let ``Slice γ: T1 byte-determinism for ValidationsEmitter`` () =
    let customer = Catalog.allKinds sampleCatalog |> List.head
    let entries =
        [ mkEntry "tightening.unique.validation.first"  (Some customer.SsKey)
          mkEntry "tightening.unique.validation.second" (Some customer.SsKey) ]
    let r1 = ValidationsEmitter.emit sampleCatalog entries |> mustOk
    let r2 = ValidationsEmitter.emit sampleCatalog entries |> mustOk
    let d1 = (ArtifactByKind.toMap r1 |> Map.find customer.SsKey).ToJsonString()
    let d2 = (ArtifactByKind.toMap r2 |> Map.find customer.SsKey).ToJsonString()
    Assert.Equal<string> (d1, d2)

// ---------------------------------------------------------------------------
// Three-sibling commutativity: routing partition + three emitters
// reconstruct the full entry set.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Three-sibling commutativity: union of opportunities+validations+decisionLog covers all entries`` () =
    let customer = Catalog.allKinds sampleCatalog |> List.head
    let entries =
        [ mkEntry "tightening.unique.opportunity.dup"             (Some customer.SsKey)
          mkEntry "tightening.nullability.validation.nullSurvey"  (Some customer.SsKey)
          mkEntry "adapter.osm.unmappedDeleteRule"                (Some customer.SsKey)
          mkEntry "userFkReflow.noEmail"                          (Some customer.SsKey)
          mkEntry "tightening.foreignKey.opportunity.orphans"     (Some customer.SsKey)
          mkEntry "tightening.unique.validation.duplicates"       (Some customer.SsKey) ]
    let oppArtifact = OpportunitiesEmitter.emit sampleCatalog entries |> mustOk
    let valArtifact = ValidationsEmitter.emit  sampleCatalog entries |> mustOk
    let (decisionLogEntries, _, _) = Routing.partition entries
    let dlArtifact = DecisionLogEmitter.emitRouted sampleCatalog decisionLogEntries |> mustOk
    let oppCount =
        (requireArr "entries" (ArtifactByKind.toMap oppArtifact |> Map.find customer.SsKey).["entries"]).Count
    let valCount =
        (requireArr "entries" (ArtifactByKind.toMap valArtifact |> Map.find customer.SsKey).["entries"]).Count
    let dlCount =
        (requireArr "entries" (ArtifactByKind.toMap dlArtifact |> Map.find customer.SsKey).["entries"]).Count
    // Six entries split: 2 opportunities + 2 validations + 2 decision-log.
    Assert.Equal (2, oppCount)
    Assert.Equal (2, valCount)
    Assert.Equal (2, dlCount)
    Assert.Equal (6, oppCount + valCount + dlCount)
