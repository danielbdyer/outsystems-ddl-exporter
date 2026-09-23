namespace Projection.Tests

// The displacement algebra's laws (the data-sink chapter, S4a;
// CHAPTER_SINK_OPEN.md). The FTC pair at the acquisition grain — the seed
// of T19, promoted to the axiom's live witness at S10 — plus the
// CDC-silence law, key-basis honesty, deterministic ordering, and the
// domain classifier's lifecycle-seed-shaped facts.

open Xunit
open FsCheck.Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Adapters.OssysSql

module SinkDisplacementTests =

    let private canonical = SinkDisplacement.canonical
    let private diff = SinkDisplacement.diff
    let private applyAll = SinkDisplacement.applyAll

    // ------------------------------------------------------------------
    // The FTC pair + silence.
    // ------------------------------------------------------------------

    [<Property(MaxTest = 30)>]
    let ``T19 seed: applyAll (diff a b) (canonical a) = canonical b`` (s1: int) (s2: int) =
        let a = OssysSnapshotBuilders.fullyPopulated s1
        let b = OssysSnapshotBuilders.fullyPopulated s2
        applyAll (diff a b) (canonical a) = canonical b

    [<Property(MaxTest = 30)>]
    let ``CDC-silence at acquisition grain: diff a a = []`` (seed: int) =
        let a = OssysSnapshotBuilders.fullyPopulated seed
        diff a a |> List.isEmpty

    [<Fact>]
    let ``the empty-to-populated diff is total: every row appears exactly once`` () =
        let b = OssysSnapshotBuilders.fullyPopulated 3
        let displacements = diff OssysSnapshotBuilders.emptySnapshot b
        // fullyPopulated carries one row per rowset — sixteen appearances.
        Assert.Equal(16, SinkDisplacement.norm displacements)
        Assert.All(displacements, fun d ->
            Assert.Equal(SinkDisplacement.RowChange.Appeared, SinkDisplacement.changeOf d))

    [<Fact>]
    let ``displacements emit in deterministic key order within each table`` () =
        let b = OssysSnapshotBuilders.fullyPopulated 5
        let a = OssysSnapshotBuilders.emptySnapshot
        let keysByTable =
            diff a b
            |> List.groupBy (fun d -> d.Table)
            |> List.map (fun (_, ds) -> ds |> List.map (fun d -> d.KeyText))
        Assert.All(keysByTable, fun keys -> Assert.Equal<string list>(List.sort keys, keys))

    // ------------------------------------------------------------------
    // Key-basis honesty.
    // ------------------------------------------------------------------

    [<Fact>]
    let ``key basis: an SS_Key-bearing entity keys Native; a keyless one keys Positional`` () =
        let withKey = OssysSnapshotBuilders.entityRow 8000 800 "Order" "OSUSR_FUL_ORDER"
        let withoutKey = { OssysSnapshotBuilders.entityRow 8001 800 "Invoice" "OSUSR_FUL_INVOICE" with EntitySsKey = None }
        let a = OssysSnapshotBuilders.snapshotOf [ OssysSnapshotBuilders.moduleRow 800 "Fulfillment" ] [] []
        let b = OssysSnapshotBuilders.snapshotOf [ OssysSnapshotBuilders.moduleRow 800 "Fulfillment" ] [ withKey; withoutKey ] []
        let entityDisplacements =
            diff a b |> List.filter (fun d -> d.Table = SinkDisplacement.SinkTable.Entities)
        Assert.Equal(2, List.length entityDisplacements)
        Assert.Contains(entityDisplacements, fun d ->
            match d.KeyBasis with
            | SinkDisplacement.KeyBasis.Native _ -> true
            | _ -> false)
        Assert.Contains(entityDisplacements, fun d ->
            match d.KeyBasis with
            | SinkDisplacement.KeyBasis.Positional 8001 -> true
            | _ -> false)

    // ------------------------------------------------------------------
    // The domain classifier — the lifecycle seed's shapes, pure.
    // ------------------------------------------------------------------

    let private moduleRows =
        [ OssysSnapshotBuilders.moduleRow 800 "Fulfillment"
          { OssysSnapshotBuilders.moduleRow 900 "FulfillmentExtension" with EspaceKind = Some "Extension" } ]

    let private domainOf (table: SinkDisplacement.SinkTable) (key: string) (displacements: SinkDisplacement.Displacement list) =
        displacements
        |> List.find (fun d -> d.Table = table && d.KeyText = key)
        |> fun d -> d.Domain

    [<Fact>]
    let ``classifier: an IsActive true→false entity flip reads EntityDeactivated (the tombstone)`` () =
        let entity = OssysSnapshotBuilders.entityRow 8001 800 "Invoice" "OSUSR_FUL_INVOICE"
        let a = OssysSnapshotBuilders.snapshotOf moduleRows [ entity ] []
        let b = OssysSnapshotBuilders.snapshotOf moduleRows [ { entity with IsActive = false } ] []
        let key = (SinkDisplacement.diff a b |> List.head).KeyText
        Assert.Equal(Some SinkDisplacement.DomainTransition.EntityDeactivated, domainOf SinkDisplacement.SinkTable.Entities key (diff a b))

    [<Fact>]
    let ``classifier: an EspaceId move reads EntityRehomed with both homes named`` () =
        let entity = OssysSnapshotBuilders.entityRow 8000 800 "Order" "OSUSR_FUL_ORDER"
        let a = OssysSnapshotBuilders.snapshotOf moduleRows [ entity ] []
        let b = OssysSnapshotBuilders.snapshotOf moduleRows [ { entity with EspaceId = 900 } ] []
        let key = (diff a b |> List.head).KeyText
        Assert.Equal(
            Some (SinkDisplacement.DomainTransition.EntityRehomed (800, 900)),
            domainOf SinkDisplacement.SinkTable.Entities key (diff a b))

    [<Fact>]
    let ``classifier: an external entity appearing under an Extension module reads EntityRegisteredExternal (case-insensitive marker)`` () =
        let extensionEntity =
            { OssysSnapshotBuilders.entityRow 9002 900 "Shipment" "OSUSR_FUL_SHIPMENT" with IsExternal = true }
        let lowercaseMarker =
            [ OssysSnapshotBuilders.moduleRow 800 "Fulfillment"
              { OssysSnapshotBuilders.moduleRow 900 "FulfillmentExtension" with EspaceKind = Some "extension" } ]
        let a = OssysSnapshotBuilders.snapshotOf lowercaseMarker [] []
        let b = OssysSnapshotBuilders.snapshotOf lowercaseMarker [ extensionEntity ] []
        let key = (diff a b |> List.head).KeyText
        Assert.Equal(
            Some SinkDisplacement.DomainTransition.EntityRegisteredExternal,
            domainOf SinkDisplacement.SinkTable.Entities key (diff a b))

    [<Fact>]
    let ``classifier: a non-external entity appearing on an already-claimed table reads PhysicalTableSuperseded`` () =
        let original = OssysSnapshotBuilders.entityRow 8003 800 "Carrier" "OSUSR_FUL_CARRIER"
        let usurper = OssysSnapshotBuilders.entityRow 8503 800 "CarrierV2" "OSUSR_FUL_CARRIER"
        let a = OssysSnapshotBuilders.snapshotOf moduleRows [ original ] []
        let b = OssysSnapshotBuilders.snapshotOf moduleRows [ original; usurper ] []
        let displacements = diff a b
        let appeared =
            displacements
            |> List.find (fun d -> SinkDisplacement.changeOf d = SinkDisplacement.RowChange.Appeared)
        Assert.Equal(
            Some (SinkDisplacement.DomainTransition.PhysicalTableSuperseded "OSUSR_FUL_CARRIER"),
            appeared.Domain)

    [<Fact>]
    let ``classifier: attribute Length and DataType moves read AttributeRetyped with the CatalogDiff facets`` () =
        let attr = OssysSnapshotBuilders.attributeRow 80022 8002 "TrackingCode"
        let a = OssysSnapshotBuilders.snapshotOf moduleRows [] [ attr ]
        let b =
            OssysSnapshotBuilders.snapshotOf moduleRows []
                [ { attr with Length = Some 200; DataType = Some "LongText" } ]
        let key = (diff a b |> List.head).KeyText
        match domainOf SinkDisplacement.SinkTable.Attributes key (diff a b) with
        | Some (SinkDisplacement.DomainTransition.AttributeRetyped facets) ->
            Assert.Contains(AttributeFacet.DataType, facets)
            Assert.Contains(AttributeFacet.Length, facets)
        | other -> Assert.Fail (sprintf "expected AttributeRetyped, got %A" other)

    [<Fact>]
    let ``classifier: a capability-bit flip reads ShapeChanged (ossys-schema drift is data)`` () =
        let a = { OssysSnapshotBuilders.emptySnapshot with Capabilities = [ OssysSnapshotBuilders.capabilityRow ] }
        let b = { OssysSnapshotBuilders.emptySnapshot with Capabilities = [ { OssysSnapshotBuilders.capabilityRow with HasOrderNum = false } ] }
        let displacements = diff a b
        let d = Assert.Single displacements
        Assert.Equal(Some SinkDisplacement.DomainTransition.ShapeChanged, d.Domain)

    [<Fact>]
    let ``an unclassified delta still rides the journal (total carriers, Domain = None)`` () =
        let entity = OssysSnapshotBuilders.entityRow 8000 800 "Order" "OSUSR_FUL_ORDER"
        let a = OssysSnapshotBuilders.snapshotOf moduleRows [ entity ] []
        let b = OssysSnapshotBuilders.snapshotOf moduleRows [ { entity with Description = Some "renarrated" } ] []
        let d = Assert.Single (diff a b)
        Assert.Equal(SinkDisplacement.RowChange.Reshaped, SinkDisplacement.changeOf d)
        Assert.Equal(None, d.Domain)
