module Projection.Tests.SinkDiffViewTests

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Adapters.OssysSql

// The Catalog-grain view over the displacement journal (the data-sink
// chapter, S10): `SinkDiffView.catalogDiffOf` parses both editions through
// the exact sink-read pipeline and diffs with `CatalogDiff.between`. The
// laws here: the view's CDC-silence, the ERASURE-WITNESS INEQUALITY
// (`CatalogDiff.norm view ≤ SinkDisplacement.norm journal` — the view can
// understate the journal, never invent), and R6's sequence-identity check
// (one synthesis convention on both sides of any sink diff, so a shared
// sequence never reads as an add/remove pair).
//
// The editions are PARSEABLE builder snapshots (module + entities +
// identifier attributes, no dangling references) — the codec-oriented
// `fullyPopulated` builder deliberately does not parse (dangling reference
// rows), so the view laws run over this hand-built family instead.

let private baseEdition () =
    OssysSnapshotBuilders.snapshotOf
        [ OssysSnapshotBuilders.moduleRow 800 "Fulfillment" ]
        [ OssysSnapshotBuilders.entityRow 8000 800 "Order" "OSUSR_FUL_ORDER"
          OssysSnapshotBuilders.entityRow 8001 800 "Invoice" "OSUSR_FUL_INVOICE" ]
        [ OssysSnapshotBuilders.identifierRow 80001 8000
          OssysSnapshotBuilders.identifierRow 80011 8001 ]

let private withSequence (s: MetadataSnapshotRunner.MetadataSnapshot) =
    { s with Sequences = [ OssysSnapshotBuilders.sequenceRow "dbo" "SEQ_TICKET" ] }

/// The mutation family the inequality is exercised over — each edition a
/// small, parseable displacement from base: a tombstone flip, a logical
/// rename, an entity removal, an entity addition, a sequence addition.
let private mutations () : (string * MetadataSnapshotRunner.MetadataSnapshot) list =
    let b = baseEdition ()
    [ "tombstone",
        { b with
            Entities = b.Entities |> List.map (fun e -> if e.EntityId = 8001 then { e with IsActive = false } else e) }
      "rename",
        { b with
            Entities = b.Entities |> List.map (fun e -> if e.EntityId = 8001 then { e with EntityName = "InvoiceArchive" } else e) }
      "removal",
        { b with
            Entities = b.Entities |> List.filter (fun e -> e.EntityId <> 8001)
            Attributes = b.Attributes |> List.filter (fun a -> a.EntityId <> 8001) }
      "addition",
        { b with
            Entities = b.Entities @ [ OssysSnapshotBuilders.entityRow 8002 800 "Shipment" "OSUSR_FUL_SHIPMENT" ]
            Attributes = b.Attributes @ [ OssysSnapshotBuilders.identifierRow 80021 8002 ] }
      "sequence-added", withSequence b ]

let private viewOf a b : CatalogDiff =
    match TaskSync.run (fun () -> SinkDiffView.catalogDiffOf a b) with
    | Ok d -> d
    | Error es -> failwithf "the view refused to parse an edition: %A" es

[<Fact>]
let ``the view's CDC-silence: catalogDiffOf a a has norm 0`` () =
    let a = baseEdition ()
    Assert.Equal(0, CatalogDiff.norm (viewOf a a))

[<Fact>]
let ``erasure witness: the view's norm never exceeds the journal's displacement count (per mutation)`` () =
    let a = baseEdition ()
    for (label, b) in mutations () do
        let viewNorm = CatalogDiff.norm (viewOf a b)
        let journalNorm = SinkDisplacement.norm (SinkDisplacement.diff a b)
        Assert.True(
            viewNorm <= journalNorm,
            sprintf "%s: view norm %d exceeds journal norm %d — the view invented a change the journal never witnessed" label viewNorm journalNorm)
        // And each mutation is REAL at both grains (no vacuous rows).
        Assert.True(journalNorm > 0, sprintf "%s: the journal saw nothing" label)
        Assert.True(viewNorm > 0, sprintf "%s: the view saw nothing" label)

[<Fact>]
let ``R6 sequence identity: one synthesis convention on both sides — a shared sequence never reads as add+remove`` () =
    // The sink read rides the ROWSET path exclusively (toBundle → parse
    // SnapshotRowsets), whose sequence SsKey convention is
    // OSSYS_SEQUENCE "schema.name" (OssysRowsetReader) — the OS_SEQ
    // composite lives only on the V1-json path (OssysTranslation), which
    // no sink diff leg ever takes. So a sequence present in BOTH editions
    // carries ONE identity and zero sequence-channel counts.
    let a = withSequence (baseEdition ())
    let b =
        { withSequence (baseEdition ()) with
            Entities = (baseEdition ()).Entities |> List.map (fun e -> if e.EntityId = 8001 then { e with IsActive = false } else e) }
    let d = viewOf a b
    let c = CatalogDiff.channelCounts d
    Assert.Equal(0, c.AddedSequences)
    Assert.Equal(0, c.RemovedSequences)
    Assert.Equal(0, c.RenamedSequences)
    Assert.Equal(0, c.ChangedSequences)
